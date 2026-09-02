using Microsoft.AspNetCore.Authorization;

namespace Initials.AutoLink.Api.Security;

/// <summary>
/// Requires that the current backoffice user has been granted a given section.
/// </summary>
internal sealed class SectionAccessRequirement : IAuthorizationRequirement
{
    public SectionAccessRequirement(string sectionAlias) => SectionAlias = sectionAlias;

    public string SectionAlias { get; }
}
