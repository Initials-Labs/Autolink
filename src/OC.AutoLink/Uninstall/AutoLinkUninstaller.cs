using Microsoft.Extensions.Logging;
using OC.AutoLink.Caching;
using OC.AutoLink.Persistence;
using OC.AutoLink.Persistence.Migrations;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace OC.AutoLink.Uninstall;

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
/// Document types are deliberately left alone. The keyword property holds editors' data, and deleting it would take
/// every keyword on every page with it — that is not a package's decision to make.
/// </remarks>
public sealed class AutoLinkUninstaller : IAutoLinkUninstaller
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IKeyValueService _keyValueService;
    private readonly IKeywordRegistryInvalidator _invalidator;
    private readonly ILogger<AutoLinkUninstaller> _logger;

    public AutoLinkUninstaller(
        IScopeProvider scopeProvider,
        IKeyValueService keyValueService,
        IKeywordRegistryInvalidator invalidator,
        ILogger<AutoLinkUninstaller> logger)
    {
        _scopeProvider = scopeProvider;
        _keyValueService = keyValueService;
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

        _keyValueService.SetValue(upgrader.StateValueKey, plan.InitialState);

        // Whatever the registry was holding is now built on tables that no longer exist.
        _invalidator.InvalidateEverywhere();

        _logger.LogWarning(
            "Auto-link data removed: both decision tables dropped and the migration state at {Key} reset. Document types were left untouched.",
            upgrader.StateValueKey);

        return new AutoLinkUninstallResult(upgrader.StateValueKey);
    }
}
