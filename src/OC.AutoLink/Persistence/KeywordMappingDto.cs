using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace OC.AutoLink.Persistence;

/// <summary>
/// A keyword and its destination. There is no other source of keywords.
/// </summary>
/// <remarks>
/// The keyword is stored twice on purpose. <c>keywordKey</c> is lower-cased and carries the unique index, so
/// uniqueness behaves the same on SQLite (case-sensitive text by default) as on SQL Server (usually not).
/// <c>keyword</c> keeps the casing somebody typed, purely so the backoffice can show it back to them.
/// </remarks>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class KeywordMappingDto
{
    public const string TableName = "ocAutoLinkKeywordMapping";
    public const string IndexName = "IX_" + TableName + "_keywordKey_culture";
    public const string LegacyIndexName = "IX_" + TableName + "_keywordKey";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("keywordKey")]
    [Length(255)]
    [Index(IndexTypes.UniqueNonClustered, Name = IndexName, ForColumns = "keywordKey,culture")]
    public string KeywordKey { get; set; } = string.Empty;

    /// <summary>
    /// Culture the keyword applies to, or empty for every culture. The same word can point somewhere different in
    /// each language, so the culture is part of the row's identity.
    /// </summary>
    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    [Column("keyword")]
    [Length(255)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// The target's key rather than its URL. A mapped page that later moves or gets renamed still resolves,
    /// and the move shows up in the registry stamp instead of quietly rotting. Empty for an external link.
    /// </summary>
    [Column("targetKey")]
    public Guid TargetKey { get; set; }

    /// <summary>
    /// An absolute URL outside the site, when the destination is not a page. Exactly one of this and
    /// <see cref="TargetKey"/> is meaningful: a destination is a destination either way, so it lives in one table
    /// with one precedence rule rather than a parallel one with its own.
    /// </summary>
    [Column("externalUrl")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? ExternalUrl { get; set; }

    /// <summary>Label for an external link, used as the anchor title. Defaults to the host.</summary>
    [Column("label")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Label { get; set; }

    /// <summary>Overrides the configured rel default for this link. Null follows the configuration.</summary>
    [Column("nofollow")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public bool? Nofollow { get; set; }

    [Column("updateDate")]
    public DateTime UpdateDate { get; set; }

    [Column("updatedBy")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? UpdatedBy { get; set; }
}
