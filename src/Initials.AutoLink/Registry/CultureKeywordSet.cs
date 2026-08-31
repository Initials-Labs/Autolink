using System.Text.RegularExpressions;
using Initials.AutoLink.Models;
using Initials.AutoLink.Persistence;

namespace Initials.AutoLink.Registry;

/// <summary>
/// Everything the renderer needs for one culture: the lookup, the matcher, and the suppressions holding it back.
/// </summary>
/// <remarks>
/// One of these per configured language, plus one for the invariant case. Keywords are decided per language, and so
/// are the URLs they resolve to, so a single shared set cannot express either.
/// </remarks>
public sealed class CultureKeywordSet
{
    public static readonly CultureKeywordSet Empty = new(
        string.Empty,
        new Dictionary<string, KeywordTarget>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<KeywordSuppression>>(StringComparer.OrdinalIgnoreCase),
        null);

    public CultureKeywordSet(
        string culture,
        IReadOnlyDictionary<string, KeywordTarget> targets,
        IReadOnlyDictionary<string, IReadOnlyList<KeywordSuppression>> suppressions,
        Regex? matcher)
    {
        Culture = culture;
        Targets = targets;
        Suppressions = suppressions;
        Matcher = matcher;
    }

    /// <summary>The culture this set is for, or empty for the invariant one.</summary>
    public string Culture { get; }

    /// <summary>Keyword to resolved target, case-insensitive. A keyword whose destination will not resolve is absent.</summary>
    public IReadOnlyDictionary<string, KeywordTarget> Targets { get; }

    /// <summary>
    /// Keyword to the suppression rows applying to it in this culture.
    /// </summary>
    /// <remarks>
    /// One structure rather than three. This began as a set of globally suppressed keywords, a page-to-keywords
    /// lookup, and the rows themselves: the same data three ways, and three things to keep in step. The rows answer
    /// both questions on their own — whether something is held back, and which row to lift to release it.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<KeywordSuppression>> Suppressions { get; }

    /// <summary>
    /// Single compiled alternation over every keyword the renderer needs to recognise in this culture, longest
    /// first. Null when there are none.
    /// </summary>
    public Regex? Matcher { get; }

    public bool IsEmpty => Matcher is null || Targets.Count == 0;

    /// <summary>Whether a keyword is held back, across the culture or on this particular page.</summary>
    public bool IsSuppressed(string keyword, Guid? pageKey) =>
        Rows(keyword).Any(row => row.IsGlobal || (pageKey is not null && row.PageKey == pageKey.Value));

    /// <summary>
    /// The suppression row holding a keyword back on a page, narrowest first: this page in this culture, then this
    /// page for all cultures, then every page in this culture, then every page for all cultures.
    /// </summary>
    /// <remarks>
    /// Narrowest first so lifting works progressively. A keyword switched off both on one page and everywhere keeps
    /// showing as suppressed after the page row is lifted, which is the truth, rather than appearing to do nothing.
    /// </remarks>
    public KeywordSuppression? FindSuppression(string keyword, Guid? pageKey)
    {
        KeywordSuppression? best = null;
        int bestRank = int.MaxValue;

        foreach (KeywordSuppression row in Rows(keyword))
        {
            bool matchesPage = pageKey is not null && row.PageKey == pageKey.Value;
            if (!matchesPage && !row.IsGlobal)
            {
                continue;
            }

            int rank = (matchesPage ? 0 : 2) + (row.IsAllCultures ? 1 : 0);
            if (rank < bestRank)
            {
                best = row;
                bestRank = rank;
            }
        }

        return best;
    }

    private IReadOnlyList<KeywordSuppression> Rows(string keyword) =>
        Suppressions.TryGetValue(keyword, out IReadOnlyList<KeywordSuppression>? rows) ? rows : [];
}
