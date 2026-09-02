using Microsoft.AspNetCore.Authorization;

namespace Initials.AutoLink.Api.Security;

/// <summary>
/// Requires that the current backoffice user is an administrator.
/// </summary>
internal sealed class AdministratorRequirement : IAuthorizationRequirement
{
}
