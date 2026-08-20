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
    /// Tag group the keyword registry reads from. Must match the group configured on the Tags datatype.
    /// </summary>
    public string TagGroup { get; set; } = "autolink";

    /// <summary>
    /// Tags property holding the keywords a page should be linked from.
    /// </summary>
    public string KeywordsPropertyAlias { get; set; } = "linkKeywords";

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
    /// What happens to a keyword two or more pages claim with no manual mapping to settle it.
    /// </summary>
    public UnresolvedCollisionBehaviour OnUnresolvedCollision { get; set; } = UnresolvedCollisionBehaviour.Skip;

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
    /// Document type aliases the schema installer adds the keyword properties to. Empty disables the installer.
    /// </summary>
    public string[] InstallOnDocumentTypes { get; set; } = ["article", "content", "home", "category", "author"];

    /// <summary>
    /// When true, ensures the Tags datatype and keyword properties exist at startup. PoC convenience so the
    /// demo is reproducible from a clean database; a real package would use a migration plan.
    /// </summary>
    public bool InstallSchema { get; set; } = true;

    /// <summary>
    /// Hardcoded keyword to URL pairs, merged over whatever the tags query finds. This is the Spike 0 escape
    /// hatch — it proves the render pipeline without any content setup at all.
    /// </summary>
    public Dictionary<string, string> DebugKeywords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
