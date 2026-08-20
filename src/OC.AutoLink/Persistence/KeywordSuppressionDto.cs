using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace OC.AutoLink.Persistence;

/// <summary>
/// A decision that a keyword should not be linked, either on one page or anywhere.
/// </summary>
/// <remarks>
/// <c>pageKey</c> uses <see cref="Guid.Empty"/> rather than null for "everywhere". A nullable column in a unique
/// index behaves differently across providers — SQL Server treats two nulls as equal, SQLite treats them as
/// distinct — so the sentinel keeps the index meaning the same on both.
/// </remarks>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class KeywordSuppressionDto
{
    public const string TableName = "ocAutoLinkSuppression";
    public const string IndexName = "IX_" + TableName + "_keywordKey_pageKey_culture";
    public const string LegacyIndexName = "IX_" + TableName + "_keywordKey_pageKey";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("keywordKey")]
    [Length(255)]
    [Index(IndexTypes.UniqueNonClustered, Name = IndexName, ForColumns = "keywordKey,pageKey,culture")]
    public string KeywordKey { get; set; } = string.Empty;

    /// <summary>Culture the suppression applies to, or empty for every culture.</summary>
    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    [Column("keyword")]
    [Length(255)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Page the suppression applies to, or <see cref="Guid.Empty"/> for every page.</summary>
    [Column("pageKey")]
    public Guid PageKey { get; set; }

    [Column("createDate")]
    public DateTime CreateDate { get; set; }

    [Column("createdBy")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? CreatedBy { get; set; }
}
