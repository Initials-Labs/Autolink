using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OC.AutoLink.Uninstall;

namespace OC.AutoLink.Api.Controllers;

/// <summary>
/// Teardown, for removing the package cleanly.
/// </summary>
/// <remarks>
/// Deliberately not surfaced as a button in the dashboard. It destroys every mapping and suppression on the site, and
/// a destructive action one click away from the screen editors use every day is a mistake waiting to happen. It is an
/// explicit call for whoever is removing the package.
/// </remarks>
public sealed class AutoLinkDataController : AutoLinkControllerBase
{
    /// <summary>The exact value callers must send, so this cannot fire by accident.</summary>
    public const string ConfirmationToken = "remove-autolink-data";

    private readonly IAutoLinkUninstaller _uninstaller;

    public AutoLinkDataController(IAutoLinkUninstaller uninstaller) => _uninstaller = uninstaller;

    /// <summary>
    /// Drops both decision tables and resets the migration state so a reinstall recreates them. Leaves document
    /// types, the keyword property and its values alone.
    /// </summary>
    [HttpDelete("data")]
    [ProducesResponseType(typeof(AutoLinkUninstallResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult RemoveData([FromQuery] string? confirm)
    {
        if (confirm != ConfirmationToken)
        {
            return BadRequest(
                $"This removes every keyword mapping and suppression on the site. Send confirm={ConfirmationToken} if that is what you want.");
        }

        return Ok(_uninstaller.RemoveData());
    }
}
