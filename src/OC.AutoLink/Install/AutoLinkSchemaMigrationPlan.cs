using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Install;

/// <summary>
/// The plan for the optional editor schema: the scan opt-out property on the document types somebody nominated.
/// </summary>
/// <remarks>
/// Its own plan rather than another step on <see cref="Persistence.Migrations.AutoLinkMigrationPlan"/>, because the
/// two answer independent questions. The keyword tables belong to every install and are created unconditionally;
/// the schema is opt-in and driven by configuration. A single plan would consume this step on installs that have
/// nominated nothing, and a plan step is spent for good — so a site that configured the feature afterwards would
/// never get the property. <see cref="Notifications.AutoLinkMigrationHandler"/> executes this plan only once the
/// feature is actually configured, which is what keeps that step available until there is something for it to do.
/// </remarks>
public sealed class AutoLinkSchemaMigrationPlan : MigrationPlan
{
    public AutoLinkSchemaMigrationPlan() : base("OC.AutoLink.Schema")
    {
        From(string.Empty)
            .To<InstallAutoLinkSchema>("autolink-keyword-schema");
    }
}
