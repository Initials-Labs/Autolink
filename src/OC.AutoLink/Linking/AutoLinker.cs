using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OC.AutoLink.Models;
using OC.AutoLink.Persistence;
using OC.AutoLink.Registry;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace OC.AutoLink.Linking;

/// <inheritdoc />
public sealed class AutoLinker : IAutoLinker
{
    private readonly IKeywordRegistry _registry;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly IRequestCache _requestCache;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly ILogger<AutoLinker> _logger;
    private readonly HtmlParser _parser = new();

    // AsyncLocal rather than a field: a scan reads many pages inside one async flow, and must not switch
    // linking off for concurrent front-end requests being served at the same time.
    private static readonly AsyncLocal<bool> ScanInProgress = new();

    public AutoLinker(
        IKeywordRegistry registry,
        IUmbracoContextAccessor umbracoContextAccessor,
        IVariationContextAccessor variationContextAccessor,
        IRequestCache requestCache,
        IOptionsMonitor<AutoLinkOptions> options,
        ILogger<AutoLinker> logger)
    {
        _registry = registry;
        _umbracoContextAccessor = umbracoContextAccessor;
        _variationContextAccessor = variationContextAccessor;
        _requestCache = requestCache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public IDisposable Suppress() => new ScanScope();

    /// <inheritdoc />
    public IReadOnlyList<AutoLinkPlacement> Preview(
        string markup,
        Guid? currentPageKey,
        AutoLinkRequestState state,
        string? culture = null)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return [];
        }

        CultureKeywordSet set = _registry.Current.For(culture);
        if (set.IsEmpty || !set.Matcher!.IsMatch(markup))
        {
            return [];
        }

        var placements = new List<AutoLinkPlacement>();

        try
        {
            // The rewritten markup is thrown away. Only the placements it reports matter.
            Rewrite(markup, set, _options.CurrentValue, currentPageKey, state, placements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-link preview failed for a rich text property. Reporting what was found.");
        }

        return placements;
    }

    /// <inheritdoc />
    public string ProcessMarkup(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup) || ScanInProgress.Value)
        {
            return markup;
        }

        // Which keywords apply depends on the culture being rendered: "hello" and "bonjour" are different sets
        // pointing at different URLs for the same pages.
        CultureKeywordSet set = _registry.Current.For(CurrentCulture());
        if (set.IsEmpty)
        {
            return markup;
        }

        // Cheap gate before the expensive one. Most markup contains no keyword at all, and a regex scan over
        // the raw string is far cheaper than a parse. A false positive inside an attribute only costs a parse.
        if (!set.Matcher!.IsMatch(markup))
        {
            return markup;
        }

        AutoLinkOptions options = _options.CurrentValue;
        IPublishedContent? currentPage = GetCurrentPage();

        if (currentPage is not null && currentPage.Value<bool>(options.ExcludePropertyAlias))
        {
            return markup;
        }

        AutoLinkRequestState state = GetRequestState();
        if (state.TotalLinks >= options.MaxLinksPerPage)
        {
            return markup;
        }

        try
        {
            return Rewrite(markup, set, options, currentPage?.Key, state, placements: null);
        }
        catch (Exception ex)
        {
            // Never let a markup edge case take down a page render.
            _logger.LogError(ex, "Auto-linking failed for a rich text property. Returning the original markup.");
            return markup;
        }
    }

    private string Rewrite(
        string markup,
        CultureKeywordSet set,
        AutoLinkOptions options,
        Guid? currentPageKey,
        AutoLinkRequestState state,
        List<AutoLinkPlacement>? placements)
    {
        IHtmlDocument document = _parser.ParseDocument(string.Empty);
        IElement container = document.CreateElement("div");

        // Parsing into a detached div gives a stable round trip: InnerHtml in, InnerHtml out.
        container.InnerHtml = markup;

        // Anything the editor linked by hand wins. If they already pointed at the target, do not add a second link.
        HashSet<string> existingHrefs = container
            .QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href") ?? string.Empty)
            .Where(href => href.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skipInside = new HashSet<string>(options.SkipInsideElements, StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (IText textNode in container.Descendants<IText>().ToList())
        {
            if (state.TotalLinks >= options.MaxLinksPerPage)
            {
                break;
            }

            bool insideSkipped = textNode.Ancestors<IElement>().Any(e => skipInside.Contains(e.LocalName));

            if (insideSkipped && placements is null)
            {
                // Rendering: nothing to do inside a heading or an anchor. An audit still looks, so it can say why
                // a mention sitting in one was left alone.
                continue;
            }

            if (RewriteTextNode(
                    textNode,
                    document,
                    set,
                    options,
                    currentPageKey,
                    state,
                    existingHrefs,
                    placements,
                    insideSkipped ? AutoLinkSkipReason.SkippedElement : null))
            {
                changed = true;
            }
        }

        return changed ? container.InnerHtml : markup;
    }

    private static bool RewriteTextNode(
        IText textNode,
        IDocument document,
        CultureKeywordSet set,
        AutoLinkOptions options,
        Guid? currentPageKey,
        AutoLinkRequestState state,
        HashSet<string> existingHrefs,
        List<AutoLinkPlacement>? placements,
        string? forcedSkipReason)
    {
        string text = textNode.Data;
        MatchCollection matches = set.Matcher!.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        INode? parent = textNode.Parent;
        if (parent is null)
        {
            return false;
        }

        var replacement = new List<INode>();
        int cursor = 0;

        foreach (Match match in matches)
        {
            bool pageFull = state.TotalLinks >= options.MaxLinksPerPage;

            if (pageFull && placements is null)
            {
                break;
            }

            if (!set.Targets.TryGetValue(match.Value, out KeywordTarget? target))
            {
                // Unreachable in practice: the matcher is built from the resolved keywords and nothing else. It
                // stays because the alternative to a mismatch between the two is a null reference mid-render.
                continue;
            }

            if (forcedSkipReason is not null)
            {
                Report(placements, state, options, target, match.Value, forcedSkipReason);
                continue;
            }

            if (pageFull)
            {
                Report(placements, state, options, target, match.Value, AutoLinkSkipReason.LimitReached);
                continue;
            }

            // Never link a page to itself.
            if (currentPageKey is not null && target.TargetKey == currentPageKey.Value)
            {
                Report(placements, state, options, target, match.Value, AutoLinkSkipReason.SelfLink);
                continue;
            }

            // The editor already linked to this target somewhere in this property.
            if (existingHrefs.Contains(target.Url))
            {
                Report(placements, state, options, target, match.Value, AutoLinkSkipReason.HandLinked);
                continue;
            }

            // Suppressed by an editorial decision, globally or on this page alone. Reported so the audit can
            // offer to lift it, but it does not burn the keyword's allowance.
            if (set.IsSuppressed(target.Keyword, currentPageKey))
            {
                if (placements is not null
                    && state.ReportsFor(target.Keyword, AutoLinkSkipReason.Suppressed) < options.MaxLinksPerKeyword)
                {
                    // Name the row actually in force, so the audit lifts that one rather than a guess at its scope.
                    KeywordSuppression? row = set.FindSuppression(target.Keyword, currentPageKey);

                    placements.Add(new AutoLinkPlacement(
                        target.Keyword,
                        match.Value,
                        target.TargetKey,
                        target.TargetName,
                        target.Url,
                        row?.PageKey,
                        row?.Culture,
                        AutoLinkSkipReason.Suppressed));

                    state.RecordReport(target.Keyword, AutoLinkSkipReason.Suppressed);
                }

                continue;
            }

            // Budget check last, so a rejected candidate does not burn the keyword's single allowance.
            if (state.CountFor(target.Keyword) >= options.MaxLinksPerKeyword)
            {
                Report(placements, state, options, target, match.Value, AutoLinkSkipReason.LimitReached);
                continue;
            }

            placements?.Add(ToPlacement(target, match.Value, suppressedPageKey: null, suppressedCulture: null));

            if (match.Index > cursor)
            {
                replacement.Add(document.CreateTextNode(text[cursor..match.Index]));
            }

            IElement anchor = document.CreateElement("a");
            anchor.SetAttribute("href", target.Url);
            anchor.SetAttribute("data-autolink", "true");
            anchor.SetAttribute("title", target.TargetName);

            if (target.IsExternal)
            {
                // A second attribute rather than a different value for the first, so anything already keying on
                // data-autolink keeps working while external links can still be styled or audited on their own.
                anchor.SetAttribute("data-autolink-external", "true");

                if (target.Rel is { Length: > 0 } rel)
                {
                    anchor.SetAttribute("rel", rel);
                }
            }

            // match.Value, not target.Keyword — the editor's casing is preserved.
            anchor.TextContent = match.Value;

            replacement.Add(anchor);
            state.Record(target.Keyword);
            cursor = match.Index + match.Length;
        }

        if (replacement.Count == 0)
        {
            return false;
        }

        if (cursor < text.Length)
        {
            replacement.Add(document.CreateTextNode(text[cursor..]));
        }

        foreach (INode node in replacement)
        {
            parent.InsertBefore(node, textNode);
        }

        parent.RemoveChild(textNode);
        return true;
    }

    /// <summary>
    /// Records a mention that was not linked, capped per keyword per reason so a page mentioning a keyword ten times
    /// yields one explanatory row rather than ten.
    /// </summary>
    private static void Report(
        List<AutoLinkPlacement>? placements,
        AutoLinkRequestState state,
        AutoLinkOptions options,
        KeywordTarget target,
        string matchedText,
        string reason)
    {
        if (placements is null)
        {
            return;
        }

        if (state.ReportsFor(target.Keyword, reason) >= options.MaxLinksPerKeyword)
        {
            return;
        }

        placements.Add(new AutoLinkPlacement(
            target.Keyword,
            matchedText,
            target.TargetKey,
            target.TargetName,
            target.Url,
            SuppressedPageKey: null,
            SuppressedCulture: null,
            reason));

        state.RecordReport(target.Keyword, reason);
    }

    private static AutoLinkPlacement ToPlacement(
        KeywordTarget target,
        string matchedText,
        Guid? suppressedPageKey,
        string? suppressedCulture) =>
        new(
            target.Keyword,
            matchedText,
            target.TargetKey,
            target.TargetName,
            target.Url,
            suppressedPageKey,
            suppressedCulture,
            SkipReason: null);

    /// <summary>
    /// Switches linking off for the current async flow. Nested scopes are fine; the flag is only cleared on the
    /// way out of the outermost one.
    /// </summary>
    private sealed class ScanScope : IDisposable
    {
        private readonly bool _previous;

        public ScanScope()
        {
            _previous = ScanInProgress.Value;
            ScanInProgress.Value = true;
        }

        public void Dispose() => ScanInProgress.Value = _previous;
    }

    /// <summary>
    /// The culture being rendered. The variation context is the canonical source: Umbraco sets it from the request
    /// for variant content, and leaves it empty on an invariant site.
    /// </summary>
    private string? CurrentCulture()
    {
        string? culture = _variationContextAccessor.VariationContext?.Culture;
        if (!string.IsNullOrEmpty(culture))
        {
            return culture;
        }

        return _umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? context)
            ? context.PublishedRequest?.Culture
            : null;
    }

    /// <summary>
    /// The element passed to a value converter is the block, not the page, when the rich text lives inside a
    /// Block List or Block Grid. The current page has to come from the request instead.
    /// </summary>
    private IPublishedContent? GetCurrentPage() =>
        _umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? context)
            ? context.PublishedRequest?.PublishedContent
            : null;

    private AutoLinkRequestState GetRequestState()
    {
        if (!_requestCache.IsAvailable)
        {
            // Outside a request (a background render, or a unit test). Budget applies per call.
            return new AutoLinkRequestState();
        }

        return _requestCache.GetCacheItem(AutoLinkRequestState.CacheKey, () => new AutoLinkRequestState())
               ?? new AutoLinkRequestState();
    }
}
