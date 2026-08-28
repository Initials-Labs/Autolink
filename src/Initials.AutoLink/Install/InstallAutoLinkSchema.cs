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
/// <remarks>
/// One property, and an optional one. Keywords used to be installed alongside it as a Tags property on every target
/// document type, which is what made this class worth having; they now live in the package's own table and are
/// edited on the Auto-linking screen, so the only thing left that genuinely belongs on a page is whether that page's
/// rich text gets scanned.
/// <para>
/// A migration rather than a startup handler. The work is a one-off bootstrap, and doing it on every boot meant a
/// package editing document types every time the site started, which is nobody's expectation — and on a site using
/// <c>InMemoryAuto</c> models it regenerated the models under already-compiled views, so the first page load after
/// an install failed.
/// </para>
/// <para>
/// Everything here stays additive and idempotent even though it now runs once: an existing property is left alone,
/// and the property group is only added when it is absent. Nothing is renamed, moved or deleted, so running this
/// against a site somebody has since customised cannot undo their work.
/// </para>
/// <para>
/// A nominated document type that does not exist is the one case that is not simply skipped: this throws, which
/// leaves the plan state where it was so the next boot tries again. An unattended install imports its starter kit
/// around the same runtime this hooks, so the document types can genuinely arrive after the first attempt, and a
/// one-shot that quietly gave up on them would leave a site that looks installed and scans nothing it was told to
/// leave alone. A wrong alias in configuration lands in the same place, which is the honest outcome: it is a
/// misconfiguration, and it says so once per boot until somebody fixes it.
/// </para>
/// </remarks>
internal sealed class InstallAutoLinkSchema : AsyncMigrationBase
{
    private const string PropertyGroupAlias = "autoLinking";
    private const string PropertyGroupName = "Auto-linking";

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
            // The handler does not execute this plan in that state. This is the belt to its braces: a migration that
            // added a property nobody asked for, because it was reached by some other route, would be worse.
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
            Name = "Exclude from auto-linking",
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
