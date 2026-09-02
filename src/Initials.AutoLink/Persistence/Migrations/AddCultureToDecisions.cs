using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// Adds the culture column to both decision tables, preserving the rows already in them.
/// </summary>
internal sealed class AddCultureToDecisions : AsyncMigrationBase
{
    public AddCultureToDecisions(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        RebuildMappings();
        RebuildSuppressions();

        return Task.CompletedTask;
    }

    private void RebuildMappings()
    {
        if (!TableExists(KeywordMappingDto.TableName) || ColumnExists(KeywordMappingDto.TableName, "culture"))
        {
            return;
        }

        List<LegacyMappingDto> rows = Database.Fetch<LegacyMappingDto>(
            $"SELECT keywordKey, keyword, targetKey, updateDate, updatedBy FROM {KeywordMappingDto.TableName}");

        Delete.Table(KeywordMappingDto.TableName).Do();
        Create.Table<KeywordMappingDto>().Do();

        foreach (LegacyMappingDto row in rows)
        {
            Database.Insert(new KeywordMappingDto
            {
                KeywordKey = row.KeywordKey,
                Keyword = row.Keyword,
                Culture = string.Empty,
                TargetKey = row.TargetKey,
                UpdateDate = row.UpdateDate,
                UpdatedBy = row.UpdatedBy,
            });
        }

        Logger.LogInformation(
            "Rebuilt {Table} with culture, preserving {Count} row(s).",
            KeywordMappingDto.TableName,
            rows.Count);
    }

    private void RebuildSuppressions()
    {
        if (!TableExists(KeywordSuppressionDto.TableName) || ColumnExists(KeywordSuppressionDto.TableName, "culture"))
        {
            return;
        }

        List<LegacySuppressionDto> rows = Database.Fetch<LegacySuppressionDto>(
            $"SELECT keywordKey, keyword, pageKey, createDate, createdBy FROM {KeywordSuppressionDto.TableName}");

        Delete.Table(KeywordSuppressionDto.TableName).Do();
        Create.Table<KeywordSuppressionDto>().Do();

        foreach (LegacySuppressionDto row in rows)
        {
            Database.Insert(new KeywordSuppressionDto
            {
                KeywordKey = row.KeywordKey,
                Keyword = row.Keyword,
                Culture = string.Empty,
                PageKey = row.PageKey,
                CreateDate = row.CreateDate,
                CreatedBy = row.CreatedBy,
            });
        }

        Logger.LogInformation(
            "Rebuilt {Table} with culture, preserving {Count} row(s).",
            KeywordSuppressionDto.TableName,
            rows.Count);
    }

    /// <summary>The mapping table as it was before culture, for reading the rows back out.</summary>
    [TableName(KeywordMappingDto.TableName)]
    [ExplicitColumns]
    private sealed class LegacyMappingDto
    {
        [Column("keywordKey")]
        public string KeywordKey { get; set; } = string.Empty;

        [Column("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [Column("targetKey")]
        public Guid TargetKey { get; set; }

        [Column("updateDate")]
        public DateTime UpdateDate { get; set; }

        [Column("updatedBy")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? UpdatedBy { get; set; }
    }

    /// <summary>The suppression table as it was before culture.</summary>
    [TableName(KeywordSuppressionDto.TableName)]
    [ExplicitColumns]
    private sealed class LegacySuppressionDto
    {
        [Column("keywordKey")]
        public string KeywordKey { get; set; } = string.Empty;

        [Column("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [Column("pageKey")]
        public Guid PageKey { get; set; }

        [Column("createDate")]
        public DateTime CreateDate { get; set; }

        [Column("createdBy")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? CreatedBy { get; set; }
    }
}
