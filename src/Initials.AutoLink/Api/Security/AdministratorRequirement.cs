using Microsoft.AspNetCore.Authorization;

namespace Initials.AutoLink.Api.Security;

/// <summary>
/// Requires that the current backoffice user is an administrator.
/// </summary>
/// <remarks>
/// Section access is the right gate for editing keyword decisions and the wrong one for destroying them: the group
/// that uses the dashboard every day is exactly the group that should not be able to drop both tables. A
/// confirmation token prevents an accident, not a permission, so "destructive" is a separate authorization concept
/// here rather than a string comparison inside one action.
/// </remarks>
internal sealed class AdministratorRequirement : IAuthorizationRequirement
{
}
