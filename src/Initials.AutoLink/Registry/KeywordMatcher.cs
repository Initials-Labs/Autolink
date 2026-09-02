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
    /// Word boundaries are applied per keyword rather than around the whole alternation, because \b only does the
    /// right thing next to a word character. Wrapping the group would stop "C#" ever matching.
    /// </summary>
    private static string ToBoundedPattern(string keyword)
    {
        string escaped = Regex.Escape(keyword);
        string left = char.IsLetterOrDigit(keyword[0]) ? @"\b" : string.Empty;
        string right = char.IsLetterOrDigit(keyword[^1]) ? @"\b" : string.Empty;

        return $"{left}{escaped}{right}";
    }
}
