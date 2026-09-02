using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Install;

/// <summary>
/// Adds the scan opt-out property to the nominated document types.
/// </summary>
internal sealed class InstallAutoLinkSchema : AsyncMigrationBase
{
    private const string PropertyGroupAlias = "autoLinking";
    private const string PropertyGroupName = "Autolink";

    private readonly IDataTypeService _dataTypeService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly AutoLinkOptions _options;

    public InstallAutoLinkSchema(
        IMigrationContext context,
        IDataTypeService dataTypeService,
        IContentTypeService contentTypeService,
        IShortStringHelper shortStringHelper,
        IOptions<AutoLinkOptions> options)
        : base(context)
    {
        _dataTypeService = dataTypeService;
        _contentTypeService = contentTypeService;
        _shortStringHelper = shortStringHelper;
        _options = options.Value;
    }

    protected override async Task MigrateAsync()
    {
        if (_options.InstallOnDocumentTypes.Length == 0)
        {
            Logger.LogDebug("No document types are nominated for the auto-link schema, so there is nothing to install.");
            return;
        }

        IDataType booleanDataType = (await _dataTypeService.GetByEditorAliasAsync(
                Constants.PropertyEditors.Aliases.Boolean)).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No datatype uses the {Constants.PropertyEditors.Aliases.Boolean} property editor, so the "
                + $"'{_options.ExcludePropertyAlias}' property could not be added. Add it by hand, or set "
                + "Initials:AutoLink:InstallSchema to false.");

        var missing = new List<string>();

        foreach (string alias in _options.InstallOnDocumentTypes)
        {
            if (!await EnsurePropertyAsync(alias, booleanDataType))
            {
                missing.Add(alias);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Document type(s) '{string.Join("', '", missing)}' were not found, so the auto-link opt-out property could not be added to them. "
                + "The install will be retried on the next startup. Correct Initials:AutoLink:InstallOnDocumentTypes if an alias is wrong, or add the property by hand.");
        }
    }

    /// <summary>
    /// Adds the opt-out property if the document type does not already have it. False when the document type itself
    /// is not there.
    /// </summary>
    private async Task<bool> EnsurePropertyAsync(string contentTypeAlias, IDataType booleanDataType)
    {
        IContentType? contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
        {
            return false;
        }

        if (contentType.PropertyTypeExists(_options.ExcludePropertyAlias))
        {
            return true;
        }

        if (contentType.PropertyGroups.All(group => group.Alias != PropertyGroupAlias))
        {
            contentType.AddPropertyGroup(PropertyGroupAlias, PropertyGroupName);
        }

        var exclude = new PropertyType(_shortStringHelper, booleanDataType)
        {
            Alias = _options.ExcludePropertyAlias,
            Name = "Exclude from Autolink",
            Description = "Stop this page's rich text being scanned. It can still be a link target.",
            SortOrder = 10,
        };

        contentType.AddPropertyType(exclude, PropertyGroupAlias, PropertyGroupName);

        Attempt<ContentTypeOperationStatus> result =
            await _contentTypeService.UpdateAsync(contentType, Constants.Security.SuperUserKey);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Could not add the auto-link opt-out property to document type '{contentTypeAlias}': {result.Result}.");
        }

        Logger.LogInformation("Added the auto-link opt-out property to document type '{Alias}'.", contentTypeAlias);

        return true;
    }
}
