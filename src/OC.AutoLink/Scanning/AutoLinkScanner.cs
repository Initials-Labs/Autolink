using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OC.AutoLink.Linking;
using OC.AutoLink.Models;
using OC.AutoLink.Registry;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace OC.AutoLink.Scanning;

/// <summary>
/// Reports which pages the auto-linker would place links on.
/// </summary>
public interface IAutoLinkScanner
{
    Task<AutoLinkScanReport> ScanAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Render-time linking records nothing, so the only honest way to answer "which pages have auto-links" is to run
/// the linker again and ask. This does exactly that, in dry-run mode, against the published cache — which makes
/// the report exact (same code, same rules, same conflicts) and complete (pages nobody has visited are included).
/// What it deliberately is not is a history of what was served; that would need writing observations down as
/// pages render.
/// </remarks>
internal sealed class AutoLinkScanner : IAutoLinkScanner
{
    /// <summary>Depth guard for nested blocks. Deep enough for any real layout, shallow enough to stop a cycle.</summary>
    private const int MaxBlockDepth = 10;

    /// <summary>
    /// Property editors worth converting: rich text itself, and the block editors that can contain it. Converting
    /// every property on every page instead means resolving media, pickers and the rest, which is most of the cost
    /// of a scan and none of the value.
    /// </summary>
    private static readonly HashSet<string> ScannableEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        Constants.PropertyEditors.Aliases.RichText,
        Constants.PropertyEditors.Aliases.BlockList,
        Constants.PropertyEditors.Aliases.BlockGrid,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKeywordRegistry _registry;
    private readonly IAutoLinker _linker;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly ILogger<AutoLinkScanner> _logger;

    public AutoLinkScanner(
        IServiceScopeFactory scopeFactory,
        IKeywordRegistry registry,
        IAutoLinker linker,
        IVariationContextAccessor variationContextAccessor,
        IOptionsMonitor<AutoLinkOptions> options,
        ILogger<AutoLinkScanner> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _linker = linker;
        _variationContextAccessor = variationContextAccessor;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AutoLinkScanReport> ScanAsync(CancellationToken cancellationToken = default)
    {
        AutoLinkOptions options = _options.CurrentValue;
        KeywordSnapshot snapshot = _registry.Current;
        var pages = new List<ScannedPage>();
        var skippedPages = new List<SkippedPage>();
        int scanned = 0;
        int skipped = 0;

        using IServiceScope scope = _scopeFactory.CreateScope();

        var umbracoContextFactory = scope.ServiceProvider.GetRequiredService<IUmbracoContextFactory>();
        var contentService = scope.ServiceProvider.GetRequiredService<IContentService>();
        var languageService = scope.ServiceProvider.GetRequiredService<ILanguageService>();
        var urlProvider = scope.ServiceProvider.GetRequiredService<IPublishedUrlProvider>();

        // Needed for invariant pages: they carry no per-culture versions, but they are still served in every
        // language, and the renderer will match them against the requested language's keywords.
        IReadOnlyList<string> siteCultures = (await languageService.GetAllAsync())
            .Select(language => language.IsoCode)
            .ToList();

        using UmbracoContextReference contextReference = umbracoContextFactory.EnsureUmbracoContext();
        IPublishedContentCache? contentCache = contextReference.UmbracoContext.Content;

        if (contentCache is null)
        {
            return new AutoLinkScanReport(snapshot.Stamp, 0, 0, [], []);
        }

        // No short-circuit on an empty snapshot. Preview already bails per blob, and reporting "0 pages scanned"
        // when the walk never happened reads as a broken scan rather than as an empty result.

        foreach (Guid key in EnumeratePublishedKeys(contentService))
        {
            cancellationToken.ThrowIfCancellationRequested();

            IPublishedContent? page = await contentCache.GetByIdAsync(key);
            if (page is null)
            {
                continue;
            }

            if (page.Value<bool>(options.ExcludePropertyAlias))
            {
                skipped++;

                foreach (string optedOut in CulturesOf(page, siteCultures))
                {
                    skippedPages.Add(new SkippedPage(
                        page.Key, page.Name, optedOut, AutoLinkScanSkipReason.OptedOut));
                }

                continue;
            }

            scanned++;

            // A variant page is one node with a version per language, each with its own keywords, its own URL and
            // its own copy. Every one of them is a separate row.
            foreach (string culture in CulturesOf(page, siteCultures))
            {
                string? url = urlProvider.GetUrl(page, UrlMode.Relative, culture.Length == 0 ? null : culture);
                if (string.IsNullOrWhiteSpace(url) || url == "#")
                {
                    // Unroutable in this culture: usually the variant is not published. Recorded rather than dropped,
                    // because "my page is missing from the report" is otherwise unanswerable.
                    skippedPages.Add(new SkippedPage(
                        page.Key, page.Name, culture, AutoLinkScanSkipReason.Unroutable));

                    continue;
                }

                var placements = new List<AutoLinkPlacement>();

                // Every rich text property on the page shares one budget, exactly as a real request would, so the
                // report honours MaxLinksPerKeyword and MaxLinksPerPage the same way the renderer does.
                var state = new AutoLinkRequestState();

                foreach (string markup in CollectMarkup(page, culture))
                {
                    placements.AddRange(_linker.Preview(markup, page.Key, state, culture));
                }

                if (placements.Count > 0)
                {
                    pages.Add(new ScannedPage(page.Key, page.Name, url, culture, placements));
                }
            }
        }

        _logger.LogInformation(
            "Auto-link scan complete: {Scanned} page(s) scanned, {Skipped} opted out, {Unroutable} unroutable, {Hits} with mentions.",
            scanned,
            skipped,
            skippedPages.Count(p => p.Reason == AutoLinkScanSkipReason.Unroutable),
            pages.Count);

        return new AutoLinkScanReport(
            snapshot.Stamp,
            scanned,
            skipped,
            pages
                .OrderBy(p => p.Culture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            skippedPages
                .OrderBy(p => p.Culture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// Published content keys, from the service layer because the published cache exposes no root enumeration.
    /// </summary>
    private static IEnumerable<Guid> EnumeratePublishedKeys(IContentService contentService)
    {
        foreach (IContent root in contentService.GetRootContent())
        {
            if (root.Published)
            {
                yield return root.Key;
            }

            const int pageSize = 500;
            int pageIndex = 0;
            long total;

            do
            {
                IEnumerable<IContent> batch = contentService.GetPagedDescendants(root.Id, pageIndex, pageSize, out total);

                foreach (IContent child in batch)
                {
                    if (child.Published)
                    {
                        yield return child.Key;
                    }
                }

                pageIndex++;
            }
            while (pageIndex * pageSize < total);
        }
    }

    /// <summary>
    /// Every piece of rich text on a page, including the ones nested inside Block List and Block Grid.
    /// </summary>
    /// <remarks>
    /// Linking is switched off while the values are read. Reading a converted property value runs the value
    /// converter, which is where linking happens — so without this the scan would both double-link and spend the
    /// page budget before the preview ever ran.
    /// </remarks>
    private IEnumerable<string> CollectMarkup(IPublishedContent page, string culture)
    {
        var markup = new List<string>();

        // The variation context decides which culture a variant property value resolves to, including for rich text
        // nested inside blocks, where there is no per-call culture argument to pass.
        VariationContext? previous = _variationContextAccessor.VariationContext;

        try
        {
            _variationContextAccessor.VariationContext = new VariationContext(culture.Length == 0 ? null : culture);

            using (_linker.Suppress())
            {
                CollectFromElement(page, markup, 0);
            }
        }
        finally
        {
            _variationContextAccessor.VariationContext = previous;
        }

        return markup;
    }

    /// <summary>
    /// The cultures to examine a page in.
    /// </summary>
    /// <remarks>
    /// A varying page is examined in the cultures it is published in. An invariant page has none of its own, but it
    /// is still served in every language, and the renderer picks keywords by the culture of the request — so it has
    /// to be examined once per site language, not once against the invariant keyword set. Scanning it invariantly
    /// was a real gap: a page whose doctype did not vary rendered en-GB links and appeared nowhere in the report.
    /// </remarks>
    private static IReadOnlyList<string> CulturesOf(IPublishedContent page, IReadOnlyList<string> siteCultures)
    {
        List<string> cultures = page.Cultures.Keys.Where(culture => culture.Length > 0).ToList();

        if (cultures.Count > 0)
        {
            return cultures;
        }

        return siteCultures.Count > 0 ? siteCultures : [string.Empty];
    }

    private void CollectFromElement(IPublishedElement element, List<string> markup, int depth)
    {
        if (depth > MaxBlockDepth)
        {
            return;
        }

        foreach (IPublishedProperty property in element.Properties)
        {
            if (!ScannableEditors.Contains(property.PropertyType.EditorAlias))
            {
                continue;
            }

            object? value;

            try
            {
                value = property.GetValue();
            }
            catch (Exception ex)
            {
                // One broken property must not abort the whole scan.
                _logger.LogWarning(ex, "Could not read property {Alias} while scanning.", property.Alias);
                continue;
            }

            CollectFromValue(value, markup, depth);
        }
    }

    private void CollectFromValue(object? value, List<string> markup, int depth)
    {
        switch (value)
        {
            case null:
                return;

            case IHtmlEncodedString html:
                string? text = html.ToHtmlString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    markup.Add(text);
                }

                return;

            case BlockListModel blockList:
                foreach (BlockListItem item in blockList)
                {
                    CollectFromElement(item.Content, markup, depth + 1);
                }

                return;

            case BlockGridModel blockGrid:
                foreach (BlockGridItem item in blockGrid)
                {
                    CollectFromGridItem(item, markup, depth + 1);
                }

                return;

            case IEnumerable<IPublishedElement> elements:
                foreach (IPublishedElement child in elements)
                {
                    CollectFromElement(child, markup, depth + 1);
                }

                return;
        }
    }

    private void CollectFromGridItem(BlockGridItem item, List<string> markup, int depth)
    {
        if (depth > MaxBlockDepth)
        {
            return;
        }

        CollectFromElement(item.Content, markup, depth);

        foreach (BlockGridArea area in item.Areas)
        {
            foreach (BlockGridItem child in area)
            {
                CollectFromGridItem(child, markup, depth + 1);
            }
        }
    }
}
