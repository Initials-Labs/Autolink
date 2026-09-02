using System.Diagnostics.CodeAnalysis;

namespace Initials.AutoLink.Models;

/// <summary>
/// Validation for editor-supplied link destinations.
/// </summary>
internal static class ExternalUrl
{
    /// <summary>
    /// True when the value is an absolute http or https URL, returning it trimmed.
    /// </summary>
    public static bool TryNormalise(string? value, [NotNullWhen(true)] out string? normalised)
    {
        normalised = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalised = trimmed;
        return true;
    }

    /// <summary>
    /// The host, for labelling a link nobody gave a title to. Falls back to the whole value if it will not parse.
    /// </summary>
    public static string Describe(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
}
