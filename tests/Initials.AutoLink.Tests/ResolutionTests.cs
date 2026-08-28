using Initials.AutoLink.Linking;
using Initials.AutoLink.Models;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Registry;

namespace Initials.AutoLink.Tests;

/// <summary>
/// The decision rules: which culture applies, which row wins, and what counts as a usable URL.
/// </summary>
public class ResolutionTests
{
    private static readonly Guid Page = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("https://umbraco.com", true)]
    [InlineData("http://example.org/path?a=b", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=", false)]
    [InlineData("/relative/path", false)]
    [InlineData("umbraco.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_absolute_http_urls_are_accepted(string? url, bool expected)
    {
        Assert.Equal(expected, ExternalUrl.TryNormalise(url, out _));
    }

    [Fact]
    public void An_accepted_url_is_returned_trimmed()
    {
        Assert.True(ExternalUrl.TryNormalise("  https://umbraco.com  ", out string? normalised));
        Assert.Equal("https://umbraco.com", normalised);
    }

    [Fact]
    public void A_url_describes_itself_by_host()
    {
        Assert.Equal("umbraco.com", ExternalUrl.Describe("https://umbraco.com/products"));
    }

    [Fact]
    public void A_culture_specific_decision_beats_one_for_all_cultures()
    {
        var all = new KeywordMapping("hello", Target, null, null, null, DateTime.UtcNow, "test", string.Empty);
        var specific = new KeywordMapping("hello", Page, null, null, null, DateTime.UtcNow, "test", "en-GB");

        Dictionary<string, KeywordMapping> inForce = KeywordMapping.InForce([all, specific], "en-GB");

        Assert.Equal(Page, inForce["hello"].TargetKey);
    }

    [Fact]
    public void An_all_cultures_decision_applies_where_there_is_no_specific_one()
    {
        var all = new KeywordMapping("hello", Target, null, null, null, DateTime.UtcNow, "test", string.Empty);
        var other = new KeywordMapping("hello", Page, null, null, null, DateTime.UtcNow, "test", "fr-FR");

        Dictionary<string, KeywordMapping> inForce = KeywordMapping.InForce([all, other], "en-GB");

        Assert.Equal(Target, inForce["hello"].TargetKey);
    }

    [Fact]
    public void A_request_with_no_culture_falls_back_to_the_invariant_set()
    {
        CultureKeywordSet invariant = TestLinker.Set(TestLinker.Page("hello"));
        var snapshot = new KeywordSnapshot(
            new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase)
            {
                [KeywordSnapshot.InvariantCulture] = invariant,
            },
            "test");

        Assert.Same(invariant, snapshot.For(null));
        Assert.Same(invariant, snapshot.For("en-GB"));
    }

    [Fact]
    public void A_known_culture_gets_its_own_set()
    {
        CultureKeywordSet invariant = TestLinker.Set(TestLinker.Page("hello"));
        CultureKeywordSet french = TestLinker.Set(TestLinker.Page("bonjour"));

        var snapshot = new KeywordSnapshot(
            new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase)
            {
                [KeywordSnapshot.InvariantCulture] = invariant,
                ["fr-FR"] = french,
            },
            "test");

        Assert.Same(french, snapshot.For("fr-FR"));
        Assert.Same(invariant, snapshot.For("de-DE"));
    }

    [Fact]
    public void The_narrowest_suppression_row_is_the_one_offered()
    {
        // Switched off on this page for this culture, and separately everywhere for all cultures. Lifting should
        // target the page row first, so the editor makes progress rather than appearing to do nothing.
        KeywordSuppression page = TestLinker.Suppression("hello", Page, "en-GB");
        KeywordSuppression global = TestLinker.Suppression("hello", Guid.Empty);

        CultureKeywordSet set = TestLinker.Set([TestLinker.Page("hello")], [global, page]);

        KeywordSuppression? found = set.FindSuppression("hello", Page);

        Assert.NotNull(found);
        Assert.Equal(Page, found.PageKey);
        Assert.Equal("en-GB", found.Culture);
    }

    [Fact]
    public void A_global_row_suppresses_a_page_that_has_no_row_of_its_own()
    {
        CultureKeywordSet set = TestLinker.Set(
            [TestLinker.Page("hello")], [TestLinker.Suppression("hello", Guid.Empty)]);

        Assert.True(set.IsSuppressed("hello", Page));
        Assert.Equal(Guid.Empty, set.FindSuppression("hello", Page)!.PageKey);
    }

    [Fact]
    public void A_page_row_does_not_suppress_a_different_page()
    {
        CultureKeywordSet set = TestLinker.Set(
            [TestLinker.Page("hello")], [TestLinker.Suppression("hello", Page)]);

        Assert.False(set.IsSuppressed("hello", Target));
        Assert.Null(set.FindSuppression("hello", Target));
    }

    [Fact]
    public void Reporting_is_capped_per_keyword_per_reason()
    {
        var state = new AutoLinkRequestState();

        Assert.Equal(0, state.ReportsFor("hello", AutoLinkSkipReason.SelfLink));

        state.RecordReport("hello", AutoLinkSkipReason.SelfLink);

        Assert.Equal(1, state.ReportsFor("hello", AutoLinkSkipReason.SelfLink));

        // A different reason for the same keyword is tallied separately, so "linked once" and "not linked here"
        // can both be reported.
        Assert.Equal(0, state.ReportsFor("hello", AutoLinkSkipReason.LimitReached));
    }

    [Fact]
    public void The_linking_allowance_is_separate_from_the_reporting_tally()
    {
        var state = new AutoLinkRequestState();

        state.RecordReport("hello", AutoLinkSkipReason.Suppressed);

        // A mention that was not linked must not spend the allowance.
        Assert.Equal(0, state.CountFor("hello"));
        Assert.Equal(0, state.TotalLinks);
    }
}
