using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Initials.AutoLink.Api.Security;
using Initials.AutoLink.Uninstall;

namespace Initials.AutoLink.Api.Controllers;

/// <summary>
/// Teardown, for removing the package cleanly.
/// </summary>
/// <remarks>
/// Deliberately not surfaced as a button in the dashboard. It destroys every keyword on the site, and there is
/// nowhere else they are stored, so it is an explicit call for whoever is removing the package.
/// <para>
/// Both policies apply: the caller needs the Autolink section granted, as every endpoint does, and must also be an
/// administrator. The confirmation token stops a mistake; it is not authorization.
/// </para>
/// </remarks>
[Authorize(Policy = AutoLinkApiConfiguration.TeardownPolicyName)]
public sealed class AutoLinkDataController : AutoLinkControllerBase
{
    /// <summary>The exact value callers must send, so this cannot fire by accident.</summary>
    public const string ConfirmationToken = "remove-autolink-data";

    private readonly IAutoLinkUninstaller _uninstaller;

    public AutoLinkDataController(IAutoLinkUninstaller uninstaller) => _uninstaller = uninstaller;

    /// <summary>
    /// Drops both keyword tables and resets that plan's migration state so a reinstall recreates them. Leaves
    /// document types and their properties alone.
    /// </summary>
    [HttpDelete("data")]
    [ProducesResponseType(typeof(AutoLinkUninstallResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult RemoveData([FromQuery] string? confirm)
    {
        if (confirm != ConfirmationToken)
        {
            return BadRequest(
                $"This removes every auto-link keyword on the site, and every link switched off by hand. There is nowhere else they are stored. Send confirm={ConfirmationToken} if that is what you want.");
        }

        return Ok(_uninstaller.RemoveData());
    }
}
