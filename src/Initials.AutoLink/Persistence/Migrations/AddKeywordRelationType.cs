using Microsoft.Extensions.Logging;
using Initials.AutoLink.Relations;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the relation type auto-link relations are written under.
/// </summary>
internal sealed class AddKeywordRelationType : AsyncMigrationBase
{
    private readonly IRelationService _relationService;

    public AddKeywordRelationType(IMigrationContext context, IRelationService relationService)
        : base(context) => _relationService = relationService;

    protected override Task MigrateAsync()
    {
        if (_relationService.GetRelationTypeByAlias(AutoLinkRelation.Alias) is not null)
        {
            Logger.LogDebug("The {Alias} relation type is already present.", AutoLinkRelation.Alias);
            return Task.CompletedTask;
        }

        if (_relationService.GetAllRelationTypes()
                .FirstOrDefault(t => t.Name == AutoLinkRelation.Name) is { } collision)
        {
            Logger.LogWarning(
                "A relation type named '{Name}' already exists with alias {ExistingAlias}, so the {Alias} relation "
                + "type was not created and pages carrying auto-links will not be tracked as dependencies. Rename or "
                + "remove the existing relation type and restart to enable dependency tracking.",
                AutoLinkRelation.Name,
                collision.Alias,
                AutoLinkRelation.Alias);
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
