using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace OC.AutoLink.Api.Controllers;

/// <summary>
/// Routing, grouping and authorization shared by the package endpoints.
/// </summary>
/// <remarks>
/// The policy is the interesting one: Umbraco has no ready-made policy for a section it does not know about, and
/// getting it wrong signs the user out rather than showing an error, so it belongs in exactly one place.
/// </remarks>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("autolink")]
[ApiExplorerSettings(GroupName = AutoLinkApiConfiguration.ApiTitle)]
[MapToApi(AutoLinkApiConfiguration.ApiName)]
[Authorize(Policy = AutoLinkApiConfiguration.PolicyName)]
public abstract class AutoLinkControllerBase : ManagementApiControllerBase;
