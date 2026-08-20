using Microsoft.Extensions.Logging;
using OC.AutoLink.Persistence.Migrations;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace OC.AutoLink.Notifications;

/// <summary>
/// Runs the package's migration plan at startup.
/// </summary>
/// <remarks>
/// Unlike the schema installer next door, this is a real migration plan rather than a startup fixup: the
/// mapping table holds editorial decisions, so it is not something to recreate opportunistically. Failures
/// are logged rather than thrown; a missing table degrades to automatic resolution instead of stopping boot.
/// </remarks>
public sealed class AutoLinkMigrationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;
    private readonly ILogger<AutoLinkMigrationHandler> _logger;

    public AutoLinkMigrationHandler(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor migrationPlanExecutor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState,
        ILogger<AutoLinkMigrationHandler> logger)
    {
        _scopeProvider = scopeProvider;
        _migrationPlanExecutor = migrationPlanExecutor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (_runtimeState.Level != RuntimeLevel.Run)
        {
            // Mid-install or mid-upgrade. Umbraco will start us again once it is done.
            return;
        }

        try
        {
            var upgrader = new Upgrader(new AutoLinkMigrationPlan());
            await upgrader.ExecuteAsync(_migrationPlanExecutor, _scopeProvider, _keyValueService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The auto-link migration plan failed. Manual keyword mappings will be unavailable.");
        }
    }
}
