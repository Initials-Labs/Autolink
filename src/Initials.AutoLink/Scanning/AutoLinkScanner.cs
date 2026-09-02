using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Initials.AutoLink.Linking;
using Initials.AutoLink.Models;
using Initials.AutoLink.Registry;
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

namespace Initials.AutoLink.Scanning;

/// <summary>
/// Reports which pages the auto-linker would place links on.
/// </summary>
public interface IAutoLinkScanner
{
    Task<AutoLinkScanReport> ScanAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class AutoLinkScanner : IAutoLinkScanner
{
    /// <summary>Depth guard for nested blocks. Deep enough for any real layout, shallow enough to stop a cycle.</summary>
    private const int MaxBlockDepth = 10;

    /// <summary>
    /// Property editors worth converting: the editors whose converters we wrap, and the block editors that can
    /// contain them. Converting every property on every page instead means resolving media, pickers and the rest,
    /// which is most of the cost of a scan and none of the value.
    /// </summary>
    private static readonly HashSet<string> ScannableEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        Constants.PropertyEditors.Aliases.RichText,
        Constants.PropertyEditors.Aliases.MarkdownEditor,
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

        IReadOnlyList<string> siteCultures = (await languageService.GetAllAsync())
            .Select(language => language.IsoCode)
            .ToList();

        using UmbracoContextReference contextReference = umbracoContextFactory.EnsureUmbracoContext();
        IPublishedContentCache? contentCache = contextReference.UmbracoContext.Content;

        if (contentCache is null)
        {
            return new AutoLinkScanReport(snapshot.Stamp, 0, 0, [], []);
        }

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

            foreach (string culture in CulturesOf(page, siteCultures))
            {
                string? url = urlProvider.GetUrl(page, UrlMode.Relative, culture.Length == 0 ? null : culture);
                if (string.IsNullOrWhiteSpace(url) || url == "#")
                {
                    skippedPages.Add(new SkippedPage(
                        page.Key, page.Name, culture, AutoLinkScanSkipReason.Unroutable));

                    continue;
                }

                var placements = new List<AutoLinkPlacement>();

                var state = new AutoLinkRequestState();

                foreach (string markup in CollectMarkup(page, culture))
                {
                    placements.AddRange(_linker.Preview(markup, page.Key, state, culture));
                }

                if (placements.Count > 0)
                {
                    pages.Add(new ScannedPage(
                        page.Key, page.Name, url, culture, placements, page.ContentType.VariesByCulture()));
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
    private IEnumerable<string> CollectMarkup(IPublishedContent page, string culture)
    {
        var markup = new List<string>();

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
