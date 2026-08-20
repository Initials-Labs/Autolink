namespace OC.AutoLink.Api;

/// <summary>
/// Names the package's slice of the management API, so its endpoints get their own Swagger document at
/// <c>/umbraco/swagger</c> rather than being buried in Umbraco's.
/// </summary>
public static class AutoLinkApiConfiguration
{
    public const string ApiName = "autolink";

    public const string ApiTitle = "Auto Link";

    /// <summary>Alias of the backoffice section the mapping screen lives in.</summary>
    public const string SectionAlias = "OC.AutoLink.Section";

    /// <summary>
    /// Authorization policy for the package endpoints: access to our own section, nothing more. Umbraco has no
    /// ready-made policy for a section it does not know about, so we register one with the same requirement its
    /// own section policies use.
    /// </summary>
    public const string PolicyName = "OC.AutoLink.SectionAccess";
}
