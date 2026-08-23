namespace OC.AutoLink.Persistence;

/// <summary>
/// Reads and writes the keywords. Matched case-insensitively, the same way the registry's own dictionary is.
/// </summary>
public interface IKeywordMappingStore
{
    /// <summary>Every stored keyword. Called once per registry rebuild, not per render.</summary>
    IReadOnlyList<KeywordMapping> GetAll();

    /// <summary>
    /// Creates a keyword, or replaces where an existing one points, in a culture. An empty culture applies to all
    /// of them.
    /// </summary>
    void Save(string keyword, KeywordDestination destination, string? updatedBy, string culture);

    /// <summary>Removes the keyword entirely. False if there wasn't one.</summary>
    bool Delete(string keyword, string culture);
}
