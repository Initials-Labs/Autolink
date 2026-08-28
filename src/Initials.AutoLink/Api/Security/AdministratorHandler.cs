using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security.Authorization;

namespace Initials.AutoLink.Api.Security;

/// <inheritdoc cref="AdministratorRequirement" />
internal sealed class AdministratorHandler : AuthorizationHandler<AdministratorRequirement>
{
    private readonly IAuthorizationHelper _authorizationHelper;

    public AdministratorHandler(IAuthorizationHelper authorizationHelper) =>
        _authorizationHelper = authorizationHelper;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministratorRequirement requirement)
    {
        // TryGetUmbracoUser for the same reason as the section handler: the throwing overload turns an anonymous
        // request into a 500 instead of a 401.
        if (_authorizationHelper.TryGetUmbracoUser(context.User, out IUser? user)
            && user.Groups.Any(group =>
                string.Equals(group.Alias, Constants.Security.AdminGroupAlias, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
