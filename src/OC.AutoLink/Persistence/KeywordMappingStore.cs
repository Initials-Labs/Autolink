using Microsoft.Extensions.Logging;
using OC.AutoLink.Caching;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace OC.AutoLink.Persistence;

/// <inheritdoc />
internal sealed class KeywordMappingStore : IKeywordMappingStore
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IKeywordRegistryInvalidator _invalidator;
    private readonly ILogger<KeywordMappingStore> _logger;

    public KeywordMappingStore(
        IScopeProvider scopeProvider,
        IKeywordRegistryInvalidator invalidator,
        ILogger<KeywordMappingStore> logger)
    {
        _scopeProvider = scopeProvider;
        _invalidator = invalidator;
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
            // Most likely the migration has not run yet. Render unlinked rather than taking the whole registry
            // down with us: no keywords is a site that behaves as though the package were not installed, which is
            // survivable in a way that a failed request is not.
            _logger.LogError(ex, "Could not read the auto-link keywords. Nothing will be auto-linked.");
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
            throw new ArgumentException("A keyword is needed.", nameof(keyword));
        }

        Write(DecisionKey.Normalise(trimmed), trimmed, culture, destination, updatedBy);

        // Invalidated here rather than by the caller. This is the code that knows the rows changed, and an
        // invalidation nobody sends leaves every other server resolving the keyword the old way until the next
        // content change. The stamp is a content hash, so re-saving the same destination costs a rebuild and
        // nothing else.
        _invalidator.InvalidateEverywhere();
    }

    private void Write(string key, string keyword, string culture, KeywordDestination destination, string? updatedBy)
    {
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
                Keyword = keyword,
                Culture = culture,
            }, destination, updatedBy));

            return;
        }

        existing.Keyword = keyword;
        scope.Database.Update(Apply(existing, destination, updatedBy));
    }

    /// <inheritdoc />
    public bool Delete(string keyword, string culture)
    {
        string key = DecisionKey.Normalise(keyword);
        culture = DecisionKey.Normalise(culture);

        bool removed;

        using (IScope scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            Sql<ISqlContext> sql = scope.SqlContext.Sql()
                .Delete<KeywordMappingDto>()
                .Where<KeywordMappingDto>(x => x.KeywordKey == key && x.Culture == culture);

            removed = scope.Database.Execute(sql) > 0;
        }

        if (removed)
        {
            // Invalidated here rather than by the caller. This is the code that knows the rows changed, and an
            // invalidation nobody sends leaves every other server resolving the keyword the old way until the next
            // content change. The stamp is a content hash, so re-saving the same decision costs a rebuild and nothing
            // else.
            _invalidator.InvalidateEverywhere();
        }

        return removed;
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
