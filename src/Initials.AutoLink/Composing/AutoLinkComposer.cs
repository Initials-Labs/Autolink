using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Initials.AutoLink.Api;
using Initials.AutoLink.Caching;
using Initials.AutoLink.Api.Security;
using Initials.AutoLink.Linking;
using Initials.AutoLink.Notifications;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.PropertyEditors;
using Initials.AutoLink.Registry;
using Initials.AutoLink.Relations;
using Initials.AutoLink.Scanning;
using Initials.AutoLink.Telemetry;
using Initials.AutoLink.Uninstall;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Infrastructure.Telemetry.Interfaces;
using Umbraco.Extensions;

namespace Initials.AutoLink.Composing;

public sealed class AutoLinkComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services
            .AddOptions<AutoLinkOptions>()
            .Bind(builder.Config.GetSection(AutoLinkOptions.SectionName));

        builder.Services.AddSingleton<IKeywordRegistryInvalidator, KeywordRegistryInvalidator>();
        builder.Services.AddSingleton<IKeywordMappingStore, KeywordMappingStore>();
        builder.Services.AddSingleton<IKeywordSuppressionStore, KeywordSuppressionStore>();
        builder.Services.AddSingleton<IAutoLinkScanner, AutoLinkScanner>();
        builder.Services.AddSingleton<IAutoLinkRelationWriter, AutoLinkRelationWriter>();
        builder.Services.AddSingleton<IAutoLinkUninstaller, AutoLinkUninstaller>();
        builder.Services.AddSingleton<IKeywordRegistry, KeywordRegistry>();
        builder.Services.AddSingleton<IAutoLinker, AutoLinker>();

        // Adds our counts to the telemetry report Umbraco already sends, not a report of our own. Plain DI rather
        // than a collection builder: UsageInformationService takes IEnumerable<IDetailedTelemetryProvider>.
        builder.Services.AddTransient<IDetailedTelemetryProvider, AutoLinkTelemetryProvider>();

        // Replacing a built-in converter removes it from the collection, and with it its DI registration.
        // Register them explicitly so the wrappers can still resolve and delegate to them.
        builder.Services.AddTransient<RteBlockRenderingValueConverter>();
        builder.Services.AddTransient<MarkdownEditorValueConverter>();

        // Umbraco registers a policy per built-in section; ours needs registering the same way, with the same
        // requirement type its own section policies use, so access follows the user group section grant.
        builder.Services.AddSingleton<IAuthorizationHandler, SectionAccessHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, AdministratorHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AutoLinkApiConfiguration.PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(Constants.Security.BackOfficeAuthenticationType);
                policy.Requirements.Add(new SectionAccessRequirement(AutoLinkApiConfiguration.SectionAlias));
            });

            // Teardown is its own policy rather than a check inside the action, so the next destructive endpoint
            // has something to inherit instead of inventing its own guard.
            options.AddPolicy(AutoLinkApiConfiguration.TeardownPolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(Constants.Security.BackOfficeAuthenticationType);
                policy.Requirements.Add(new AdministratorRequirement());
            });
        });

        // Gives the package endpoints their own document at /umbraco/swagger.
        builder.Services.ConfigureOptions<ConfigureAutoLinkSwaggerGenOptions>();

        // Carries our own table changes to every server, the way Umbraco carries content changes.
        builder.CacheRefreshers().Add<AutoLinkCacheRefresher>();

        builder.PropertyValueConverters()
            .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();
        builder.PropertyValueConverters()
            .Replace<MarkdownEditorValueConverter, AutoLinkMarkdownValueConverter>();

        // One startup handler, two plans: the decision tables, and the optional keyword schema. See
        // AutoLinkMigrationHandler for why the second is conditional.
        builder
            .AddNotificationHandler<ContentCacheRefresherNotification, KeywordRegistryInvalidationHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, AutoLinkMigrationHandler>();

        // Warns before a page other pages auto-link to is deleted, and clears the relations once it is gone. The
        // warning in the delete dialog itself comes from Umbraco, off the back of the dependency relation type.
        builder
            .AddNotificationHandler<ContentMovingToRecycleBinNotification, AutoLinkRelationHandler>()
            .AddNotificationHandler<ContentDeletingNotification, AutoLinkRelationHandler>()
            .AddNotificationHandler<ContentDeletedNotification, AutoLinkRelationHandler>();
    }
}
