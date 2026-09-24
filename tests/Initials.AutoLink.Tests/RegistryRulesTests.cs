using Initials.AutoLink.Models;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Registry;

namespace Initials.AutoLink.Tests;

/// <summary>
/// The pure rules the registry applies while building a snapshot: the rel attribute, the invalidation stamp, and
/// what the matcher treats as a word boundary.
/// </summary>
public class RegistryRulesTests
{
    [Theory]
    [InlineData(null, "nofollow", "nofollow")]
    [InlineData(null, "nofollow noopener", "nofollow noopener")]
    [InlineData(null, "noopener", "noopener")]
    [InlineData(null, "", null)]
    [InlineData(true, "", "nofollow")]
    [InlineData(true, "noopener", "nofollow noopener")]
    [InlineData(true, "nofollow noopener", "nofollow noopener")]
    [InlineData(false, "nofollow", null)]
    [InlineData(false, "nofollow noopener", "noopener")]
    [InlineData(false, "NoFollow  noreferrer", "noreferrer")]
    public void Rel_is_the_configured_tokens_with_the_row_deciding_nofollow(bool? nofollow, string configured, string? expected)
    {
        Assert.Equal(expected, KeywordRegistry.RelFor(nofollow, configured));
    }

    [Fact]
    public void The_stamp_is_stable_for_the_same_content()
    {
        Assert.Equal(Stamp(Set("Umbraco", "/umbraco/")), Stamp(Set("Umbraco", "/umbraco/")));
    }

    [Fact]
    public void The_stamp_ignores_the_order_targets_were_added_in()
    {
        CultureKeywordSet forwards = TestLinker.Set(TestLinker.Page("Alpha", "/a/"), TestLinker.Page("Beta", "/b/"));
        CultureKeywordSet backwards = TestLinker.Set(TestLinker.Page("Beta", "/b/"), TestLinker.Page("Alpha", "/a/"));

        Assert.Equal(Stamp(forwards), Stamp(backwards));
    }

    [Fact]
    public void The_stamp_moves_when_a_url_changes()
    {
        Assert.NotEqual(Stamp(Set("Umbraco", "/umbraco/")), Stamp(Set("Umbraco", "/moved/")));
    }

    [Fact]
    public void The_stamp_moves_when_a_keyword_is_added()
    {
        CultureKeywordSet one = TestLinker.Set(TestLinker.Page("Alpha", "/a/"));
        CultureKeywordSet two = TestLinker.Set(TestLinker.Page("Alpha", "/a/"), TestLinker.Page("Beta", "/b/"));

        Assert.NotEqual(Stamp(one), Stamp(two));
    }

    [Fact]
    public void The_stamp_moves_when_a_suppression_is_added()
    {
        KeywordTarget[] targets = [TestLinker.Page("Umbraco")];

        CultureKeywordSet open = TestLinker.Set(targets, []);
        CultureKeywordSet held = TestLinker.Set(targets, [TestLinker.Suppression("Umbraco", TestLinker.PageKey)]);

        Assert.NotEqual(Stamp(open), Stamp(held));
    }

    [Fact]
    public void The_stamp_moves_when_a_keyword_appears_in_another_culture()
    {
        CultureKeywordSet set = Set("Umbraco", "/umbraco/");

        string invariantOnly = KeywordRegistry.ComputeStamp(new Dictionary<string, CultureKeywordSet>
        {
            [KeywordSnapshot.InvariantCulture] = set,
        });

        string withCulture = KeywordRegistry.ComputeStamp(new Dictionary<string, CultureKeywordSet>
        {
            [KeywordSnapshot.InvariantCulture] = set,
            ["en-GB"] = set,
        });

        Assert.NotEqual(invariantOnly, withCulture);
    }

    [Theory]
    [InlineData("Umbraco", "I use Umbraco daily.", true)]
    [InlineData("Umbraco", "I use Umbracology daily.", false)]
    [InlineData("C#", "Written in C# today.", true)]
    [InlineData("C#", "Written in C#.", true)]
    [InlineData(".NET", "Runs on .NET now.", true)]
    [InlineData(".NET", "Runs on ASP.NET now.", false)]
    [InlineData(".NET", "(.NET)", true)]
    [InlineData("C#", "C#x is not C#.", true)]
    public void A_keyword_is_not_matched_inside_a_word_on_either_side(string keyword, string text, bool expected)
    {
        Assert.Equal(expected, KeywordMatcher.Build([keyword]).IsMatch(text));
    }

    private static CultureKeywordSet Set(string keyword, string url) =>
        TestLinker.Set(TestLinker.Page(keyword, url));

    private static string Stamp(CultureKeywordSet set) =>
        KeywordRegistry.ComputeStamp(new Dictionary<string, CultureKeywordSet>
        {
            [KeywordSnapshot.InvariantCulture] = set,
        });
}
