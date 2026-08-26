using Microsoft.Extensions.Logging;
using OC.AutoLink.Relations;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the relation type auto-link relations are written under.
/// </summary>
/// <remarks>
/// A migration rather than a startup check, for the same reason the tables are: it is a one-off, and a package
/// writing to <c>umbracoRelationType</c> on every boot is not what anybody expects.
/// <para>
/// <c>isDependency: true</c> is the line that matters. It is what puts these relations in front of an editor about
/// to delete a page, through Umbraco's own tracked references rather than a warning of ours bolted on beside it.
/// </para>
/// </remarks>
internal sealed class AddKeywordRelationType : AsyncMigrationBase
{
    private readonly IRelationService _relationService;

    public AddKeywordRelationType(IMigrationContext context, IRelationService relationService)
        : base(context) => _relationService = relationService;

    protected override Task MigrateAsync()
    {
        if (_relationService.GetRelationTypeByAlias(AutoLinkRelation.Alias) is not null)
        {
            // Somebody created it by hand, or a previous run got this far. Either way it is theirs now.
            Logger.LogDebug("The {Alias} relation type is already present.", AutoLinkRelation.Alias);
            return Task.CompletedTask;
        }

        var relationType = new RelationType(
            AutoLinkRelation.Name,
            AutoLinkRelation.Alias,
            isBidrectional: false,
            parentObjectType: Constants.ObjectTypes.Document,
            childObjectType: Constants.ObjectTypes.Document,
            isDependency: true,
            key: null);

        _relationService.Save(relationType);

        Logger.LogInformation(
            "Created the {Alias} relation type, so pages carrying auto-links are tracked as dependencies.",
            AutoLinkRelation.Alias);

        return Task.CompletedTask;
    }
}
