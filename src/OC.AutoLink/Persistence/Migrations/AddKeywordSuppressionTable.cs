using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace OC.AutoLink.Persistence.Migrations;

/// <summary>
/// Creates the keyword suppression table.
/// </summary>
public sealed class AddKeywordSuppressionTable : MigrationBase
{
    public AddKeywordSuppressionTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(KeywordSuppressionDto.TableName))
        {
            Logger.LogDebug("{Table} already exists, skipping.", KeywordSuppressionDto.TableName);
            return;
        }

        Create.Table<KeywordSuppressionDto>().Do();
    }
}
