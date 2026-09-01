namespace Initials.AutoLink.Models;

/// <summary>
/// What kind of destination a keyword resolves to. Surfaced in the backoffice so a link that leaves the site is
/// distinguishable from one that stays on it.
/// </summary>
public enum KeywordSource
{
    /// <summary>A page in this site, chosen on the Autolink screen.</summary>
    Manual,

    /// <summary>Somewhere outside the site. No page behind it.</summary>
    External,
}
