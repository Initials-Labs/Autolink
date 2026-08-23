namespace OC.AutoLink;

/// <summary>
/// Configuration for the keyword auto-linker, bound from the <c>OC:AutoLink</c> configuration section.
/// </summary>
public sealed class AutoLinkOptions
{
    public const string SectionName = "OC:AutoLink";

    /// <summary>
    /// Master switch. When false the converter delegates straight through to Umbraco's built-in one.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Boolean property that opts a page out of being <em>scanned</em>. A page can still be a link target.
    /// </summary>
    public string ExcludePropertyAlias { get; set; } = "excludeFromAutoLinking";

    /// <summary>
    /// Default rel attribute for external auto-links. Empty omits it. Individual links can override this, for a
    /// domain trusted enough to pass authority to.
    /// </summary>
    public string ExternalLinkRel { get; set; } = "nofollow";

    /// <summary>
    /// How many times a single keyword may be linked on one page. SEO caution — the first mention is the useful one.
    /// </summary>
    public int MaxLinksPerKeyword { get; set; } = 1;

    /// <summary>
    /// Ceiling on auto-links across a whole page, counted across every rich text property rendered in the request.
    /// </summary>
    public int MaxLinksPerPage { get; set; } = 25;

    /// <summary>
    /// Text inside these elements is never linked. Anchors and code are the important ones; headings are an
    /// editorial choice.
    /// </summary>
    public string[] SkipInsideElements { get; set; } =
    [
        "a", "code", "pre", "kbd", "samp", "script", "style", "textarea", "button", "select", "option",
        "h1", "h2", "h3", "h4", "h5", "h6",
    ];

    /// <summary>
    /// Document type aliases the schema installer adds the scan opt-out property to. Empty disables the installer.
    /// </summary>
    /// <remarks>
    /// Empty by default. Guessing alias names on somebody else's site was fine for a spike and wrong for a package:
    /// installing a property onto document types nobody nominated is not a decision a package gets to make.
    /// </remarks>
    public string[] InstallOnDocumentTypes { get; set; } = [];

    /// <summary>
    /// When true, adds the <see cref="ExcludePropertyAlias"/> property to the document types named in
    /// <see cref="InstallOnDocumentTypes"/>.
    /// </summary>
    /// <remarks>
    /// Off by default: adding a property to somebody's document types is not a decision a package gets to make on
    /// its own. Turn it on to have it created for you, or add a True/false property with that alias yourself and
    /// leave this alone. Either way it is optional — without it, every page stays scannable.
    /// <para>
    /// Keywords are not installed as a property at all. They live in the package's own table and are edited on the
    /// Auto-linking screen, so there is nothing to add to a document type and nothing to fill in per page.
    /// </para>
    /// <para>
    /// The work runs once, from the package's own <c>OC.AutoLink.Schema</c> migration plan, rather than at every
    /// startup. Nothing is consumed until at least one document type is nominated, so turning this on after the
    /// first boot still works. Once it has run, though, a document type nominated later is not retro-fitted: add
    /// the property to it in the backoffice, the same way you would any other.
    /// </para>
    /// </remarks>
    public bool InstallSchema { get; set; }
}
