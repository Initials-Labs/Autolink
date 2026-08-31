namespace Initials.AutoLink.Models;

/// <summary>
/// Why a mention of a registered keyword was not turned into a link.
/// </summary>
/// <remarks>
/// Stable codes rather than sentences, so the screen can phrase them and they can be counted. Every one of these
/// used to be a silent skip, which made the audit impossible to trust: a page could mention a keyword three times
/// and appear once, with nothing saying why.
/// </remarks>
internal static class AutoLinkSkipReason
{
    /// <summary>The mentioning page is the page the keyword points at.</summary>
    public const string SelfLink = "self";

    /// <summary>The editor already linked to that target in this property.</summary>
    public const string HandLinked = "hand-linked";

    /// <summary>Inside an element that is never linked, such as a heading, an anchor or code.</summary>
    public const string SkippedElement = "skipped-element";

    /// <summary>The per-keyword or per-page allowance was already spent.</summary>
    public const string LimitReached = "limit";

    /// <summary>Held back by an editorial decision.</summary>
    public const string Suppressed = "suppressed";
}
