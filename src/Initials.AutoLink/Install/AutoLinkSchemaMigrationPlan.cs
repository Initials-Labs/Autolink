using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Install;

/// <summary>
/// The plan for the optional editor schema: the scan opt-out property on the document types somebody nominated.
/// </summary>
internal sealed class AutoLinkSchemaMigrationPlan : MigrationPlan
{
    public AutoLinkSchemaMigrationPlan() : base("Initials.AutoLink.Schema")
    {
        From(string.Empty)
            .To<InstallAutoLinkSchema>("autolink-keyword-schema");
    }
}
