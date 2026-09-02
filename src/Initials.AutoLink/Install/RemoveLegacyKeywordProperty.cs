using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Install;

/// <summary>
/// Removes the keyword Tags property and datatype an earlier version of this package installed.
/// </summary>
internal sealed class RemoveLegacyKeywordProperty : AsyncMigrationBase
{
    /// <summary>
    /// The alias the old installer used. Hard-coded rather than read from configuration, because the option that
    /// carried it has been removed — and a cleanup that depended on the setting being left in place would skip
    /// exactly the sites that had already tidied it away.
    /// </summary>
    private const string LegacyPropertyAlias = "linkKeywords";

    /// <summary>Name the old installer gave the datatype it created.</summary>
    private const string LegacyDataTypeName = "Auto-link Keywords";

    private readonly IDataTypeService _dataTypeService;
    private readonly IContentTypeService _contentTypeService;

    public RemoveLegacyKeywordProperty(
        IMigrationContext context,
        IDataTypeService dataTypeService,
        IContentTypeService contentTypeService)
        : base(context)
    {
        _dataTypeService = dataTypeService;
        _contentTypeService = contentTypeService;
    }

    protected override async Task MigrateAsync()
    {
        IDataType? dataType = await _dataTypeService.GetAsync(LegacyDataTypeName);

        if (dataType is null)
        {
            Logger.LogDebug(
                "No {Name} datatype, so there is no legacy keyword property to remove.", LegacyDataTypeName);
            return;
        }

        var cleared = new List<string>();

        foreach (IContentType contentType in _contentTypeService.GetAll().ToList())
        {
            IPropertyType? property = contentType.PropertyTypes.FirstOrDefault(
                pt => pt.Alias == LegacyPropertyAlias && pt.DataTypeKey == dataType.Key);

            if (property is null)
            {
                continue;
            }

            contentType.RemovePropertyType(LegacyPropertyAlias);

            Attempt<ContentTypeOperationStatus> result =
                await _contentTypeService.UpdateAsync(contentType, Constants.Security.SuperUserKey);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Could not remove the '{LegacyPropertyAlias}' property from document type '{contentType.Alias}': {result.Result}.");
            }

            cleared.Add(contentType.Alias);
        }

        bool stillInUse = _contentTypeService.GetAll()
            .SelectMany(contentType => contentType.PropertyTypes)
            .Any(pt => pt.DataTypeKey == dataType.Key);

        if (stillInUse)
        {
            Logger.LogWarning(
                "Removed the legacy '{Alias}' property from {Types}, but the {Name} datatype is still used elsewhere and was left in place.",
                LegacyPropertyAlias,
                string.Join(", ", cleared),
                LegacyDataTypeName);

            return;
        }

        await _dataTypeService.DeleteAsync(dataType.Key, Constants.Security.SuperUserKey);

        Logger.LogWarning(
            "Removed the legacy '{Alias}' property from {Count} document type(s) ({Types}) and deleted the {Name} datatype. Keywords now live on the Autolink screen; the tag values stored against that property are gone.",
            LegacyPropertyAlias,
            cleared.Count,
            cleared.Count == 0 ? "none" : string.Join(", ", cleared),
            LegacyDataTypeName);
    }
}
