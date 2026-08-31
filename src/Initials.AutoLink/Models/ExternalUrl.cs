using System.Diagnostics.CodeAnalysis;

namespace Initials.AutoLink.Models;

/// <summary>
/// Validation for editor-supplied link destinations.
/// </summary>
/// <remarks>
/// This is a security boundary, not a formatting nicety. Every URL the linker emitted before external links came
/// from Umbraco resolving a node; this is the first time a string somebody typed lands in an href, which makes
/// <c>javascript:</c> and <c>data:</c> an XSS vector. Checked when the row is saved and again when the registry
/// builds, so a row written by any other route cannot render a hostile scheme.
/// </remarks>
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
