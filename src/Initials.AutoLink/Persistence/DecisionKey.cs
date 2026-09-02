namespace Initials.AutoLink.Persistence;

/// <summary>
/// Normalisation for the values that make up a decision's identity.
/// </summary>
internal static class DecisionKey
{
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();
}
