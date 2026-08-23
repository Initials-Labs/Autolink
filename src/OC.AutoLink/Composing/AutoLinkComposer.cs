using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OC.AutoLink.Api;
using OC.AutoLink.Caching;
using OC.AutoLink.Api.Security;
using OC.AutoLink.Linking;
using OC.AutoLink.Notifications;
using OC.AutoLink.Persistence;
using OC.AutoLink.PropertyEditors;
using OC.AutoLink.Registry;
using OC.AutoLink.Relations;
using OC.AutoLink.Scanning;
using OC.AutoLink.Uninstall;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Extensions;

namespace OC.AutoLink.Composing;

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

        // Replacing the built-in converter removes it from the collection, and with it its DI registration.
        // Register it explicitly so the wrapper can still resolve and delegate to it.
        builder.Services.AddTransient<RteBlockRenderingValueConverter>();

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
