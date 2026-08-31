using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the manual mapping table.
/// </summary>
internal sealed class AddKeywordMappingTable : AsyncMigrationBase
{
    public AddKeywordMappingTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(KeywordMappingDto.TableName))
        {
            Logger.LogDebug("{Table} already exists, skipping.", KeywordMappingDto.TableName);
            return Task.CompletedTask;
        }

        Create.Table<KeywordMappingDto>().Do();

        return Task.CompletedTask;
    }
}
