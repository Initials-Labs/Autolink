namespace Initials.AutoLink.Api.Models;

/// <summary>
/// Everything the keywords screen needs in one call: the rows, and the counts for the header.
/// </summary>
public sealed class KeywordOverviewResponseModel
{
    /// <summary>Registry content stamp. Changes when the linking behaviour would actually differ.</summary>
    public required string Stamp { get; init; }

    /// <summary>
    /// One entry per culture, plus the invariant one under an empty culture. Keywords are decided per language, so
    /// the whole table is.
    /// </summary>
    public required IEnumerable<CultureOverviewResponseModel> Cultures { get; init; }
}

/// <summary>
/// One culture's keywords.
/// </summary>
public sealed class CultureOverviewResponseModel
{
    /// <summary>Culture code, or empty for the invariant set.</summary>
    public required string Culture { get; init; }

    public required int Total { get; init; }

    /// <summary>
    /// Keywords whose destination will not resolve here.
    /// </summary>
    /// <remarks>
    /// The count worth a badge. It replaces what used to be the conflict count: two pages can no longer claim the
    /// same phrase, so the thing that now needs somebody's attention is a keyword pointing at a page that has been
    /// deleted, unpublished, or never published in this language.
    /// </remarks>
    public required int Unresolved { get; init; }

    /// <summary>Keywords pointing somewhere outside the site.</summary>
    public required int External { get; init; }

    public required IEnumerable<KeywordRowResponseModel> Keywords { get; init; }
}

/// <summary>
/// One keyword and where it points.
/// </summary>
public sealed class KeywordRowResponseModel
{
    public required string Keyword { get; init; }

    /// <summary>manual, external, or unresolved.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// The page this keyword points at, straight off the stored row rather than off the resolved target, so the
    /// screen can put an unresolved keyword back in the link picker instead of only reporting that it is broken.
    /// Null for an external link.
    /// </summary>
    public Guid? TargetKey { get; init; }

    public string? TargetName { get; init; }

    /// <summary>The resolved destination, or null when it will not resolve in this culture.</summary>
    public string? Url { get; init; }

    /// <summary>The stored URL for an external link, resolved or not. Null when the destination is a page.</summary>
    public string? ExternalUrl { get; init; }

    /// <summary>Title for an external link, or null to fall back to the host.</summary>
    public string? Label { get; init; }

    /// <summary>Whether this external link overrides the configured rel default. Null follows it.</summary>
    public bool? Nofollow { get; init; }

    public DateTime? UpdateDate { get; init; }

    public string? UpdatedBy { get; init; }

    /// <summary>
    /// Culture the row in force was written for, or empty when it applies to every culture. Lets the screen say
    /// "set for all languages" rather than implying the keyword is specific to the culture being viewed.
    /// </summary>
    public string? MappingCulture { get; init; }
}
