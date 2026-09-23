namespace Initials.AutoLink.Models;

/// <summary>
/// A single keyword and the page it resolves to. URLs are resolved once at registry build time — resolving
/// them per render is most of what makes the naive version slow.
/// </summary>
/// <param name="Keyword">The keyword in the casing it was saved with on the Auto-linking screen.</param>
/// <param name="TargetKey">Key of the target page, used to avoid linking a page to itself.</param>
/// <param name="Url">Pre-resolved relative URL of the target.</param>
/// <param name="TargetName">Target page name, used for the link title attribute.</param>
/// <param name="Source">How this target won the keyword.</param>
/// <param name="Rel">
/// Value for the anchor rel attribute, or null to omit it. Only external links carry one: the configured tokens,
/// with the keyword's own nofollow choice applied. Auto-generated outbound links at scale can read as a link
/// scheme, so the default is nofollow.
/// </param>
/// <param name="VariesByCulture">
/// Whether the target document varies by culture. The backoffice needs it to build a workspace edit URL, whose
/// variant segment is a culture for a variant document and the literal <c>invariant</c> otherwise — the wrong
/// segment renders a blank workspace, not an error. Always false for an external link.
/// </param>
public sealed record KeywordTarget(
    string Keyword,
    Guid TargetKey,
    string Url,
    string TargetName,
    KeywordSource Source,
    string? Rel = null,
    bool VariesByCulture = false)
{
    /// <summary>Points outside the site, so there is no page behind it.</summary>
    public bool IsExternal => Source == KeywordSource.External;
}
