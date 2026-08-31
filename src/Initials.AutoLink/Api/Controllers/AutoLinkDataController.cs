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
/// Deliberately not surfaced as a button in the dashboard. It destroys every keyword on the site, and a destructive
/// action one click away from the screen editors use every day is a mistake waiting to happen. It is an explicit
/// call for whoever is removing the package.
/// <para>
/// The blast radius grew when keywords stopped living on document types. These tables used to hold decisions
/// layered over tags, so a teardown lost the decisions and left the keywords themselves in the content. They are
/// now the only place keywords exist, so this drops the lot.
/// </para>
/// <para>
/// It also asks for more than the section: everything else here is gated on access to the Auto-linking section, which
/// is the permission an editor settling keyword collisions holds. Dropping both tables is not that permission, so this
/// one endpoint additionally requires an administrator. Both policies apply, so the administrator doing the teardown
/// needs the section granted as well — the same tick that let them use the dashboard in the first place. The
/// confirmation token below stops a mistake; it was never authorization.
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
