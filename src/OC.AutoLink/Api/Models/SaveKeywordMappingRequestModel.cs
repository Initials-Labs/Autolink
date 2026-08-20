namespace OC.AutoLink.Api.Models;

/// <summary>
/// A decision about which page a keyword belongs to.
/// </summary>
public sealed class SaveKeywordMappingRequestModel
{
    public required string Keyword { get; init; }

    /// <summary>
    /// The page to link to. Does not have to be one of the candidates, or tagged at all — that is how a
    /// synonym gets pointed at a hub page without cluttering its tag list. Leave empty for an external link.
    /// </summary>
    public Guid TargetKey { get; init; }

    /// <summary>
    /// An absolute http or https URL outside the site, when the destination is not a page. Exactly one of this and
    /// <see cref="TargetKey"/> must be set.
    /// </summary>
    public string? ExternalUrl { get; init; }

    /// <summary>Label for an external link, used as the anchor title. Defaults to the host.</summary>
    public string? Label { get; init; }

    /// <summary>Overrides the configured rel default for this link. Null follows the configuration.</summary>
    public bool? Nofollow { get; init; }

    /// <summary>
    /// Culture the decision applies to, or empty for every culture. A keyword contested in one language is a
    /// separate decision from the same word in another.
    /// </summary>
    public string Culture { get; init; } = string.Empty;
}
