using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the manual mapping table.
/// </summary>
public sealed class AddKeywordMappingTable : MigrationBase
{
    public AddKeywordMappingTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(KeywordMappingDto.TableName))
        {
            Logger.LogDebug("{Table} already exists, skipping.", KeywordMappingDto.TableName);
            return;
        }

        Create.Table<KeywordMappingDto>().Do();
    }
}
