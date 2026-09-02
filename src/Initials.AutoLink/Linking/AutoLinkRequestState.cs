namespace Initials.AutoLink.Linking;

/// <summary>
/// Per-request tally of what has already been linked.
/// </summary>
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
    public int ReportsFor(string keyword, string reason) =>
        _reports.TryGetValue(Key(keyword, reason), out int count) ? count : 0;

    public void RecordReport(string keyword, string reason) =>
        _reports[Key(keyword, reason)] = ReportsFor(keyword, reason) + 1;

    private static string Key(string keyword, string reason) => $"{keyword}\u001F{reason}";
}
