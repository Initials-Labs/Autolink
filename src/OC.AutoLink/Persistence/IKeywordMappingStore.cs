namespace OC.AutoLink.Persistence;

/// <summary>
/// Reads and writes the manual keyword mappings. Keywords are matched case-insensitively, matching the
/// registry's own dictionary.
/// </summary>
public interface IKeywordMappingStore
{
    /// <summary>Every stored mapping. Called once per registry rebuild, not per render.</summary>
    IReadOnlyList<KeywordMapping> GetAll();

    /// <summary>
    /// Creates or replaces the mapping for a keyword in a culture. An empty culture applies to all of them.
    /// </summary>
    void Save(string keyword, KeywordDestination destination, string? updatedBy, string culture);

    /// <summary>Removes the mapping, handing the keyword back to automatic resolution. False if there wasn't one.</summary>
    bool Delete(string keyword, string culture);
}
