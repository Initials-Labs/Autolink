namespace OC.AutoLink.Relations;

/// <summary>
/// Identity of the relation type this package writes.
/// </summary>
/// <remarks>
/// A relation records that one page's rendered output currently carries an auto-link to another: the parent is the
/// page whose copy mentions the keyword, the child is the page being linked <em>to</em>.
/// <para>
/// That direction is not arbitrary, and it is the opposite of the obvious reading. Umbraco stores a reference with
/// the <em>referencing</em> item as the parent and the referenced one as the child — a Content Picker on page A
/// pointing at page B is stored parent=A, child=B — and tracked references answer "what is using this item" by
/// looking up relations where it is the child. Get it the wrong way round and the delete warning fires on the
/// mentioning pages instead of on the target, which is exactly the page whose deletion breaks the links.
/// </para>
/// <para>
/// Marked as a dependency relation, which is the whole point of using relations rather than another table of our
/// own: Umbraco's own delete flow reads dependency relations and warns before removing something other content
/// needs. The warning we want already exists, and this is how a package joins it.
/// </para>
/// </remarks>
public static class AutoLinkRelation
{
    /// <summary>Alias of the relation type. Stable — it identifies existing rows.</summary>
    public const string Alias = "ocAutoLinkKeyword";

    /// <summary>Name shown in Settings, Relations.</summary>
    public const string Name = "Auto-link keyword";
}
