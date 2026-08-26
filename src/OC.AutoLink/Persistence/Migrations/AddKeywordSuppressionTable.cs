using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the keyword suppression table.
/// </summary>
internal sealed class AddKeywordSuppressionTable : AsyncMigrationBase
{
    public AddKeywordSuppressionTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(KeywordSuppressionDto.TableName))
        {
            Logger.LogDebug("{Table} already exists, skipping.", KeywordSuppressionDto.TableName);
            return Task.CompletedTask;
        }

        Create.Table<KeywordSuppressionDto>().Do();

        return Task.CompletedTask;
    }
}
