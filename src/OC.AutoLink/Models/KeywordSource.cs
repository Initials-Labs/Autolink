namespace OC.AutoLink.Models;

/// <summary>
/// Where a keyword's resolved target came from. Surfaced in the backoffice so an editor can tell a decision
/// somebody made from one the registry made on its own.
/// </summary>
public enum KeywordSource
{
    /// <summary>Exactly one page claimed the keyword, so no decision was needed.</summary>
    Tag,

    /// <summary>A manual mapping settled it. Wins over anything the tags say.</summary>
    Manual,

    /// <summary>A hand-made link to somewhere outside the site. No page behind it.</summary>
    External,

    /// <summary>Configured in <see cref="AutoLinkOptions.DebugKeywords"/>. Wins over everything.</summary>
    Debug,
}
