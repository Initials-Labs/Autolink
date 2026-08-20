namespace OC.AutoLink;

/// <summary>
/// What to do with a keyword that more than one page claims and no manual mapping settles.
/// </summary>
public enum UnresolvedCollisionBehaviour
{
    /// <summary>
    /// Do not link it at all, and report it as a conflict. A confidently wrong link is worse than no link,
    /// and an unlinked keyword is what sends somebody to the mapping screen to make the call.
    /// </summary>
    Skip,

    /// <summary>
    /// Link the first candidate by URL. Deterministic across restarts, unlike the original tag-query order,
    /// but still a guess. Here for anyone who prefers the pre-mapping behaviour.
    /// </summary>
    FirstByUrl,
}
