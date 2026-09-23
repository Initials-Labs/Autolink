using System.Text.RegularExpressions;

namespace Initials.AutoLink.Registry;

/// <summary>
/// Builds the single compiled alternation the renderer matches with.
/// </summary>
internal static class KeywordMatcher
{
    /// <summary>
    /// The matcher for one culture's keyword set, or null when there is nothing to match.
    /// </summary>
    public static Regex? For(IEnumerable<string> keywords)
    {
        var matchable = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);

        return matchable.Count == 0 ? null : Build(matchable);
    }

    /// <summary>
    /// One regex for the whole keyword set, sorted longest first so that where two keywords start at the same
    /// position the more specific one wins.
    /// </summary>
    public static Regex Build(IEnumerable<string> keywords)
    {
        string pattern = string.Join(
            '|',
            keywords
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.Ordinal)
                .Select(ToBoundedPattern));

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// A keyword must not have a word character on either side. Lookarounds rather than \b, because \b only means
    /// "edge of a word" next to a word character: it would let ".NET" match inside "ASP.NET" and stop "C#" ever
    /// matching at all.
    /// </summary>
    private static string ToBoundedPattern(string keyword) => $@"(?<!\w){Regex.Escape(keyword)}(?!\w)";
}
