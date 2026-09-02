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
            scope.Database.Execute($"DROP TABLE IF EXISTS {KeywordMappingDto.TableName}");
            scope.Database.Execute($"DROP TABLE IF EXISTS {KeywordSuppressionDto.TableName}");
        }

        RemoveRelations();

        _keyValueService.SetValue(upgrader.StateValueKey, plan.InitialState);

        _invalidator.InvalidateEverywhere();

        _logger.LogWarning(
            "Auto-link data removed: every keyword and suppression dropped with their tables, and the migration state at {Key} reset. Document types were left untouched.",
            upgrader.StateValueKey);

        return new AutoLinkUninstallResult(upgrader.StateValueKey);
    }

    /// <summary>
    /// Drops the relation type and every relation written under it.
    /// </summary>
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
            _logger.LogError(ex, "Could not remove the auto-link relation type. Delete it by hand in Settings.");
        }
    }
}
