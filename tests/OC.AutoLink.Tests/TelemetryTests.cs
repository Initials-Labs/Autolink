using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OC.AutoLink.Persistence;
using OC.AutoLink.Telemetry;
using Umbraco.Cms.Core.Models;

namespace OC.AutoLink.Tests;

/// <summary>
/// What the telemetry provider reports, and more importantly what it never does.
/// </summary>
/// <remarks>
/// The report goes to Umbraco HQ under the site owner's consent, so the tests here are as much about the payload
/// containing nothing identifying as about the arithmetic being right. A keyword is editorial content, and a
/// destination URL can name a client.
/// </remarks>
public class TelemetryTests
{
    private static KeywordMapping Page(string keyword, string culture = "") =>
        new(keyword, Guid.NewGuid(), null, null, null, DateTime.UtcNow, "editor@example.com", culture);

    private static KeywordMapping External(string keyword, string url, string culture = "") =>
        new(keyword, Guid.Empty, url, null, null, DateTime.UtcNow, "editor@example.com", culture);

    private static KeywordSuppression OnPage(string keyword) =>
        new(keyword, Guid.NewGuid(), DateTime.UtcNow, "editor@example.com", "");

    private static KeywordSuppression Everywhere(string keyword) =>
        new(keyword, KeywordSuppression.Everywhere, DateTime.UtcNow, "editor@example.com", "");

    private static AutoLinkTelemetryProvider Provider(
        IReadOnlyList<KeywordMapping>? mappings = null,
        IReadOnlyList<KeywordSuppression>? suppressions = null)
    {
        var mappingStore = Substitute.For<IKeywordMappingStore>();
        mappingStore.GetAll().Returns(mappings ?? []);
        var suppressionStore = Substitute.For<IKeywordSuppressionStore>();
        suppressionStore.GetAll().Returns(suppressions ?? []);
        return new AutoLinkTelemetryProvider(
            mappingStore, suppressionStore, NullLogger<AutoLinkTelemetryProvider>.Instance);
    }

    private static Dictionary<string, object> Report(AutoLinkTelemetryProvider provider) =>
        provider.GetInformation().ToDictionary(u => u.Name, u => u.Data);

    [Fact]
    public void Counts_keywords_and_which_of_them_are_external()
    {
        var report = Report(Provider(mappings:
        [
            Page("Umbraco"),
            Page("Harrie"),
            External("Initials CX", "https://initials.co.uk"),
        ]));

        Assert.Equal(3, report[AutoLinkTelemetryProvider.KeywordCount]);
        Assert.Equal(1, report[AutoLinkTelemetryProvider.ExternalKeywordCount]);
    }

    [Fact]
    public void Culture_count_is_distinct_and_ignores_the_all_cultures_row()
    {
        var report = Report(Provider(mappings:
        [
            Page("Umbraco"),                      // all cultures: not a culture
            Page("Harrie", "en-GB"),
            Page("Owain", "en-gb"),               // same culture, different case
            Page("Owain", "en-US"),
        ]));

        Assert.Equal(2, report[AutoLinkTelemetryProvider.CultureCount]);
    }

    [Fact]
    public void Splits_suppressions_into_per_page_and_global()
    {
        var report = Report(Provider(suppressions:
        [
            OnPage("Owain"),
            OnPage("Owain"),
            Everywhere("Harrie"),
        ]));

        Assert.Equal(2, report[AutoLinkTelemetryProvider.PageSuppressionCount]);
        Assert.Equal(1, report[AutoLinkTelemetryProvider.GlobalSuppressionCount]);
    }

    [Fact]
    public void Empty_stores_report_zeros_rather_than_nothing()
    {
        // A site with the package installed and no keywords yet is still a data point.
        var report = Report(Provider());

        Assert.Equal(5, report.Count);
        Assert.All(report.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Payload_is_counts_only_and_every_key_is_prefixed()
    {
        // The interesting assertion in this file. The report is a flat bag shared with every other provider on the
        // site and it leaves the building, so nothing here may be a string the editor typed, and nothing may
        // collide with a core key.
        IEnumerable<UsageInformation> report = Provider(
            mappings: [Page("Client Name", "en-GB"), External("Secret Project", "https://client.example")],
            suppressions: [OnPage("Client Name")]).GetInformation();

        Assert.All(report, u =>
        {
            Assert.StartsWith("AutoLink", u.Name);
            Assert.IsType<int>(u.Data);
        });
    }

    // Not tested here: that Umbraco only includes these at the Detailed level. That is UsageInformationService's
    // behaviour, and the class is internal to Umbraco, so a test would have to reach it by reflection and would be
    // testing their code with ours as the fixture. The registration is verified by booting the site in Development,
    // where the service provider validates the graph on build.

    [Fact]
    public void A_store_that_throws_reports_nothing_instead_of_failing_the_job()
    {
        var mappingStore = Substitute.For<IKeywordMappingStore>();
        mappingStore.GetAll().Throws(new InvalidOperationException("no such table"));
        var provider = new AutoLinkTelemetryProvider(
            mappingStore, Substitute.For<IKeywordSuppressionStore>(), NullLogger<AutoLinkTelemetryProvider>.Instance);

        Assert.Empty(provider.GetInformation());
    }
}
