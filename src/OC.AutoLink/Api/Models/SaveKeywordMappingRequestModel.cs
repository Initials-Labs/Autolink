namespace OC.AutoLink.Api.Models;

/// <summary>
/// A keyword and where it should point.
/// </summary>
public sealed class SaveKeywordMappingRequestModel
{
    public required string Keyword { get; init; }

    /// <summary>
    /// The page to link to. Any published page will do, and several keywords may point at the same one — that is
    /// how synonyms and plurals are expressed. Leave empty for an external link.
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
    /// Culture the keyword applies to, or empty for every culture. The same word in two languages can point at two
    /// different pages, so each is its own row.
    /// </summary>
    public string Culture { get; init; } = string.Empty;
}
