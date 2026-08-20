namespace OC.AutoLink.Api.Models;

/// <summary>
/// A decision that a keyword should not be linked.
/// </summary>
public sealed class SaveKeywordSuppressionRequestModel
{
    public required string Keyword { get; init; }

    /// <summary>
    /// Page to suppress it on, or an empty guid for every page.
    /// </summary>
    public Guid PageKey { get; init; }

    /// <summary>Culture it applies to, or empty for every culture.</summary>
    public string Culture { get; init; } = string.Empty;
}
