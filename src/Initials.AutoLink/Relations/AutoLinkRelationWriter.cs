using Microsoft.Extensions.Logging;
using Initials.AutoLink.Models;
using Initials.AutoLink.Scanning;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Initials.AutoLink.Relations;

/// <summary>
/// Keeps the auto-link relations in step with what the linker would actually render.
/// </summary>
public interface IAutoLinkRelationWriter
{
    /// <summary>
    /// Reconciles the stored relations against a scan, adding the ones that are missing and removing the ones that
    /// no longer hold. Returns what changed.
    /// </summary>
    AutoLinkRelationChanges Reconcile(AutoLinkScanReport report);

    /// <summary>
    /// Removes every auto-link relation touching a page, in either direction.
    /// </summary>
    /// <param name="pageId">
    /// The integer id, not the key. This runs after the node has gone, and by then a key lookup has nothing left to
    /// resolve against — the cleanup would quietly do nothing. The delete notification hands over the entity, which
    /// still carries its id.
    /// </param>
    int RemoveFor(int pageId);

    /// <summary>The keys of pages currently recorded as linking to a page.</summary>
    IReadOnlyList<int> MentioningPages(Guid pageKey);
}

/// <param name="Added">Relations written.</param>
/// <param name="Removed">Relations that no longer held and were deleted.</param>
public readonly record struct AutoLinkRelationChanges(int Added, int Removed)
{
    public bool Any => Added > 0 || Removed > 0;
}

/// <inheritdoc />
internal sealed class AutoLinkRelationWriter : IAutoLinkRelationWriter
{
    private readonly IRelationService _relationService;
    private readonly IIdKeyMap _idKeyMap;
    private readonly ILogger<AutoLinkRelationWriter> _logger;

    public AutoLinkRelationWriter(
        IRelationService relationService,
        IIdKeyMap idKeyMap,
        ILogger<AutoLinkRelationWriter> logger)
    {
        _relationService = relationService;
        _idKeyMap = idKeyMap;
        _logger = logger;
    }

    /// <inheritdoc />
    public AutoLinkRelationChanges Reconcile(AutoLinkScanReport report)
    {
        IRelationType? relationType = _relationService.GetRelationTypeByAlias(AutoLinkRelation.Alias);

        if (relationType is null)
        {
            _logger.LogWarning(
                "The {Alias} relation type is missing, so auto-link relations were not updated.",
                AutoLinkRelation.Alias);

            return default;
        }

        HashSet<(int Parent, int Child)> wanted = Wanted(report);

        var existing = new Dictionary<(int Parent, int Child), IRelation>();

        foreach (IRelation relation in _relationService.GetAllRelationsByRelationType(relationType.Id) ?? [])
        {
            existing.TryAdd((relation.ParentId, relation.ChildId), relation);
        }

        int added = 0;

        foreach ((int parent, int child) in wanted)
        {
            if (existing.ContainsKey((parent, child)))
            {
                continue;
            }

            _relationService.Relate(parent, child, relationType);
            added++;
        }

        List<IRelation> stale = existing
            .Where(pair => !wanted.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        foreach (IRelation relation in stale)
        {
            _relationService.Delete(relation);
        }

        var changes = new AutoLinkRelationChanges(added, stale.Count);

        if (changes.Any)
        {
            _logger.LogInformation(
                "Auto-link relations reconciled: {Added} added, {Removed} removed, {Total} now recorded.",
                changes.Added,
                changes.Removed,
                wanted.Count);
        }

        return changes;
    }

    /// <inheritdoc />
    public int RemoveFor(int pageId)
    {
        List<IRelation> relations = _relationService
            .GetByParentOrChildId(pageId, AutoLinkRelation.Alias)
            .ToList();

        foreach (IRelation relation in relations)
        {
            _relationService.Delete(relation);
        }

        if (relations.Count > 0)
        {
            _logger.LogInformation(
                "Removed {Count} auto-link relation(s) for deleted page {PageId}.", relations.Count, pageId);
        }

        return relations.Count;
    }

    /// <inheritdoc />
    public IReadOnlyList<int> MentioningPages(Guid pageKey)
    {
        Attempt<int> id = _idKeyMap.GetIdForKey(pageKey, UmbracoObjectTypes.Document);

        if (!id.Success)
        {
            return [];
        }

        return _relationService
            .GetByParentOrChildId(id.Result, AutoLinkRelation.Alias)
            .Where(relation => relation.ChildId == id.Result)
            .Select(relation => relation.ParentId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The relations the report says should exist: mentioning page as parent, target page as child.
    /// </summary>
    private HashSet<(int Parent, int Child)> Wanted(AutoLinkScanReport report)
    {
        var wanted = new HashSet<(int, int)>();
        var ids = new Dictionary<Guid, int?>();

        foreach (ScannedPage page in report.Pages)
        {
            int? mentioningId = Resolve(ids, page.PageKey);

            if (mentioningId is null)
            {
                continue;
            }

            foreach (AutoLinkPlacement placement in page.Placements)
            {
                if (placement.SkipReason is not null)
                {
                    continue;
                }

                if (placement.TargetKey is not { } targetKey || targetKey == Guid.Empty)
                {
                    continue;
                }

                int? targetId = Resolve(ids, targetKey);

                if (targetId is null || targetId == mentioningId)
                {
                    continue;
                }

                wanted.Add((mentioningId.Value, targetId.Value));
            }
        }

        return wanted;
    }

    /// <summary>Guid to integer id, memoised per reconcile since the same pages recur across cultures.</summary>
    private int? Resolve(Dictionary<Guid, int?> cache, Guid key)
    {
        if (cache.TryGetValue(key, out int? cached))
        {
            return cached;
        }

        Attempt<int> attempt = _idKeyMap.GetIdForKey(key, UmbracoObjectTypes.Document);
        int? id = attempt.Success ? attempt.Result : null;

        cache[key] = id;

        return id;
    }
}
