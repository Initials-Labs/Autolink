using Initials.AutoLink.Install;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// The package's own migration plan. Its state is tracked in umbracoKeyValue under the plan name, so the
/// tables are created once rather than probed at every boot.
/// <para>
/// Everything here belongs to every install, which is why the legacy property removal sits on this plan rather
/// than on the optional <see cref="Install.AutoLinkSchemaMigrationPlan"/>. A site that installed the keyword
/// property and later turned the schema installer off still needs the dead property taken away, and that plan
/// would never run to do it.
/// </para>
/// </summary>
internal sealed class AutoLinkMigrationPlan : MigrationPlan
{
    public AutoLinkMigrationPlan() : base("Initials.AutoLink")
    {
        From(string.Empty)
            .To<AddKeywordMappingTable>("autolink-keyword-mapping-table")
            .To<AddKeywordSuppressionTable>("autolink-keyword-suppression-table")
            .To<AddCultureToDecisions>("autolink-decisions-culture")
            .To<AddExternalLinkColumns>("autolink-external-links")
            .To<AddKeywordRelationType>("autolink-relation-type")
            .To<RemoveLegacyKeywordProperty>("autolink-remove-legacy-keyword-property");
    }
}
