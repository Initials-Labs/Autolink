using Microsoft.Extensions.Logging;
using Initials.AutoLink.Persistence;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Infrastructure.Telemetry.Interfaces;

namespace Initials.AutoLink.Telemetry;

/// <summary>
/// Adds a handful of anonymous counts about this package to the telemetry report Umbraco already sends.
/// </summary>
/// <remarks>
/// This is not telemetry of our own. Umbraco collects every registered <see cref="IDetailedTelemetryProvider"/>
/// through <c>ReportSiteJob</c> and posts the lot to <c>https://telemetry.umbraco.com/</c>, under the level the site
/// owner chose in Settings → Telemetry Data. Nothing here runs unless they picked <c>Detailed</c>, nothing is sent
/// anywhere else, and the package author never sees it. It exists so that the shape is right if HQ ever opens
/// package telemetry up, and so the counts show in the site's own Telemetry Data dashboard, which is the one
/// place a site owner can inspect exactly what a package reports about them.
/// <para>
/// The payload is counts only, on purpose. No keyword text, no URLs, no page keys, no user names: a keyword is
/// editorial content and a destination URL can identify a client, and neither belongs in a report about the
/// package. Every key carries the <c>AutoLink</c> prefix, since the report is a flat bag shared with every other
/// provider on the site.
/// </para>
/// <para>
/// The stores log and return empty if their tables are missing, so this is safe on a site where the migration has
/// not yet run. Anything else is caught and logged rather than thrown: telemetry is the last thing that should be
/// able to fail a background job that reports on the site's health.
/// </para>
/// </remarks>
internal sealed class AutoLinkTelemetryProvider : IDetailedTelemetryProvider
{
    /// <summary>Total keyword rows across every culture.</summary>
    public const string KeywordCount = "AutoLinkKeywordCount";

    /// <summary>Keyword rows that point at an outside URL rather than a page.</summary>
    public const string ExternalKeywordCount = "AutoLinkExternalKeywordCount";

    /// <summary>Distinct cultures with at least one culture-specific keyword. The all-cultures row is not one.</summary>
    public const string CultureCount = "AutoLinkCultureCount";

    /// <summary>Suppressions scoped to a single page.</summary>
    public const string PageSuppressionCount = "AutoLinkPageSuppressionCount";

    /// <summary>Suppressions that switch a keyword off everywhere.</summary>
    public const string GlobalSuppressionCount = "AutoLinkGlobalSuppressionCount";

    private readonly IKeywordMappingStore _mappings;
    private readonly IKeywordSuppressionStore _suppressions;
    private readonly ILogger<AutoLinkTelemetryProvider> _logger;

    public AutoLinkTelemetryProvider(
        IKeywordMappingStore mappings,
        IKeywordSuppressionStore suppressions,
        ILogger<AutoLinkTelemetryProvider> logger)
    {
        _mappings = mappings;
        _suppressions = suppressions;
        _logger = logger;
    }

    public IEnumerable<UsageInformation> GetInformation()
    {
        IReadOnlyList<KeywordMapping> mappings;
        IReadOnlyList<KeywordSuppression> suppressions;

        try
        {
            mappings = _mappings.GetAll();
            suppressions = _suppressions.GetAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-link telemetry could not read the keyword tables and reported nothing.");
            return [];
        }

        return
        [
            new UsageInformation(KeywordCount, mappings.Count),
            new UsageInformation(ExternalKeywordCount, mappings.Count(m => m.IsExternal)),
            new UsageInformation(
                CultureCount,
                mappings.Where(m => m.Culture.Length > 0)
                    .Select(m => m.Culture)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()),
            new UsageInformation(PageSuppressionCount, suppressions.Count(s => !s.IsGlobal)),
            new UsageInformation(GlobalSuppressionCount, suppressions.Count(s => s.IsGlobal)),
        ];
    }
}
