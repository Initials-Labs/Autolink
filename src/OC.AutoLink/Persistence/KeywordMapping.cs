namespace OC.AutoLink.Persistence;

/// <summary>
/// A keyword and the destination somebody chose for it. The only source of keywords there is.
/// </summary>
/// <param name="Keyword">The keyword, in the casing it was saved with.</param>
/// <param name="TargetKey">Key of the page it points at, or empty for an external link.</param>
/// <param name="ExternalUrl">Absolute URL outside the site, or null when the destination is a page.</param>
/// <param name="Label">Label for an external link, or null to fall back to the host.</param>
/// <param name="Nofollow">Overrides the configured rel default, or null to follow it.</param>
/// <param name="UpdateDate">When the decision was last changed.</param>
/// <param name="UpdatedBy">Who changed it, for the audit column in the backoffice.</param>
/// <param name="Culture">Culture it applies to, or empty for every culture.</param>
public sealed record KeywordMapping(
    string Keyword,
    Guid TargetKey,
    string? ExternalUrl,
    string? Label,
    bool? Nofollow,
    DateTime UpdateDate,
    string? UpdatedBy,
    string Culture)
{
    /// <summary>Points outside the site rather than at a page.</summary>
    public bool IsExternal => !string.IsNullOrEmpty(ExternalUrl);

    /// <summary>Applies to every culture, which is what a keyword added before the site varied means.</summary>
    public bool IsAllCultures => Culture.Length == 0;

    /// <summary>
    /// The mappings in force for a culture, keyed by keyword: rows for that culture over rows applying to all of
    /// them.
    /// </summary>
    /// <remarks>
    /// Shared deliberately. The registry resolves links with this and the backoffice lists them with it, and the
    /// whole point of that screen is that it cannot disagree with the renderer — which it would the moment the
    /// precedence rule existed in two places.
    /// </remarks>
    public static Dictionary<string, KeywordMapping> InForce(
        IEnumerable<KeywordMapping> mappings,
        string culture)
    {
        var inForce = new Dictionary<string, KeywordMapping>(StringComparer.OrdinalIgnoreCase);

        foreach (KeywordMapping mapping in mappings.Where(m => m.IsAllCultures))
        {
            inForce[mapping.Keyword] = mapping;
        }

        if (culture.Length == 0)
        {
            return inForce;
        }

        foreach (KeywordMapping mapping in mappings
                     .Where(m => string.Equals(m.Culture, culture, StringComparison.OrdinalIgnoreCase)))
        {
            inForce[mapping.Keyword] = mapping;
        }

        return inForce;
    }
}
