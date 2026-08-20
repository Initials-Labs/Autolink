using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Persistence.Migrations;

/// <summary>
/// The package's own migration plan. Its state is tracked in umbracoKeyValue under the plan name, so the
/// table is created once rather than probed at every boot.
/// </summary>
public sealed class AutoLinkMigrationPlan : MigrationPlan
{
    public AutoLinkMigrationPlan() : base("OC.AutoLink")
    {
        From(string.Empty)
            .To<AddKeywordMappingTable>("autolink-keyword-mapping-table")
            .To<AddKeywordSuppressionTable>("autolink-keyword-suppression-table")
            .To<AddCultureToDecisions>("autolink-decisions-culture")
            .To<AddExternalLinkColumns>("autolink-external-links");
    }
}
