namespace Initials.AutoLink.Registry;

/// <summary>
/// Holds the compiled keyword set. Singleton, rebuilt lazily after invalidation.
/// </summary>
public interface IKeywordRegistry
{
    /// <summary>
    /// The current snapshot, rebuilding first if the registry has been invalidated since the last read.
    /// </summary>
    KeywordSnapshot Current { get; }

    /// <summary>
    /// Marks the registry stale. The next read rebuilds.
    /// </summary>
    void Invalidate();
}
