using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OC.AutoLink.Models;
using OC.AutoLink.Persistence;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace OC.AutoLink.Registry;

/// <inheritdoc />
public sealed class KeywordRegistry : IKeywordRegistry
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly ILogger<KeywordRegistry> _logger;

    private readonly Lock _lock = new();
    private KeywordSnapshot? _snapshot;
    private volatile bool _dirty = true;

    public KeywordRegistry(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AutoLinkOptions> options,
        ILogger<KeywordRegistry> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public KeywordSnapshot Current
    {
        get
        {
            KeywordSnapshot? current = _snapshot;
            if (!_dirty && current is not null)
            {
                return current;
            }

            lock (_lock)
            {
                if (!_dirty && _snapshot is not null)
                {
                    return _snapshot;
                }

                KeywordSnapshot rebuilt = Build();

                // Only swap when the content actually changed. Publishing an unrelated edit on a target page
                // rebuilds to an identical hash, so the stamp holds still and nothing downstream is invalidated.
                if (_snapshot is not null && string.Equals(_snapshot.Stamp, rebuilt.Stamp, StringComparison.Ordinal))
                {
                    _dirty = false;
                    return _snapshot;
                }

                _logger.LogInformation(
                    "Auto-link keyword registry rebuilt: {Cultures} culture set(s), {Count} keyword(s), {Conflicts} unsettled conflict(s), stamp {Stamp}.",
                    rebuilt.Cultures.Count,
                    rebuilt.Cultures.Values.Sum(c => c.Targets.Count),
                    rebuilt.Cultures.Values.Sum(c => c.Conflicts.Count),
                    rebuilt.Stamp);

                _snapshot = rebuilt;
                _dirty = false;
                return rebuilt;
            }
        }
    }

    /// <inheritdoc />
    public void Invalidate() => _dirty = true;

    private KeywordSnapshot Build()
    {
        AutoLinkOptions options = _options.CurrentValue;
        var sets = new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // ITagQuery is scoped, so the singleton registry resolves it per rebuild rather than holding one.
            using IServiceScope scope = _scopeFactory.CreateScope();

            var umbracoContextFactory = scope.ServiceProvider.GetRequiredService<IUmbracoContextFactory>();
            var tagQuery = scope.ServiceProvider.GetRequiredService<ITagQuery>();
            var urlProvider = scope.ServiceProvider.GetRequiredService<IPublishedUrlProvider>();
            var contentService = scope.ServiceProvider.GetRequiredService<IContentService>();
            var languageService = scope.ServiceProvider.GetRequiredService<ILanguageService>();
            var mappingStore = scope.ServiceProvider.GetRequiredService<IKeywordMappingStore>();
            var suppressionStore = scope.ServiceProvider.GetRequiredService<IKeywordSuppressionStore>();

            IReadOnlyList<KeywordMapping> allMappings = mappingStore.GetAll();
            IReadOnlyList<KeywordSuppression> allSuppressions = suppressionStore.GetAll();

            // Blocking on the async language service is acceptable here: a rebuild happens on a keyword change,
            // not per render, and there is no synchronisation context to deadlock against.
            List<string> cultures = languageService
                .GetAllAsync()
                .GetAwaiter()
                .GetResult()
                .Select(language => language.IsoCode)
                .ToList();

            // Rebuilds are triggered from notification handlers as well as from renders, so there is not
            // always an ambient context to resolve URLs against.
            using UmbracoContextReference contextReference = umbracoContextFactory.EnsureUmbracoContext();

            // Fetched once and reused for every culture: the invariant claimants are the same content whichever
            // language is being built, and only the URLs resolved from them differ.
            List<IPublishedContent> invariantClaimants =
                tagQuery.GetContentByTagGroup(options.TagGroup, null).ToList();

            // The invariant set. On a site whose keyword property does not vary this is the whole story; on one
            // that does, the culture-free tags query returns nothing and this set is simply empty.
            foreach (string culture in cultures.Prepend(KeywordSnapshot.InvariantCulture))
            {
                sets[culture] = BuildSet(
                    culture,
                    options,
                    tagQuery,
                    urlProvider,
                    contentService,
                    invariantClaimants,
                    allMappings,
                    allSuppressions);
            }
        }
        catch (Exception ex)
        {
            // A failed rebuild must not take the site down; render unlinked instead.
            _logger.LogError(ex, "Failed to build the auto-link keyword registry. Rendering without auto-links.");
            return KeywordSnapshot.Empty;
        }

        return new KeywordSnapshot(sets, ComputeStamp(sets));
    }

    /// <summary>
    /// Builds one culture's keyword set.
    /// </summary>
    private CultureKeywordSet BuildSet(
        string culture,
        AutoLinkOptions options,
        ITagQuery tagQuery,
        IPublishedUrlProvider urlProvider,
        IContentService contentService,
        IReadOnlyList<IPublishedContent> invariantClaimants,
        IReadOnlyList<KeywordMapping> allMappings,
        IReadOnlyList<KeywordSuppression> allSuppressions)
    {
        var candidates = new Dictionary<string, List<KeywordCandidate>>(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, KeywordTarget>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<KeywordConflict>();

        Dictionary<string, KeywordMapping> mappings = KeywordMapping.InForce(allMappings, culture);
        IReadOnlyDictionary<string, IReadOnlyList<KeywordSuppression>> suppressions =
            SuppressionsFor(allSuppressions, culture);

        // Invariant tags apply to every culture, so a site mixing varying and non-varying target doctypes resolves
        // both. Duplicate claimants are deduplicated by node key. The invariant claimants are passed in because they
        // are the same content for every culture — only the URLs they resolve to differ.
        CollectCandidates(options, urlProvider, candidates, invariantClaimants, KeywordSnapshot.InvariantCulture, culture);

        if (culture.Length > 0)
        {
            CollectCandidates(
                options,
                urlProvider,
                candidates,
                tagQuery.GetContentByTagGroup(options.TagGroup, culture).ToList(),
                culture,
                culture);
        }

        // Sorted once, after both passes: stable order so the FirstByUrl fallback and the backoffice list do not
        // shuffle between rebuilds.
        foreach (List<KeywordCandidate> claimants in candidates.Values)
        {
            claimants.Sort((a, b) => string.CompareOrdinal(a.Url, b.Url));
        }

        IEnumerable<string> keywords = candidates.Keys.Union(mappings.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (string keyword in keywords)
        {
            List<KeywordCandidate> claimants = candidates.TryGetValue(keyword, out List<KeywordCandidate>? found)
                ? found
                : [];

            KeywordTarget? resolved = Resolve(
                keyword, claimants, mappings, options, urlProvider, contentService, conflicts, culture);

            if (resolved is not null)
            {
                targets[keyword] = resolved;
            }
        }

        foreach ((string keyword, string url) in options.DebugKeywords)
        {
            string trimmed = keyword.Trim();
            if (trimmed.Length > 0)
            {
                targets[trimmed] = new KeywordTarget(trimmed, Guid.Empty, url, trimmed, KeywordSource.Debug);
            }
        }

        IReadOnlyDictionary<string, IReadOnlyList<KeywordCandidate>> frozenCandidates = candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<KeywordCandidate>)pair.Value,
            StringComparer.OrdinalIgnoreCase);

        // Contested keywords go into the matcher even though they resolve to nothing, so they still claim their
        // span. Regex.Matches is non-overlapping and the alternation is longest first, so a shorter keyword cannot
        // match inside a contested phrase. Without this, dropping "content editor" for being contested lets
        // "editor" link the same words to a third page that was never a candidate.
        var matchable = new HashSet<string>(targets.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (KeywordConflict conflict in conflicts)
        {
            matchable.Add(conflict.Keyword);
        }

        return new CultureKeywordSet(
            culture,
            targets,
            frozenCandidates,
            conflicts,
            suppressions,
            matchable.Count == 0 ? null : KeywordMatcher.Build(matchable));
    }

    /// <summary>
    /// The suppression rows in force for a culture, grouped by keyword. A row with no culture applies to all of them.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<KeywordSuppression>> SuppressionsFor(
        IReadOnlyList<KeywordSuppression> allSuppressions,
        string culture) =>
        allSuppressions
            .Where(row => row.IsAllCultures
                          || string.Equals(row.Culture, culture, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.Keyword, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<KeywordSuppression>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Walks the tag group and records every page claiming every keyword. Nothing is discarded here — the original
    /// version dropped the second claimant on the floor, which is the bug the mapping layer exists to fix.
    /// </summary>
    /// <param name="queryCulture">Culture to query tags for, empty for invariant tags.</param>
    /// <param name="urlCulture">Culture to resolve URLs and names for, the culture of the set being built.</param>
    private static void CollectCandidates(
        AutoLinkOptions options,
        IPublishedUrlProvider urlProvider,
        Dictionary<string, List<KeywordCandidate>> candidates,
        IReadOnlyList<IPublishedContent> claimants,
        string queryCulture,
        string urlCulture)
    {
        string? tagCulture = queryCulture.Length == 0 ? null : queryCulture;
        string? linkCulture = urlCulture.Length == 0 ? null : urlCulture;

        foreach (IPublishedContent content in claimants)
        {
            IEnumerable<string>? keywords =
                content.Value<IEnumerable<string>>(options.KeywordsPropertyAlias, tagCulture);

            if (keywords is null)
            {
                continue;
            }

            string? url = urlProvider.GetUrl(content, UrlMode.Relative, linkCulture);
            if (!IsRoutable(url))
            {
                // Unroutable in this culture: no template, outside a configured hostname, or not published in this
                // language at all. Not a usable target, and linking to another language would be worse.
                continue;
            }

            foreach (string keyword in keywords)
            {
                string trimmed = keyword.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!candidates.TryGetValue(trimmed, out List<KeywordCandidate>? existing))
                {
                    existing = [];
                    candidates[trimmed] = existing;
                }

                if (existing.All(c => c.TargetKey != content.Key))
                {
                    existing.Add(new KeywordCandidate(content.Key, url!, content.Name));
                }
            }
        }
    }

    /// <summary>
    /// Precedence: a manual mapping, then an uncontested tag, then whatever the collision behaviour says.
    /// </summary>
    private KeywordTarget? Resolve(
        string keyword,
        List<KeywordCandidate> claimants,
        Dictionary<string, KeywordMapping> mappings,
        AutoLinkOptions options,
        IPublishedUrlProvider urlProvider,
        IContentService contentService,
        List<KeywordConflict> conflicts,
        string culture)
    {
        if (mappings.TryGetValue(keyword, out KeywordMapping? mapping))
        {
            if (mapping.IsExternal)
            {
                // Revalidated on the way out: a row written by any route other than the API must not be able to
                // put a hostile scheme in an href.
                if (ExternalUrl.TryNormalise(mapping.ExternalUrl, out string? external))
                {
                    return new KeywordTarget(
                        keyword,
                        Guid.Empty,
                        external,
                        mapping.Label is { Length: > 0 } label ? label : ExternalUrl.Describe(external),
                        KeywordSource.External,
                        RelFor(mapping, options));
                }

                _logger.LogWarning(
                    "Auto-link external mapping for {Keyword} is not an absolute http or https URL and was ignored.",
                    keyword);
            }

            KeywordCandidate? chosen = claimants.FirstOrDefault(c => c.TargetKey == mapping.TargetKey);
            if (chosen is not null)
            {
                return new KeywordTarget(keyword, chosen.TargetKey, chosen.Url, chosen.TargetName, KeywordSource.Manual);
            }

            // Mapped to something outside the tag set, so it never came through the tags query.
            KeywordCandidate? direct = ResolveByKey(mapping.TargetKey, urlProvider, contentService, culture);
            if (direct is not null)
            {
                return new KeywordTarget(keyword, direct.TargetKey, direct.Url, direct.TargetName, KeywordSource.Manual);
            }

            // Stale mapping: the target is gone, unpublished, unroutable, or has no version in this culture. Fall
            // through to automatic resolution rather than dropping the keyword, and say so.
            _logger.LogWarning(
                "Auto-link mapping for {Keyword} points at {TargetKey}, which is not a routable published page in culture {Culture}. Falling back to automatic resolution.",
                keyword,
                mapping.TargetKey,
                culture.Length == 0 ? "(invariant)" : culture);
        }

        if (claimants.Count == 1)
        {
            KeywordCandidate only = claimants[0];
            return new KeywordTarget(keyword, only.TargetKey, only.Url, only.TargetName, KeywordSource.Tag);
        }

        if (claimants.Count == 0)
        {
            return null;
        }

        conflicts.Add(new KeywordConflict(keyword, claimants));

        return options.OnUnresolvedCollision == UnresolvedCollisionBehaviour.FirstByUrl
            ? new KeywordTarget(keyword, claimants[0].TargetKey, claimants[0].Url, claimants[0].TargetName, KeywordSource.Tag)
            : null;
    }

    /// <summary>
    /// The rel attribute for an external link: the row's own choice, or the configured default.
    /// </summary>
    private static string? RelFor(KeywordMapping mapping, AutoLinkOptions options)
    {
        bool nofollow = mapping.Nofollow ?? options.ExternalLinkRel.Contains("nofollow", StringComparison.OrdinalIgnoreCase);

        if (!nofollow)
        {
            return null;
        }

        return options.ExternalLinkRel.Length > 0 ? options.ExternalLinkRel : "nofollow";
    }

    /// <summary>
    /// Resolves a mapped page that no tag pointed at.
    /// </summary>
    /// <remarks>
    /// <see cref="IPublishedUrlProvider"/> takes a key directly, which keeps this synchronous — the published
    /// content cache is async-only in v17 and the registry build is not. The content service call is for the
    /// display name alone, only for these mapping-only keywords, on a rebuild that happens rarely.
    /// </remarks>
    private static KeywordCandidate? ResolveByKey(
        Guid targetKey,
        IPublishedUrlProvider urlProvider,
        IContentService contentService,
        string culture)
    {
        string? url = urlProvider.GetUrl(targetKey, UrlMode.Relative, culture.Length == 0 ? null : culture);
        if (!IsRoutable(url))
        {
            return null;
        }

        IContent? content = contentService.GetById(targetKey);
        return new KeywordCandidate(targetKey, url!, content?.Name ?? string.Empty);
    }

    private static bool IsRoutable(string? url) => !string.IsNullOrWhiteSpace(url) && url != "#";

    /// <summary>
    /// Hashes every culture's resolved targets, candidates and suppressions together. Changes only when the
    /// linking behaviour or the choices on offer would actually differ, so a typo fix on a target page does not
    /// move the stamp, while a keyword added in one language does.
    /// </summary>
    private static string ComputeStamp(Dictionary<string, CultureKeywordSet> sets)
    {
        var builder = new StringBuilder();

        foreach ((string culture, CultureKeywordSet set) in sets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(culture).Append('\u001A');

            foreach (KeywordTarget target in set.Targets.Values.OrderBy(t => t.Keyword, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(target.Keyword).Append('\u001F')
                    .Append(target.Url).Append('\u001F')
                    .Append(target.Source).Append('\u001E');
            }

            builder.Append('\u001D');

            foreach ((string keyword, IReadOnlyList<KeywordCandidate> claimants) in set.Candidates
                         .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(keyword).Append('\u001F');

                foreach (KeywordCandidate candidate in claimants)
                {
                    builder.Append(candidate.Url).Append('\u001C');
                }

                builder.Append('\u001E');
            }

            builder.Append('\u001D');

            foreach ((string keyword, IReadOnlyList<KeywordSuppression> rows) in set.Suppressions
                         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(keyword).Append('\u001F');

                foreach (KeywordSuppression row in rows
                             .OrderBy(row => row.PageKey)
                             .ThenBy(row => row.Culture, StringComparer.Ordinal))
                {
                    builder.Append(row.PageKey.ToString("N")).Append(row.Culture).Append('\u001C');
                }

                builder.Append('\u001E');
            }

            builder.Append('\u001A');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash)[..16];
    }
}
