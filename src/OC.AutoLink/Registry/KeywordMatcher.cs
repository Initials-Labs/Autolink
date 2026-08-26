using System.Text.RegularExpressions;

namespace OC.AutoLink.Registry;

/// <summary>
/// Builds the single compiled alternation the renderer matches with.
/// </summary>
/// <remarks>
/// Its own type so the two rules that matter can be tested directly rather than inferred from rendered HTML:
/// longest keyword first, and word boundaries applied per keyword.
/// <para>
/// The registry and the tests both come through here, so what ships and what is tested cannot disagree about
/// which keywords are matchable.
/// </para>
/// </remarks>
internal static class KeywordMatcher
{
    /// <summary>
    /// The matcher for one culture's keyword set, or null when there is nothing to match.
    /// </summary>
    /// <remarks>
    /// Every keyword with a destination is matched, including ones held back by a suppression: they resolve, so they
    /// are in here, and the suppression is applied after the match. That is what stops switching a keyword off from
    /// promoting a shorter overlapping one — <c>Regex.Matches</c> is non-overlapping and the alternation is longest
    /// first, so nothing can match inside a phrase that already claimed its span.
    /// <para>
    /// A keyword whose destination will not resolve is absent, and does not reserve anything. It is a mapping to
    /// repair rather than a decision to hold ground for.
    /// </para>
    /// </remarks>
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
