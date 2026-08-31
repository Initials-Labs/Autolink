namespace Initials.AutoLink.Persistence;

/// <summary>
/// Normalisation for the values that make up a decision's identity.
/// </summary>
/// <remarks>
/// Lower-cased so the unique indexes behave the same on SQLite, whose text comparison is case-sensitive, as on SQL
/// Server, whose default collation is not. These are index keys rather than display values: the screens show
/// keywords from the tag store and culture codes from the registry.
/// </remarks>
internal static class DecisionKey
{
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();
}
