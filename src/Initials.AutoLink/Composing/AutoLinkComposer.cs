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

        builder.Services.AddTransient<IDetailedTelemetryProvider, AutoLinkTelemetryProvider>();

        builder.Services.AddTransient<RteBlockRenderingValueConverter>();
        builder.Services.AddTransient<MarkdownEditorValueConverter>();

        builder.Services.AddSingleton<IAuthorizationHandler, SectionAccessHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, AdministratorHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AutoLinkApiConfiguration.PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(Constants.Security.BackOfficeAuthenticationType);
                policy.Requirements.Add(new SectionAccessRequirement(AutoLinkApiConfiguration.SectionAlias));
            });

            options.AddPolicy(AutoLinkApiConfiguration.TeardownPolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(Constants.Security.BackOfficeAuthenticationType);
                policy.Requirements.Add(new AdministratorRequirement());
            });
        });

        builder.Services.ConfigureOptions<ConfigureAutoLinkSwaggerGenOptions>();

        builder.CacheRefreshers().Add<AutoLinkCacheRefresher>();

        builder.PropertyValueConverters()
            .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();
        builder.PropertyValueConverters()
            .Replace<MarkdownEditorValueConverter, AutoLinkMarkdownValueConverter>();

        builder
            .AddNotificationHandler<ContentCacheRefresherNotification, KeywordRegistryInvalidationHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, AutoLinkMigrationHandler>();

        builder
            .AddNotificationHandler<ContentMovingToRecycleBinNotification, AutoLinkRelationHandler>()
            .AddNotificationHandler<ContentDeletingNotification, AutoLinkRelationHandler>()
            .AddNotificationHandler<ContentDeletedNotification, AutoLinkRelationHandler>();
    }
}
