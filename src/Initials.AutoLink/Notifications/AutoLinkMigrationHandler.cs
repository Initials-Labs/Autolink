using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Initials.AutoLink.Install;
using Initials.AutoLink.Persistence.Migrations;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace Initials.AutoLink.Notifications;

/// <summary>
/// Runs the package's migration plans at startup.
/// </summary>
internal sealed class AutoLinkMigrationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly ILogger<AutoLinkMigrationHandler> _logger;

    public AutoLinkMigrationHandler(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor migrationPlanExecutor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState,
        IOptionsMonitor<AutoLinkOptions> options,
        ILogger<AutoLinkMigrationHandler> logger)
    {
        _scopeProvider = scopeProvider;
        _migrationPlanExecutor = migrationPlanExecutor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (_runtimeState.Level != RuntimeLevel.Run)
        {
            return;
        }

        await ExecuteAsync(new AutoLinkMigrationPlan(), "Manual keyword mappings will be unavailable.");

        AutoLinkOptions options = _options.CurrentValue;

        if (options.InstallSchema && options.InstallOnDocumentTypes.Length > 0)
        {
            await ExecuteAsync(
                new AutoLinkSchemaMigrationPlan(),
                "Add the keyword properties by hand, or set Initials:AutoLink:InstallSchema to false.");
        }
    }

    private async Task ExecuteAsync(MigrationPlan plan, string consequence)
    {
        try
        {
            var upgrader = new Upgrader(plan);
            await upgrader.ExecuteAsync(_migrationPlanExecutor, _scopeProvider, _keyValueService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Plan} migration plan failed. {Consequence}", plan.Name, consequence);
        }
    }
}
