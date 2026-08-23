using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OC.AutoLink.Api.Models;
using OC.AutoLink.Caching;
using OC.AutoLink.Persistence;
using OC.AutoLink.Relations;
using OC.AutoLink.Scanning;

namespace OC.AutoLink.Api.Controllers;

/// <summary>
/// The audit half of the section: which pages would carry auto-links, and switching individual ones off.
/// </summary>
public sealed class AutoLinkReportController : AutoLinkControllerBase
{
    private readonly IAutoLinkScanner _scanner;
    private readonly IKeywordSuppressionStore _suppressions;
    private readonly IAutoLinkRelationWriter _relations;
    private readonly ILogger<AutoLinkReportController> _logger;

    public AutoLinkReportController(
        IAutoLinkScanner scanner,
        IKeywordSuppressionStore suppressions,
        IAutoLinkRelationWriter relations,
        ILogger<AutoLinkReportController> logger)
    {
        _scanner = scanner;
        _suppressions = suppressions;
        _relations = relations;
        _logger = logger;
    }

    /// <summary>
    /// Runs the linker across published content in dry-run mode and reports what it would link.
    /// </summary>
    /// <remarks>
    /// Reconciling the relations here, off the back of a read, is deliberate but worth being honest about. A scan is
    /// the only thing that knows which pages carry which links, and it already walks every published page — so the
    /// alternative is either a second walk somewhere else or relations that go stale until somebody remembers to
    /// rebuild them. Stale relations mean a delete warning that lies, which is worse than a GET with a side effect.
    /// The write is idempotent and cheap next to the scan itself.
    /// </remarks>
    [HttpGet("scan")]
    [ProducesResponseType(typeof(AutoLinkScanReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        AutoLinkScanReport report = await _scanner.ScanAsync(cancellationToken);

        try
        {
            _relations.Reconcile(report);
        }
        catch (Exception ex)
        {
            // The report is the answer the caller asked for; the relations are bookkeeping on top of it. A failure
            // here must not turn a good scan into a failed request.
            _logger.LogError(ex, "The scan succeeded but the auto-link relations could not be reconciled.");
        }

        return Ok(report);
    }

    /// <summary>
    /// Rebuilds the relations without the caller needing the report, for a scheduled job or a console command.
    /// </summary>
    [HttpPost("relations")]
    [ProducesResponseType(typeof(AutoLinkRelationChanges), StatusCodes.Status200OK)]
    public async Task<IActionResult> Relations(CancellationToken cancellationToken)
    {
        AutoLinkScanReport report = await _scanner.ScanAsync(cancellationToken);

        return Ok(_relations.Reconcile(report));
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

        return Ok(new { removed });
    }
}
