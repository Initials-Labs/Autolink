namespace Initials.AutoLink.Linking;

/// <summary>
/// Per-request tally of what has already been linked.
/// </summary>
/// <remarks>
/// "First occurrence per page" cannot be tracked inside a single property, because a page is made of many
/// rich text properties — every rich text block in a Block List is its own property conversion. This rides on
/// the request cache so the count carries across all of them.
/// </remarks>
internal sealed class AutoLinkRequestState
{
    public const string CacheKey = "Initials.AutoLink.RequestState";

    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _reports = new(StringComparer.OrdinalIgnoreCase);

    public int TotalLinks { get; private set; }

    public int CountFor(string keyword) => _counts.TryGetValue(keyword, out int count) ? count : 0;

    public void Record(string keyword)
    {
        _counts[keyword] = CountFor(keyword) + 1;
        TotalLinks++;
    }

    /// <summary>
    /// How many times an audit has already reported this keyword on this page for a given reason.
    /// </summary>
    /// <remarks>
    /// Tallied separately from <see cref="CountFor"/> because a mention that was not linked must not spend the
    /// linking allowance — but the audit still needs a cap, or a keyword mentioned five times on a page would
    /// produce five identical rows where linking it produces one. Counted per reason, so "linked once" and "not
    /// linked here because it is the target" can both be reported for the same keyword.
    /// </remarks>
    public int ReportsFor(string keyword, string reason) =>
        _reports.TryGetValue(Key(keyword, reason), out int count) ? count : 0;

    public void RecordReport(string keyword, string reason) =>
        _reports[Key(keyword, reason)] = ReportsFor(keyword, reason) + 1;

    private static string Key(string keyword, string reason) => $"{keyword}\u001F{reason}";
}
