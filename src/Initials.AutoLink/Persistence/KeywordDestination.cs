namespace Initials.AutoLink.Persistence;

/// <summary>
/// Where a keyword should point: a page in this site, or a URL outside it.
/// </summary>
/// <remarks>
/// One type rather than a growing parameter list on the store, and one concept in the UI. A destination behaves the
/// same either way — it is scoped to a culture, it can be changed, and it can be removed. Which is also why the
/// link picker on the keywords screen is the multi-URL one: picking a page and typing a URL are the same act.
/// </remarks>
public sealed record KeywordDestination
{
    private KeywordDestination()
    {
    }

    public Guid TargetKey { get; private init; }

    public string? ExternalUrl { get; private init; }

    public string? Label { get; private init; }

    public bool? Nofollow { get; private init; }

    public bool IsExternal => !string.IsNullOrEmpty(ExternalUrl);

    public static KeywordDestination Page(Guid targetKey) => new() { TargetKey = targetKey };

    public static KeywordDestination External(string url, string? label, bool? nofollow) =>
        new() { ExternalUrl = url, Label = label, Nofollow = nofollow };
}
