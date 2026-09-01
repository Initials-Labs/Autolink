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
/// <remarks>
/// This is the one place the package deletes somebody's data, and it is deliberate. Keywords moved into the
/// package's own table and are edited on the Autolink screen; the property left behind on every target
/// document type reads exactly like the one that used to work, so an editor filling it in gets no links and no
/// explanation. A field that silently does nothing is worse than no field.
/// <para>
/// Scoped by <em>datatype</em>, not by configuration. It removes the property only where it is bound to the
/// "Auto-link Keywords" datatype this package created, so a <c>linkKeywords</c> property somebody rebound to a
/// Tags datatype of their own — for their own purposes — is left alone. If the datatype is not there, this does
/// nothing at all, which covers installs that added the properties by hand and every subsequent boot.
/// </para>
/// <para>
/// The datatype goes last, and only once nothing points at it. Removing the properties is what takes the stored
/// values with them, and it goes through the service layer rather than SQL so Umbraco updates its own caches and
/// the content types come back consistent.
/// </para>
/// <para>
/// What this does <em>not</em> clear is <c>cmsTags</c>. The rows for the tag group survive with no relationships
/// left pointing at them — verified on 17.6.1, where twelve of them stayed behind. That is ordinary Umbraco
/// behaviour, since core has no notion of collecting unused tags, and they are inert: nothing queries that group
/// any more. Deleting them would mean going at the tag tables directly, which is not a trade worth making to tidy
/// up rows nothing reads.
/// </para>
/// <para>
/// <see cref="AutoLinkOptions.ExcludePropertyAlias"/> is untouched. It sits on Umbraco's built-in True/false
/// datatype, it is still what opts a page out of being scanned, and it is genuinely a property of a page.
/// </para>
/// </remarks>
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

        // Anything still bound to it is somebody else's, so the datatype stays and so does their data.
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
