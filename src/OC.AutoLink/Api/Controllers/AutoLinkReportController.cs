using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OC.AutoLink.Api.Models;
using OC.AutoLink.Persistence;
using OC.AutoLink.Registry;
using OC.AutoLink.Scanning;

namespace OC.AutoLink.Api.Controllers;

/// <summary>
/// The audit half of the section: which pages would carry auto-links, and switching individual ones off.
/// </summary>
public sealed class AutoLinkReportController : AutoLinkControllerBase
{
    private readonly IAutoLinkScanner _scanner;
    private readonly IKeywordSuppressionStore _suppressions;
    private readonly IKeywordRegistry _registry;

    public AutoLinkReportController(
        IAutoLinkScanner scanner,
        IKeywordSuppressionStore suppressions,
        IKeywordRegistry registry)
    {
        _scanner = scanner;
        _suppressions = suppressions;
        _registry = registry;
    }

    /// <summary>
    /// Runs the linker across published content in dry-run mode and reports what it would link.
    /// </summary>
    [HttpGet("scan")]
    [ProducesResponseType(typeof(AutoLinkScanReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        AutoLinkScanReport report = await _scanner.ScanAsync(cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Stops a keyword being linked, on one page or everywhere.
    /// </summary>
    [HttpPut("suppression")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Suppress(SaveKeywordSuppressionRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Keyword))
        {
            return BadRequest("A suppression needs a keyword.");
        }

        _suppressions.Suppress(model.Keyword, model.PageKey, User.Identity?.Name, model.Culture ?? string.Empty);
        _registry.Invalidate();

        return Ok();
    }

    /// <summary>
    /// Lifts a suppression, letting the keyword link again.
    /// </summary>
    /// <remarks>
    /// Idempotent: lifting a suppression that is not there returns success, because the state the caller asked for
    /// is the state that already holds. Returning 404 made a scope mismatch look like an error the editor could not
    /// clear, when the honest answer was that there was nothing to clear.
    /// </remarks>
    [HttpDelete("suppression")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Allow([FromQuery] string keyword, [FromQuery] Guid pageKey, [FromQuery] string? culture)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return BadRequest("A suppression needs a keyword.");
        }

        bool removed = _suppressions.Allow(keyword, pageKey, culture ?? string.Empty);

        if (removed)
        {
            _registry.Invalidate();
        }

        return Ok(new { removed });
    }
}
