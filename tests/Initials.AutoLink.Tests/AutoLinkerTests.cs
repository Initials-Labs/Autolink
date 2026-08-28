using Initials.AutoLink.Linking;
using Initials.AutoLink.Models;
using Initials.AutoLink.Registry;

namespace Initials.AutoLink.Tests;

/// <summary>
/// The rules the renderer must never quietly lose.
/// </summary>
public class AutoLinkerTests
{
    [Fact]
    public void Links_a_plain_mention()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco", "/umbraco-page/")));

        string result = linker.ProcessMarkup("<p>We use Umbraco here.</p>");

        Assert.Contains("<a href=\"/umbraco-page/\"", result);
        Assert.Contains("data-autolink=\"true\"", result);
        Assert.Contains(">Umbraco</a>", result);
    }

    [Fact]
    public void Returns_the_same_instance_when_nothing_matches()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));
        const string markup = "<p>Nothing of interest here.</p>";

        // Reference equality matters: the value converter skips re-wrapping when the string is unchanged.
        Assert.Same(markup, linker.ProcessMarkup(markup));
    }

    [Fact]
    public void Preserves_the_casing_the_editor_typed()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("umbraco")));

        Assert.Contains(">UMBRACO</a>", linker.ProcessMarkup("<p>UMBRACO shouts.</p>"));
    }

    [Theory]
    [InlineData("<h2>Umbraco</h2>")]
    [InlineData("<p><a href=\"/elsewhere/\">Umbraco</a></p>")]
    [InlineData("<p><code>Umbraco</code></p>")]
    public void Leaves_text_inside_skipped_elements_alone(string markup)
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        Assert.Same(markup, linker.ProcessMarkup(markup));
    }

    [Fact]
    public void Never_nests_an_anchor_inside_an_anchor()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        string result = linker.ProcessMarkup("<p><a href=\"/x/\">Read about Umbraco now</a></p>");

        Assert.Equal(1, CountOccurrences(result, "<a "));
    }

    [Fact]
    public void Does_not_rewrite_attribute_values()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        // The word appears only in an attribute, so the raw-string prefilter matches but nothing should change.
        const string markup = "<p><img src=\"/media/Umbraco.png\" alt=\"logo\" /></p>";

        Assert.Same(markup, linker.ProcessMarkup(markup));
    }

    [Fact]
    public void Links_the_first_mention_only()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        string result = linker.ProcessMarkup("<p>Umbraco, then Umbraco again, then Umbraco.</p>");

        Assert.Equal(1, CountOccurrences(result, "data-autolink"));
    }

    [Fact]
    public void Respects_a_raised_per_keyword_cap()
    {
        AutoLinker linker = TestLinker.Create(
            TestLinker.Set(TestLinker.Page("Umbraco")),
            new AutoLinkOptions { MaxLinksPerKeyword = 2 });

        string result = linker.ProcessMarkup("<p>Umbraco, then Umbraco again, then Umbraco.</p>");

        Assert.Equal(2, CountOccurrences(result, "data-autolink"));
    }

    [Fact]
    public void Stands_down_when_the_editor_already_linked_to_the_target()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco", "/target/")));

        string result = linker.ProcessMarkup(
            "<p><a href=\"/target/\">their own link</a> and then Umbraco.</p>");

        Assert.Equal(0, CountOccurrences(result, "data-autolink"));
    }

    [Theory]
    [InlineData("<p>meetups are good</p>", false)]
    [InlineData("<p>a meetup is good</p>", true)]
    public void Matches_whole_words_only(string markup, bool shouldLink)
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("meetup")));

        Assert.Equal(shouldLink, linker.ProcessMarkup(markup).Contains("data-autolink"));
    }

    [Fact]
    public void Prefers_the_longer_keyword_where_two_start_together()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(
            TestLinker.Page("content editor", "/editor/"),
            TestLinker.Page("content", "/content/")));

        string result = linker.ProcessMarkup("<p>Ask a content editor about it.</p>");

        Assert.Contains("href=\"/editor/\"", result);
        Assert.DoesNotContain("href=\"/content/\"", result);
    }

    [Fact]
    public void Marks_external_links_and_carries_their_rel()
    {
        AutoLinker linker = TestLinker.Create(
            TestLinker.Set(TestLinker.External("Umbraco", "https://umbraco.com")));

        string result = linker.ProcessMarkup("<p>Umbraco is over there.</p>");

        Assert.Contains("href=\"https://umbraco.com\"", result);
        Assert.Contains("data-autolink-external=\"true\"", result);
        Assert.Contains("rel=\"nofollow\"", result);
    }

    [Fact]
    public void Omits_rel_when_a_link_is_trusted()
    {
        AutoLinker linker = TestLinker.Create(
            TestLinker.Set(TestLinker.External("Umbraco", "https://umbraco.com", rel: null)));

        string result = linker.ProcessMarkup("<p>Umbraco is over there.</p>");

        Assert.Contains("data-autolink-external", result);
        Assert.DoesNotContain("rel=", result);
    }

    [Fact]
    public void Internal_links_carry_no_external_markers()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        string result = linker.ProcessMarkup("<p>Umbraco is here.</p>");

        Assert.DoesNotContain("data-autolink-external", result);
        Assert.DoesNotContain("rel=", result);
    }

    [Fact]
    public void Does_not_link_a_page_to_itself()
    {
        CultureKeywordSet set = TestLinker.Set(TestLinker.Page("Umbraco", key: TestLinker.PageKey));
        AutoLinker linker = TestLinker.Create(set);

        IReadOnlyList<AutoLinkPlacement> placements = linker.Preview(
            "<p>Umbraco is here.</p>", TestLinker.PageKey, new AutoLinkRequestState());

        AutoLinkPlacement placement = Assert.Single(placements);
        Assert.Equal(AutoLinkSkipReason.SelfLink, placement.SkipReason);
    }

    [Fact]
    public void Suppression_is_reported_with_the_row_that_caused_it()
    {
        CultureKeywordSet set = TestLinker.Set(
            [TestLinker.Page("Umbraco")],
            [TestLinker.Suppression("Umbraco", TestLinker.PageKey, culture: "en-GB")]);

        IReadOnlyList<AutoLinkPlacement> placements = TestLinker.Create(set).Preview(
            "<p>Umbraco is here.</p>", TestLinker.PageKey, new AutoLinkRequestState());

        AutoLinkPlacement placement = Assert.Single(placements);
        Assert.True(placement.Suppressed);
        Assert.Equal(TestLinker.PageKey, placement.SuppressedPageKey);
        Assert.Equal("en-GB", placement.SuppressedCulture);
    }

    [Fact]
    public void A_suppressed_keyword_reserves_its_span_so_a_shorter_one_cannot_take_it()
    {
        // "content editor" is switched off everywhere; "editor" resolves to a page. Switching a keyword off must
        // not promote a shorter keyword onto the same words, so the phrase stays plain.
        CultureKeywordSet set = TestLinker.Set(
            [TestLinker.Page("content editor", "/roles/"), TestLinker.Page("editor", "/about/")],
            [TestLinker.Suppression("content editor", Guid.Empty)]);

        string result = TestLinker.Create(set).ProcessMarkup("<p>Ask a content editor.</p>");

        Assert.DoesNotContain("data-autolink", result);
    }

    [Fact]
    public void An_audit_explains_a_mention_it_could_not_link()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));

        IReadOnlyList<AutoLinkPlacement> placements = linker.Preview(
            "<h2>Umbraco</h2>", TestLinker.PageKey, new AutoLinkRequestState());

        AutoLinkPlacement placement = Assert.Single(placements);
        Assert.Equal(AutoLinkSkipReason.SkippedElement, placement.SkipReason);
    }

    [Fact]
    public void Suppress_stops_linking_for_the_duration_of_a_scan()
    {
        AutoLinker linker = TestLinker.Create(TestLinker.Set(TestLinker.Page("Umbraco")));
        const string markup = "<p>Umbraco is here.</p>";

        using (linker.Suppress())
        {
            Assert.Same(markup, linker.ProcessMarkup(markup));
        }

        Assert.Contains("data-autolink", linker.ProcessMarkup(markup));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
