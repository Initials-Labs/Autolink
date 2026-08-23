using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OC.AutoLink;
using OC.AutoLink.Linking;
using OC.AutoLink.Models;
using OC.AutoLink.Persistence;
using OC.AutoLink.Registry;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace OC.AutoLink.Tests;

/// <summary>
/// Builds an <see cref="AutoLinker"/> over a hand-made keyword set.
/// </summary>
/// <remarks>
/// The snapshot is built with the real <see cref="KeywordMatcher"/> rather than a regex written for the tests, so
/// these exercise the matching rules the renderer actually uses. Substitutes return their defaults, which is what
/// we want: no ambient Umbraco context, no request cache, so the budget applies per call.
/// </remarks>
internal static class TestLinker
{
    public static readonly Guid PageKey = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherPageKey = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static AutoLinker Create(CultureKeywordSet set, AutoLinkOptions? options = null)
    {
        var registry = Substitute.For<IKeywordRegistry>();
        registry.Current.Returns(new KeywordSnapshot(
            new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase)
            {
                [KeywordSnapshot.InvariantCulture] = set,
            },
            "test"));

        var monitor = Substitute.For<IOptionsMonitor<AutoLinkOptions>>();
        monitor.CurrentValue.Returns(options ?? new AutoLinkOptions());

        return new AutoLinker(
            registry,
            Substitute.For<IUmbracoContextAccessor>(),
            Substitute.For<IVariationContextAccessor>(),
            Substitute.For<IRequestCache>(),
            monitor,
            NullLogger<AutoLinker>.Instance);
    }

    /// <summary>A set where each keyword points at a page, with no suppressions.</summary>
    public static CultureKeywordSet Set(params KeywordTarget[] targets) =>
        Set(targets, []);

    public static CultureKeywordSet Set(
        IReadOnlyList<KeywordTarget> targets,
        IReadOnlyList<KeywordSuppression> suppressions)
    {
        var lookup = targets.ToDictionary(t => t.Keyword, StringComparer.OrdinalIgnoreCase);

        return new CultureKeywordSet(
            KeywordSnapshot.InvariantCulture,
            lookup,
            suppressions
                .GroupBy(s => s.Keyword, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<KeywordSuppression>)g.ToList(), StringComparer.OrdinalIgnoreCase),
            KeywordMatcher.For(lookup.Keys));
    }

    public static KeywordTarget Page(string keyword, string url = "/target/", Guid? key = null) =>
        new(keyword, key ?? OtherPageKey, url, "Target", KeywordSource.Manual);

    public static KeywordTarget External(string keyword, string url, string? rel = "nofollow") =>
        new(keyword, Guid.Empty, url, "example.com", KeywordSource.External, rel);

    public static KeywordSuppression Suppression(string keyword, Guid pageKey, string culture = "") =>
        new(keyword, pageKey, DateTime.UtcNow, "test", culture);
}
