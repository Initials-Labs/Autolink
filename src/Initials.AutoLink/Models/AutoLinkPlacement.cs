namespace Initials.AutoLink.Models;

/// <summary>
/// One link the auto-linker would place, or would have placed had it not been suppressed.
/// </summary>
/// <param name="Keyword">The registered keyword that matched.</param>
/// <param name="MatchedText">The text as the editor wrote it, which is what the anchor would contain.</param>
/// <param name="TargetKey">Key of the page it points at, null when nothing resolved.</param>
/// <param name="TargetName">Name of that page, null when nothing resolved.</param>
/// <param name="Url">Its resolved relative URL, null when nothing resolved.</param>
/// <param name="SkipReason">
/// Null when the mention is linked. Otherwise one of <see cref="AutoLinkSkipReason"/>, so the audit can account for
/// every mention rather than quietly listing only the ones that became links.
/// </param>
/// <param name="SuppressedPageKey">
/// Page of the suppression row in force, or an empty guid when that row applies to every page. Null when nothing
/// is suppressing this placement.
/// </param>
/// <param name="SuppressedCulture">
/// Culture of the suppression row in force, empty when it applies to every culture, null when not suppressed.
/// </param>
/// <remarks>
/// The suppression is identified by the row actually in force rather than by flags describing it. Flags were the
/// first design and they were wrong: computed per keyword, so a keyword with an all-languages row on one page
/// reported every other page as all-languages too, and lifting it tried to delete a row that never existed.
/// </remarks>
public sealed record AutoLinkPlacement(
    string Keyword,
    string MatchedText,
    Guid? TargetKey,
    string? TargetName,
    string? Url,
    Guid? SuppressedPageKey,
    string? SuppressedCulture,
    string? SkipReason)
{
    /// <summary>
    /// Whether an editorial decision is what stopped this one linking, as opposed to one of the structural
    /// reasons. Derived rather than stored: two fields meaning the same thing is two fields to keep in step.
    /// </summary>
    public bool Suppressed => SkipReason == AutoLinkSkipReason.Suppressed;
}
