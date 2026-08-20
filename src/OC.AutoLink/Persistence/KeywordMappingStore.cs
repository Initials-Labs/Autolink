using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace OC.AutoLink.Persistence;

/// <inheritdoc />
public sealed class KeywordMappingStore : IKeywordMappingStore
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<KeywordMappingStore> _logger;

    public KeywordMappingStore(IScopeProvider scopeProvider, ILogger<KeywordMappingStore> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<KeywordMapping> GetAll()
    {
        try
        {
            using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

            Sql<ISqlContext> sql = scope.SqlContext.Sql()
                .Select<KeywordMappingDto>()
                .From<KeywordMappingDto>();

            return scope.Database.Fetch<KeywordMappingDto>(sql)
                .Select(dto => new KeywordMapping(
                    dto.Keyword,
                    dto.TargetKey,
                    dto.ExternalUrl,
                    dto.Label,
                    dto.Nofollow,
                    dto.UpdateDate,
                    dto.UpdatedBy,
                    dto.Culture ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            // Most likely the migration has not run yet. Degrade to automatic resolution rather than taking
            // the whole registry down with us — tag-based linking is still useful without the mappings.
            _logger.LogError(ex, "Could not read auto-link keyword mappings. Falling back to automatic resolution.");
            return [];
        }
    }

    /// <inheritdoc />
    public void Save(string keyword, KeywordDestination destination, string? updatedBy, string culture)
    {
        culture = DecisionKey.Normalise(culture);

        string trimmed = keyword.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A mapping needs a keyword.", nameof(keyword));
        }

        string key = DecisionKey.Normalise(trimmed);

        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Select<KeywordMappingDto>()
            .From<KeywordMappingDto>()
            .Where<KeywordMappingDto>(x => x.KeywordKey == key && x.Culture == culture);

        KeywordMappingDto? existing = scope.Database.FirstOrDefault<KeywordMappingDto>(sql);

        if (existing is null)
        {
            scope.Database.Insert(Apply(new KeywordMappingDto
            {
                KeywordKey = key,
                Keyword = trimmed,
                Culture = culture,
            }, destination, updatedBy));

            return;
        }

        existing.Keyword = trimmed;
        scope.Database.Update(Apply(existing, destination, updatedBy));
    }

    /// <inheritdoc />
    public bool Delete(string keyword, string culture)
    {
        string key = DecisionKey.Normalise(keyword);
        culture = DecisionKey.Normalise(culture);

        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> sql = scope.SqlContext.Sql()
            .Delete<KeywordMappingDto>()
            .Where<KeywordMappingDto>(x => x.KeywordKey == key && x.Culture == culture);

        return scope.Database.Execute(sql) > 0;
    }

    /// <summary>
    /// Writes a destination onto a row, clearing whichever half does not apply so a row can never claim both a page
    /// and a URL.
    /// </summary>
    private static KeywordMappingDto Apply(KeywordMappingDto dto, KeywordDestination destination, string? updatedBy)
    {
        dto.TargetKey = destination.IsExternal ? Guid.Empty : destination.TargetKey;
        dto.ExternalUrl = destination.IsExternal ? destination.ExternalUrl : null;
        dto.Label = destination.IsExternal ? destination.Label : null;
        dto.Nofollow = destination.IsExternal ? destination.Nofollow : null;
        dto.UpdateDate = DateTime.UtcNow;
        dto.UpdatedBy = updatedBy;

        return dto;
    }
}
