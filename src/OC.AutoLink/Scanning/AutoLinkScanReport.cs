using OC.AutoLink.Models;

namespace OC.AutoLink.Scanning;

/// <summary>
/// What a site scan found.
/// </summary>
/// <param name="Stamp">Registry stamp the scan ran against, so a stale report is recognisable.</param>
/// <param name="PagesScanned">Published pages examined.</param>
/// <param name="PagesSkipped">Pages excluded from scanning by the opt-out property.</param>
/// <param name="Pages">Only the pages with something to report.</param>
/// <param name="Skipped">
/// Pages the scan could not examine, with the reason. A page that appears in neither list was examined and simply
/// has no mention of a keyword in its rich text.
/// </param>
public sealed record AutoLinkScanReport(
    string Stamp,
    int PagesScanned,
    int PagesSkipped,
    IReadOnlyList<ScannedPage> Pages,
    IReadOnlyList<SkippedPage> Skipped);

/// <summary>
/// A page the scan could not look at, and why.
/// </summary>
/// <param name="PageKey">The page.</param>
/// <param name="Name">Its name.</param>
/// <param name="Culture">Culture the scan was looking at, empty on an invariant site.</param>
/// <param name="Reason">One of <see cref="AutoLinkScanSkipReason"/>.</param>
public sealed record SkippedPage(Guid PageKey, string Name, string Culture, string Reason);

/// <summary>
/// Why a whole page could not be examined. Page-level counterpart to the per-mention reasons.
/// </summary>
public static class AutoLinkScanSkipReason
{
    /// <summary>No routable URL in this culture, usually because the variant is not published.</summary>
    public const string Unroutable = "unroutable";

    /// <summary>The page opted out of being scanned.</summary>
    public const string OptedOut = "opted-out";
}

/// <summary>
/// A page and the links the auto-linker would place on it.
/// </summary>
/// <param name="PageKey">The page.</param>
/// <param name="Name">Its name.</param>
/// <param name="Url">Its relative URL in this culture.</param>
/// <param name="Culture">Culture this row is for, empty on an invariant site.</param>
/// <param name="Placements">Links it would carry, suppressed ones included and flagged.</param>
public sealed record ScannedPage(
    Guid PageKey,
    string Name,
    string Url,
    string Culture,
    IReadOnlyList<AutoLinkPlacement> Placements);
