using Microsoft.Extensions.Logging;
using Initials.AutoLink.Caching;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Persistence.Migrations;
using Initials.AutoLink.Relations;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Initials.AutoLink.Uninstall;

/// <summary>
/// The outcome of a teardown: the migration state key that was reset, so a caller can see which plan was rewound.
/// </summary>
/// <remarks>
/// Deliberately reports no per-table flag. The drops are unconditional and idempotent, and a DDL statement's
/// affected-row count says nothing about whether the table was there, so any such flag would read as an answer
/// while being incapable of varying with the truth.
/// </remarks>
public sealed record AutoLinkUninstallResult(string MigrationStateKey);

/// <summary>
/// Removes everything this package created in the database.
/// </summary>
public interface IAutoLinkUninstaller
{
    AutoLinkUninstallResult RemoveData();
}

/// <inheritdoc />
/// <remarks>
/// Umbraco has no uninstall hook for a package delivered over NuGet: removing the reference removes the assembly and
/// leaves the tables. So teardown is an explicit action rather than something that happens on its own.
/// <para>
/// The part that is easy to get wrong is the migration state. Dropping the tables while leaving the plan recorded as
/// complete in umbracoKeyValue means a reinstall never recreates them, and the package comes back up permanently
/// broken with a store that logs and returns empty. Resetting the state to the plan's initial value is what makes a
/// reinstall work.
/// </para>
/// Document types are deliberately left alone, and only this plan is rewound — not the <c>Initials.AutoLink.Schema</c> plan
/// that added the scan opt-out property. Rewinding that one would have a reinstall re-add a property somebody may
/// have removed on purpose, and re-adding schema is not what "remove the data" means.
/// <para>
/// Worth being clear about what this does destroy. These two tables are the only place keywords live, so a teardown
/// is not "lose the overrides and keep the keywords" — it is all of them. That is the right behaviour for removing a
/// package and the wrong thing to run by accident, which is what the confirmation token on the endpoint is for.
/// </para>
/// </remarks>
internal sealed class AutoLinkUninstaller : IAutoLinkUninstaller
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IKeyValueService _keyValueService;
    private readonly IRelationService _relationService;
    private readonly IKeywordRegistryInvalidator _invalidator;
    private readonly ILogger<AutoLinkUninstaller> _logger;

    public AutoLinkUninstaller(
        IScopeProvider scopeProvider,
        IKeyValueService keyValueService,
        IRelationService relationService,
        IKeywordRegistryInvalidator invalidator,
        ILogger<AutoLinkUninstaller> logger)
    {
        _scopeProvider = scopeProvider;
        _keyValueService = keyValueService;
        _relationService = relationService;
        _invalidator = invalidator;
        _logger = logger;
    }

    /// <inheritdoc />
    public AutoLinkUninstallResult RemoveData()
    {
        var plan = new AutoLinkMigrationPlan();
        var upgrader = new Upgrader(plan);

        using (IScope scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            // DROP TABLE IF EXISTS rather than a syntax provider probe: supported by SQLite and by every SQL Server
            // version Umbraco 17 runs on, and it keeps the teardown idempotent.
            scope.Database.Execute($"DROP TABLE IF EXISTS {KeywordMappingDto.TableName}");
            scope.Database.Execute($"DROP TABLE IF EXISTS {KeywordSuppressionDto.TableName}");
        }

        RemoveRelations();

        _keyValueService.SetValue(upgrader.StateValueKey, plan.InitialState);

        // Whatever the registry was holding is now built on tables that no longer exist.
        _invalidator.InvalidateEverywhere();

        _logger.LogWarning(
            "Auto-link data removed: every keyword and suppression dropped with their tables, and the migration state at {Key} reset. Document types were left untouched.",
            upgrader.StateValueKey);

        return new AutoLinkUninstallResult(upgrader.StateValueKey);
    }

    /// <summary>
    /// Drops the relation type and every relation written under it.
    /// </summary>
    /// <remarks>
    /// Deleting the type takes its relations with it, but they are cleared first regardless: this runs against
    /// whatever state the database is actually in, and a half-finished install with relations and no type is
    /// exactly the case a teardown exists to mop up.
    /// <para>
    /// Only ours. Relation types Umbraco ships, and any somebody else made, are none of this method's business.
    /// </para>
    /// </remarks>
    private void RemoveRelations()
    {
        try
        {
            IRelationType? relationType = _relationService.GetRelationTypeByAlias(AutoLinkRelation.Alias);

            if (relationType is null)
            {
                return;
            }

            _relationService.DeleteRelationsOfType(relationType);
            _relationService.Delete(relationType);

            _logger.LogWarning("Auto-link relation type {Alias} and its relations removed.", AutoLinkRelation.Alias);
        }
        catch (Exception ex)
        {
            // The tables are the data; the relations are bookkeeping over content that still exists. Failing here
            // must not leave the tables dropped but the migration state untouched, which is the one combination
            // that comes back broken.
            _logger.LogError(ex, "Could not remove the auto-link relation type. Delete it by hand in Settings.");
        }
    }
}
