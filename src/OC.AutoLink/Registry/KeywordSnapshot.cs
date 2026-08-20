namespace OC.AutoLink.Registry;

/// <summary>
/// An immutable view of the keyword set, one <see cref="CultureKeywordSet"/> per culture, plus the content stamp
/// identifying this particular build.
/// </summary>
public sealed class KeywordSnapshot
{
    /// <summary>Key used for the invariant set, and the fallback when a request has no culture.</summary>
    public const string InvariantCulture = "";

    public static readonly KeywordSnapshot Empty = new(
        new Dictionary<string, CultureKeywordSet>(StringComparer.OrdinalIgnoreCase),
        "empty");

    public KeywordSnapshot(IReadOnlyDictionary<string, CultureKeywordSet> cultures, string stamp)
    {
        Cultures = cultures;
        Stamp = stamp;
    }

    /// <summary>Culture code to its keyword set. The empty key holds the invariant set.</summary>
    public IReadOnlyDictionary<string, CultureKeywordSet> Cultures { get; }

    /// <summary>
    /// Content hash across every culture. Changes only when the linking behaviour or the choices on offer would
    /// actually differ, so a typo fix in body copy on a target page does not invalidate anything.
    /// </summary>
    public string Stamp { get; }

    public bool IsEmpty => Cultures.Count == 0 || Cultures.Values.All(set => set.IsEmpty);

    /// <summary>
    /// The set to use for a request, falling back to the invariant one.
    /// </summary>
    /// <remarks>
    /// The fallback matters both ways round. A site whose keyword property does not vary has everything in the
    /// invariant set, and a request with no culture on a site that does vary would otherwise get nothing.
    /// </remarks>
    public CultureKeywordSet For(string? culture)
    {
        if (!string.IsNullOrEmpty(culture) && Cultures.TryGetValue(culture, out CultureKeywordSet? set))
        {
            return set;
        }

        return Cultures.TryGetValue(InvariantCulture, out CultureKeywordSet? invariant)
            ? invariant
            : CultureKeywordSet.Empty;
    }
}
