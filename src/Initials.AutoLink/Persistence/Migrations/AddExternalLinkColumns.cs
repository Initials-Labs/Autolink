using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// Adds the external link columns to the mapping table.
/// </summary>
internal sealed class AddExternalLinkColumns : AsyncMigrationBase
{
    public AddExternalLinkColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (!TableExists(KeywordMappingDto.TableName)
            || ColumnExists(KeywordMappingDto.TableName, "externalUrl"))
        {
            return Task.CompletedTask;
        }

        List<PageMappingDto> rows = Database.Fetch<PageMappingDto>(
            $"SELECT keywordKey, keyword, culture, targetKey, updateDate, updatedBy FROM {KeywordMappingDto.TableName}");

        Delete.Table(KeywordMappingDto.TableName).Do();
        Create.Table<KeywordMappingDto>().Do();

        foreach (PageMappingDto row in rows)
        {
            Database.Insert(new KeywordMappingDto
            {
                KeywordKey = row.KeywordKey,
                Keyword = row.Keyword,
                Culture = row.Culture ?? string.Empty,
                TargetKey = row.TargetKey,
                UpdateDate = row.UpdateDate,
                UpdatedBy = row.UpdatedBy,
            });
        }

        Logger.LogInformation(
            "Rebuilt {Table} with external link columns, preserving {Count} row(s).",
            KeywordMappingDto.TableName,
            rows.Count);

        return Task.CompletedTask;
    }

    /// <summary>The mapping table as it was when every destination was a page.</summary>
    [TableName(KeywordMappingDto.TableName)]
    [ExplicitColumns]
    private sealed class PageMappingDto
    {
        [Column("keywordKey")]
        public string KeywordKey { get; set; } = string.Empty;

        [Column("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [Column("culture")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Culture { get; set; }

        [Column("targetKey")]
        public Guid TargetKey { get; set; }

        [Column("updateDate")]
        public DateTime UpdateDate { get; set; }

        [Column("updatedBy")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? UpdatedBy { get; set; }
    }
}
