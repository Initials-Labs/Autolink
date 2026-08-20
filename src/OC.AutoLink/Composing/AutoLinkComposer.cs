using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OC.AutoLink.Api;
using OC.AutoLink.Caching;
using OC.AutoLink.Api.Security;
using OC.AutoLink.Install;
using OC.AutoLink.Linking;
using OC.AutoLink.Notifications;
using OC.AutoLink.Persistence;
using OC.AutoLink.PropertyEditors;
using OC.AutoLink.Registry;
using OC.AutoLink.Scanning;
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
        builder.Services.AddSingleton<IKeywordRegistry, KeywordRegistry>();
        builder.Services.AddSingleton<IAutoLinker, AutoLinker>();

        // Replacing the built-in converter removes it from the collection, and with it its DI registration.
        // Register it explicitly so the wrapper can still resolve and delegate to it.
        builder.Services.AddTransient<RteBlockRenderingValueConverter>();

        // Umbraco registers a policy per built-in section; ours needs registering the same way, with the same
        // requirement type its own section policies use, so access follows the user group section grant.
        builder.Services.AddSingleton<IAuthorizationHandler, SectionAccessHandler>();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(AutoLinkApiConfiguration.PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(Constants.Security.BackOfficeAuthenticationType);
                policy.Requirements.Add(new SectionAccessRequirement(AutoLinkApiConfiguration.SectionAlias));
            }));

        // Gives the package endpoints their own document at /umbraco/swagger.
        builder.Services.ConfigureOptions<ConfigureAutoLinkSwaggerGenOptions>();

        // Carries our own table changes to every server, the way Umbraco carries content changes.
        builder.CacheRefreshers().Add<AutoLinkCacheRefresher>();

        builder.PropertyValueConverters()
            .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();

        builder
            .AddNotificationHandler<ContentCacheRefresherNotification, KeywordRegistryInvalidationHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, AutoLinkMigrationHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, AutoLinkSchemaInstaller>();
    }
}
