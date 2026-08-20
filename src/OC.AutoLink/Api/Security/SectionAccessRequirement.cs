using Microsoft.AspNetCore.Authorization;

namespace OC.AutoLink.Api.Security;

/// <summary>
/// Requires that the current backoffice user has been granted a given section.
/// </summary>
/// <remarks>
/// Umbraco has an equivalent requirement behind its own <c>SectionAccess*</c> policies, but the type is
/// internal, so a package that adds its own section has to bring its own. The check itself is the same one:
/// the section alias against <see cref="Umbraco.Cms.Core.Models.Membership.IUser.AllowedSections"/>.
/// </remarks>
public sealed class SectionAccessRequirement : IAuthorizationRequirement
{
    public SectionAccessRequirement(string sectionAlias) => SectionAlias = sectionAlias;

    public string SectionAlias { get; }
}
