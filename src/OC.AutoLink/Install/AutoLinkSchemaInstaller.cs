using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;

namespace OC.AutoLink.Install;

/// <summary>
/// Ensures the keyword Tags datatype exists and is attached to the configured document types.
/// </summary>
/// <remarks>
/// Proof of concept convenience so the retroactivity demo is reproducible from a clean database without any
/// clicking. A shipping package would do this with a migration plan instead of at every startup. Everything
/// here is additive and idempotent, and failures are logged rather than thrown — this must never stop boot.
/// </remarks>
public sealed class AutoLinkSchemaInstaller : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string DataTypeName = "Auto-link Keywords";
    private const string PropertyGroupAlias = "autoLinking";
    private const string PropertyGroupName = "Auto-linking";

    private readonly IDataTypeService _dataTypeService;
    private readonly IContentTypeService _contentTypeService;
    private readonly PropertyEditorCollection _propertyEditors;
    private readonly IConfigurationEditorJsonSerializer _serializer;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly ILogger<AutoLinkSchemaInstaller> _logger;

    public AutoLinkSchemaInstaller(
        IDataTypeService dataTypeService,
        IContentTypeService contentTypeService,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer serializer,
        IShortStringHelper shortStringHelper,
        IOptionsMonitor<AutoLinkOptions> options,
        ILogger<AutoLinkSchemaInstaller> logger)
    {
        _dataTypeService = dataTypeService;
        _contentTypeService = contentTypeService;
        _propertyEditors = propertyEditors;
        _serializer = serializer;
        _shortStringHelper = shortStringHelper;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        AutoLinkOptions options = _options.CurrentValue;
        if (!options.InstallSchema || options.InstallOnDocumentTypes.Length == 0)
        {
            return;
        }

        try
        {
            IDataType? keywordsDataType = await EnsureKeywordsDataTypeAsync(options);
            if (keywordsDataType is null)
            {
                return;
            }

            IDataType? booleanDataType = (await _dataTypeService.GetByEditorAliasAsync(
                Constants.PropertyEditors.Aliases.Boolean)).FirstOrDefault();

            foreach (string alias in options.InstallOnDocumentTypes)
            {
                await EnsurePropertiesAsync(alias, keywordsDataType, booleanDataType, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-link schema install failed. Add the keyword properties by hand, or set OC:AutoLink:InstallSchema to false.");
        }
    }

    private async Task<IDataType?> EnsureKeywordsDataTypeAsync(AutoLinkOptions options)
    {
        IDataType? existing = await _dataTypeService.GetAsync(DataTypeName);
        if (existing is not null)
        {
            return existing;
        }

        if (!_propertyEditors.TryGet(Constants.PropertyEditors.Aliases.Tags, out IDataEditor? tagsEditor))
        {
            _logger.LogError("Could not resolve the {Alias} property editor.", Constants.PropertyEditors.Aliases.Tags);
            return null;
        }

        var dataType = new DataType(tagsEditor, _serializer)
        {
            Name = DataTypeName,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = new Dictionary<string, object>
            {
                ["group"] = options.TagGroup,
                ["storageType"] = nameof(TagsStorageType.Json),
            },
        };

        Attempt<IDataType, DataTypeOperationStatus> result =
            await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey);

        if (!result.Success)
        {
            _logger.LogError("Could not create the {Name} datatype: {Status}.", DataTypeName, result.Status);
            return null;
        }

        _logger.LogInformation("Created the {Name} datatype in tag group '{Group}'.", DataTypeName, options.TagGroup);
        return result.Result;
    }

    private async Task EnsurePropertiesAsync(
        string contentTypeAlias,
        IDataType keywordsDataType,
        IDataType? booleanDataType,
        AutoLinkOptions options)
    {
        IContentType? contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
        {
            _logger.LogDebug("Document type '{Alias}' not found, skipping.", contentTypeAlias);
            return;
        }

        bool modified = false;

        if (!contentType.PropertyTypeExists(options.KeywordsPropertyAlias))
        {
            EnsurePropertyGroup(contentType);

            var keywords = new PropertyType(_shortStringHelper, keywordsDataType)
            {
                Alias = options.KeywordsPropertyAlias,
                Name = "Link keywords",
                Description = "Phrases that should link to this page wherever they appear in rich text elsewhere on the site.",
                SortOrder = 10,
            };

            contentType.AddPropertyType(keywords, PropertyGroupAlias, PropertyGroupName);
            modified = true;
        }

        if (booleanDataType is not null && !contentType.PropertyTypeExists(options.ExcludePropertyAlias))
        {
            EnsurePropertyGroup(contentType);

            var exclude = new PropertyType(_shortStringHelper, booleanDataType)
            {
                Alias = options.ExcludePropertyAlias,
                Name = "Exclude from auto-linking",
                Description = "Stop this page's rich text being scanned. It can still be a link target.",
                SortOrder = 20,
            };

            contentType.AddPropertyType(exclude, PropertyGroupAlias, PropertyGroupName);
            modified = true;
        }

        if (!modified)
        {
            return;
        }

        Attempt<ContentTypeOperationStatus> result =
            await _contentTypeService.UpdateAsync(contentType, Constants.Security.SuperUserKey);

        if (!result.Success)
        {
            _logger.LogError(
                "Could not add auto-link properties to document type '{Alias}': {Status}.",
                contentTypeAlias,
                result.Result);
            return;
        }

        _logger.LogInformation("Added auto-link properties to document type '{Alias}'.", contentTypeAlias);
    }

    private static void EnsurePropertyGroup(IContentType contentType)
    {
        if (contentType.PropertyGroups.All(g => g.Alias != PropertyGroupAlias))
        {
            contentType.AddPropertyGroup(PropertyGroupAlias, PropertyGroupName);
        }
    }
}
