using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Initials.AutoLink.Models;
using Initials.AutoLink.Persistence;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Initials.AutoLink.Registry;

/// <inheritdoc />
internal sealed class KeywordRegistry : IKeywordRegistry
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

                if (_snapshot is not null && string.Equals(_snapshot.Stamp, rebuilt.Stamp, StringComparison.Ordinal))
                {
                    _dirty = false;
                    return _snapshot;
                }

                _logger.LogInformation(
                    "Auto-link keyword registry rebuilt: {Cultures} culture set(s), {Count} keyword(s), stamp {Stamp}.",
                    rebuilt.Cultures.Count,
                    rebuilt.Cultures.Values.Sum(c => c.Targets.Count),
                    rebuilt.Stamp);

                _snapshot = rebuilt;
                _dirty = false;
                return rebuilt;
            }
        }
    }

    /// <inheritdoc />
    public void Invalidate() => _dirty = true;

    /// <summary>
    /// Builds every culture's keyword set from the stored keyword rows.
    /// </summary>
    private KeywordSnapshot Build()
    {
        AutoLinkOptions options = _options.CurrentValue;
        var sets = new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            var umbracoContextFactory = scope.ServiceProvider.GetRequiredService<IUmbracoContextFactory>();
            var urlProvider = scope.ServiceProvider.GetRequiredService<IPublishedUrlProvider>();
            var contentService = scope.ServiceProvider.GetRequiredService<IContentService>();
            var languageService = scope.ServiceProvider.GetRequiredService<ILanguageService>();
            var mappingStore = scope.ServiceProvider.GetRequiredService<IKeywordMappingStore>();
            var suppressionStore = scope.ServiceProvider.GetRequiredService<IKeywordSuppressionStore>();

            IReadOnlyList<KeywordMapping> allMappings = mappingStore.GetAll();
            IReadOnlyList<KeywordSuppression> allSuppressions = suppressionStore.GetAll();

            List<string> cultures = languageService
                .GetAllAsync()
                .GetAwaiter()
                .GetResult()
                .Select(language => language.IsoCode)
                .ToList();

            using UmbracoContextReference contextReference = umbracoContextFactory.EnsureUmbracoContext();

            IReadOnlyDictionary<Guid, IContent> pages = FetchPages(contentService, allMappings);

            foreach (string culture in cultures.Prepend(KeywordSnapshot.InvariantCulture))
            {
                sets[culture] = BuildSet(culture, options, urlProvider, pages, allMappings, allSuppressions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the auto-link keyword registry. Rendering without auto-links.");
            return KeywordSnapshot.Empty;
        }

        return new KeywordSnapshot(sets, ComputeStamp(sets));
    }

    /// <summary>
    /// Every page a keyword row points at, by key. A key with no row here is a page that has been deleted, which
    /// resolves to nothing rather than to a broken link.
    /// </summary>
    private static IReadOnlyDictionary<Guid, IContent> FetchPages(
        IContentService contentService,
        IReadOnlyList<KeywordMapping> allMappings)
    {
        Guid[] keys = allMappings
            .Where(mapping => !mapping.IsExternal && mapping.TargetKey != Guid.Empty)
            .Select(mapping => mapping.TargetKey)
            .Distinct()
            .ToArray();

        if (keys.Length == 0)
        {
            return new Dictionary<Guid, IContent>();
        }

        return contentService.GetByIds(keys).ToDictionary(content => content.Key);
    }

    /// <summary>
    /// Builds one culture's keyword set.
    /// </summary>
    private CultureKeywordSet BuildSet(
        string culture,
        AutoLinkOptions options,
        IPublishedUrlProvider urlProvider,
        IReadOnlyDictionary<Guid, IContent> pages,
        IReadOnlyList<KeywordMapping> allMappings,
        IReadOnlyList<KeywordSuppression> allSuppressions)
    {
        var targets = new Dictionary<string, KeywordTarget>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, KeywordMapping> mappings = KeywordMapping.InForce(allMappings, culture);
        IReadOnlyDictionary<string, IReadOnlyList<KeywordSuppression>> suppressions =
            SuppressionsFor(allSuppressions, culture);

        foreach ((string keyword, KeywordMapping mapping) in mappings)
        {
            KeywordTarget? resolved = Resolve(keyword, mapping, options, urlProvider, pages, culture);

            if (resolved is not null)
            {
                targets[keyword] = resolved;
            }
        }

        return new CultureKeywordSet(culture, targets, suppressions, KeywordMatcher.For(targets.Keys));
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
    /// Turns one stored row into a destination, or nothing when it will not resolve in this culture.
    /// </summary>
    private KeywordTarget? Resolve(
        string keyword,
        KeywordMapping mapping,
        AutoLinkOptions options,
        IPublishedUrlProvider urlProvider,
        IReadOnlyDictionary<Guid, IContent> pages,
        string culture)
    {
        if (mapping.IsExternal)
        {
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
                "Auto-link keyword {Keyword} has an external URL that is not absolute http or https, so it will not link.",
                keyword);

            return null;
        }

        string? culturePart = culture.Length == 0 ? null : culture;
        string? url = urlProvider.GetUrl(mapping.TargetKey, UrlMode.Relative, culturePart);

        if (!IsRoutable(url))
        {
            _logger.LogWarning(
                "Auto-link keyword {Keyword} points at {TargetKey}, which is not a routable published page in culture {Culture}, so it will not link there.",
                keyword,
                mapping.TargetKey,
                culture.Length == 0 ? "(invariant)" : culture);

            return null;
        }

        pages.TryGetValue(mapping.TargetKey, out IContent? page);
        string name = page?.GetCultureName(culturePart) ?? page?.Name ?? string.Empty;

        return new KeywordTarget(
            keyword,
            mapping.TargetKey,
            url!,
            name,
            KeywordSource.Manual,
            VariesByCulture: page?.ContentType.Variations.VariesByCulture() ?? false);
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

    private static bool IsRoutable(string? url) => !string.IsNullOrWhiteSpace(url) && url != "#";

    /// <summary>
    /// Hashes every culture's resolved targets and suppressions together. Changes only when the linking behaviour
    /// would actually differ, so a typo fix in body copy on a target page does not move the stamp, while a keyword
    /// added in one language does.
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
