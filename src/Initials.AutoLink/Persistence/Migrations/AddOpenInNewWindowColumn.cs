using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Initials.AutoLink.Persistence.Migrations;

/// <summary>
/// Adds the open-in-new-window column to the mapping table, preserving the rows already in it.
/// </summary>
internal sealed class AddOpenInNewWindowColumn : AsyncMigrationBase
{
    public AddOpenInNewWindowColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (!TableExists(KeywordMappingDto.TableName)
            || ColumnExists(KeywordMappingDto.TableName, "openInNewWindow"))
        {
            return Task.CompletedTask;
        }

        List<PreviousMappingDto> rows = Database.Fetch<PreviousMappingDto>(
            $"SELECT keywordKey, keyword, culture, targetKey, externalUrl, label, nofollow, updateDate, updatedBy FROM {KeywordMappingDto.TableName}");

        Delete.Table(KeywordMappingDto.TableName).Do();
        Create.Table<KeywordMappingDto>().Do();

        foreach (PreviousMappingDto row in rows)
        {
            Database.Insert(new KeywordMappingDto
            {
                KeywordKey = row.KeywordKey,
                Keyword = row.Keyword,
                Culture = row.Culture ?? string.Empty,
                TargetKey = row.TargetKey,
                ExternalUrl = row.ExternalUrl,
                Label = row.Label,
                Nofollow = row.Nofollow,
                UpdateDate = row.UpdateDate,
                UpdatedBy = row.UpdatedBy,
            });
        }

        Logger.LogInformation(
            "Rebuilt {Table} with the open-in-new-window column, preserving {Count} row(s).",
            KeywordMappingDto.TableName,
            rows.Count);

        return Task.CompletedTask;
    }

    /// <summary>The mapping table as it was before links could open in a new window.</summary>
    [TableName(KeywordMappingDto.TableName)]
    [ExplicitColumns]
    private sealed class PreviousMappingDto
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

        [Column("externalUrl")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? ExternalUrl { get; set; }

        [Column("label")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Label { get; set; }

        [Column("nofollow")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public bool? Nofollow { get; set; }

        [Column("updateDate")]
        public DateTime UpdateDate { get; set; }

        [Column("updatedBy")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? UpdatedBy { get; set; }
    }
}
