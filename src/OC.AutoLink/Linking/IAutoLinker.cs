using OC.AutoLink.Models;

namespace OC.AutoLink.Linking;

/// <summary>
/// Turns keyword mentions in a fragment of rich text markup into links.
/// </summary>
internal interface IAutoLinker
{
    /// <summary>
    /// Returns <paramref name="markup"/> with keyword mentions linked. Returns the exact same string instance
    /// when nothing was changed, so callers can skip re-wrapping.
    /// </summary>
    string ProcessMarkup(string markup);

    /// <summary>
    /// Reports what would be linked in <paramref name="markup"/> without changing it, including matches a
    /// suppression is holding back. Same code path as <see cref="ProcessMarkup"/>, so an audit built on this
    /// cannot disagree with what the renderer does.
    /// </summary>
    /// <param name="markup">The rich text to examine.</param>
    /// <param name="currentPageKey">Page the markup belongs to, for self-link and suppression checks.</param>
    /// <param name="state">Caller-supplied budget, so a scan can give each page a fresh one.</param>
    /// <param name="culture">Culture whose keyword set to match against, empty or null for the invariant one.</param>
    IReadOnlyList<AutoLinkPlacement> Preview(
        string markup,
        Guid? currentPageKey,
        AutoLinkRequestState state,
        string? culture = null);

    /// <summary>
    /// Stops linking for the current async flow until disposed. A scan reads converted property values to get
    /// at the markup, and those conversions would otherwise link as a side effect of being read.
    /// </summary>
    IDisposable Suppress();
}
