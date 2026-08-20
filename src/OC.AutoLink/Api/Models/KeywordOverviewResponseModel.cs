namespace OC.AutoLink.Api.Models;

/// <summary>
/// Everything the mapping screen needs in one call: the rows, and the counts for the header.
/// </summary>
public sealed class KeywordOverviewResponseModel
{
    /// <summary>Registry content stamp. Changes when the linking behaviour would actually differ.</summary>
    public required string Stamp { get; init; }

    /// <summary>
    /// One entry per culture, plus the invariant one under an empty culture. Keywords differ per language, so the
    /// whole table does.
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

    /// <summary>Keywords more than one page claims with nothing settling it.</summary>
    public required int Conflicts { get; init; }

    /// <summary>Keywords somebody has made a decision about.</summary>
    public required int Manual { get; init; }

    public required IEnumerable<KeywordRowResponseModel> Keywords { get; init; }
}

/// <summary>
/// One keyword, what it currently resolves to, and every page that wanted it.
/// </summary>
public sealed class KeywordRowResponseModel
{
    public required string Keyword { get; init; }

    /// <summary>tag, manual, debug or unresolved.</summary>
    public required string Source { get; init; }

    /// <summary>True when this keyword is contested and nothing has settled it.</summary>
    public required bool HasConflict { get; init; }

    public Guid? TargetKey { get; init; }

    public string? TargetName { get; init; }

    public string? Url { get; init; }

    public DateTime? UpdateDate { get; init; }

    public string? UpdatedBy { get; init; }

    /// <summary>
    /// Culture the decision in force was made for, or empty when it applies to every culture. Lets the screen say
    /// "set for all languages" rather than implying the decision is specific to the culture being viewed.
    /// </summary>
    public string? MappingCulture { get; init; }

    public required IEnumerable<KeywordCandidateResponseModel> Candidates { get; init; }
}

/// <summary>
/// A page claiming the keyword. These are the exact options the renderer considered.
/// </summary>
public sealed class KeywordCandidateResponseModel
{
    public required Guid TargetKey { get; init; }

    public required string TargetName { get; init; }

    public required string Url { get; init; }

    public required bool IsSelected { get; init; }
}
