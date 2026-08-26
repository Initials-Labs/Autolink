using Microsoft.Extensions.Logging;
using OC.AutoLink.Models;
using OC.AutoLink.Scanning;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace OC.AutoLink.Relations;

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
/// <remarks>
/// Relations are an <em>audit trail</em>, not the mechanism. Nothing in the render path reads them — the linker
/// resolves from the registry, exactly as before — so a stale or missing relation cannot change what a visitor
/// sees. What it changes is whether Umbraco warns somebody about to delete a page that other pages link to.
/// <para>
/// They are written from a scan rather than as pages render, because a page's links are not known until the linker
/// has been run over it, and doing that on render would mean writing to the database on the front end. The scan
/// already walks every published page and asks the real linker what it would do; reconciling from its report costs
/// a couple of queries on top and cannot disagree with the report the editor is looking at.
/// </para>
/// </remarks>
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
            // The migration has not run, or somebody deleted the type. Not worth failing a scan over: the report is
            // still correct, it just is not being recorded.
            _logger.LogWarning(
                "The {Alias} relation type is missing, so auto-link relations were not updated.",
                AutoLinkRelation.Alias);

            return default;
        }

        HashSet<(int Parent, int Child)> wanted = Wanted(report);

        var existing = new Dictionary<(int Parent, int Child), IRelation>();

        foreach (IRelation relation in _relationService.GetAllRelationsByRelationType(relationType.Id) ?? [])
        {
            // A duplicate pair should not exist, but if one does, keeping the first and letting the rest fall into
            // the removal set is the self-healing answer.
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

        // The mentioning page is the parent, so the pages linking to this one are the parents of the relations
        // where it is the child.
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
    /// <remarks>
    /// Only mentions that actually became links count. A mention sitting in a heading, one past the per-page cap,
    /// or one switched off by hand renders no anchor, so recording it as a dependency would make the delete warning
    /// claim links that are not there.
    /// <para>
    /// Deduplicated across cultures. One page linking to another in both en-GB and en-US is one relationship, and
    /// <c>umbracoRelation</c> has no culture column to tell two of them apart.
    /// </para>
    /// </remarks>
    private HashSet<(int Parent, int Child)> Wanted(AutoLinkScanReport report)
    {
        var wanted = new HashSet<(int, int)>();
        var ids = new Dictionary<Guid, int?>();

        foreach (ScannedPage page in report.Pages)
        {
            // The page doing the mentioning is the parent: it is the one whose output depends on the target.
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

                // An external link has no node behind it, so there is nothing to relate to.
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
