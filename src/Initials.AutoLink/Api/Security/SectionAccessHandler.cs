using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security.Authorization;

namespace Initials.AutoLink.Api.Security;

/// <inheritdoc />
internal sealed class SectionAccessHandler : AuthorizationHandler<SectionAccessRequirement>
{
    private readonly IAuthorizationHelper _authorizationHelper;

    public SectionAccessHandler(IAuthorizationHelper authorizationHelper) =>
        _authorizationHelper = authorizationHelper;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SectionAccessRequirement requirement)
    {
        // TryGetUmbracoUser, not GetUmbracoUser: the latter throws for a principal that is not a backoffice
        // user, which turns an anonymous request into a 500 instead of a 401.
        if (_authorizationHelper.TryGetUmbracoUser(context.User, out IUser? user)
            && user.AllowedSections.Contains(requirement.SectionAlias, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
