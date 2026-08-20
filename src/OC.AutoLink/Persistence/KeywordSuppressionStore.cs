using Microsoft.Extensions.Logging;
using OC.AutoLink.Caching;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace OC.AutoLink.Persistence;

/// <summary>
/// A keyword that should not be linked, on one page or on all of them.
/// </summary>
/// <param name="Keyword">The keyword, in the casing it was saved with.</param>
/// <param name="PageKey">Page it applies to, or <see cref="Guid.Empty"/> for every page.</param>
/// <param name="CreateDate">When it was suppressed.</param>
/// <param name="CreatedBy">Who suppressed it.</param>
/// <param name="Culture">Culture it applies to, or empty for every culture.</param>
public sealed record KeywordSuppression(
    string Keyword,
    Guid PageKey,
    DateTime CreateDate,
    string? CreatedBy,
    string Culture)
{
    /// <summary>Applies to every culture.</summary>
    public bool IsAllCultures => Culture.Length == 0;

    /// <summary>Sentinel meaning the suppression applies everywhere.</summary>
    public static readonly Guid Everywhere = Guid.Empty;

    public bool IsGlobal => PageKey == Everywhere;
}

/// <summary>
/// Reads and writes the keyword suppressions.
/// </summary>
public interface IKeywordSuppressionStore
{
    /// <summary>Every suppression. Read once per registry rebuild, not per render.</summary>
    IReadOnlyList<KeywordSuppression> GetAll();

    /// <summary>Suppresses a keyword on one page, or everywhere with <see cref="KeywordSuppression.Everywhere"/>.</summary>
    void Suppress(string keyword, Guid pageKey, string? createdBy, string culture);

    /// <summary>Lifts a suppression. False if there was not one.</summary>
    bool Allow(string keyword, Guid pageKey, string culture);
}

/// <inheritdoc />
public sealed class KeywordSuppressionStore : IKeywordSuppressionStore
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IKeywordRegistryInvalidator _invalidator;
    private readonly ILogger<KeywordSuppressionStore> _logger;

    public KeywordSuppressionStore(
        IScopeProvider scopeProvider,
        IKeywordRegistryInvalidator invalidator,
        ILogger<KeywordSuppressionStore> logger)
    {
        _scopeProvider = scopeProvider;
        _invalidator = invalidator;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<KeywordSuppression> GetAll()
    {
        try
        {
            using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

            Sql<ISqlContext> sql = scope.SqlContext.Sql()
                .Select<KeywordSuppressionDto>()
                .From<KeywordSuppressionDto>();

            return scope.Database.Fetch<KeywordSuppressionDto>(sql)
                .Select(dto => new KeywordSuppression(
                    dto.Keyword, dto.PageKey, dto.CreateDate, dto.CreatedBy, dto.Culture ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            // Same reasoning as the mapping store: a missing table degrades to linking everything rather than
            // taking the registry down. The failure mode is a link that should have been suppressed, which is
            // visible and fixable, rather than a site with no links at all.
            _logger.LogError(ex, "Could not read auto-link suppressions. Nothing will be suppressed.");
            return [];
        }
    }

    /// <inheritdoc />
    public void Suppress(string keyword, Guid pageKey, string? createdBy, string culture)
    {
        culture = DecisionKey.Normalise(culture);

        string trimmed = keyword.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A suppression needs a keyword.", nameof(keyword));
        }

        if (Insert(DecisionKey.Normalise(trimmed), trimmed, pageKey, culture, createdBy))
        {
            // Invalidated here rather than by the caller, for the same reason as the mapping store: this is the
            // code that knows a row appeared, and an invalidation nobody sends leaves the other servers linking
            // a keyword this page just switched off.
            _invalidator.InvalidateEverywhere();
        }
    }

    private bool Insert(string key, string keyword, Guid pageKey, string culture, string? createdBy)
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

        Sql<ISqlContext> existing = scope.SqlContext.Sql()
            .Select<KeywordSuppressionDto>()
            .From<KeywordSuppressionDto>()
            .Where<KeywordSuppressionDto>(x => x.KeywordKey == key && x.PageKey == pageKey && x.Culture == culture);

        if (scope.Database.FirstOrDefault<KeywordSuppressionDto>(existing) is not null)
        {
            // Suppressing twice is not an error, it is the same decision.
            return false;
        }

        scope.Database.Insert(new KeywordSuppressionDto
        {
            KeywordKey = key,
            Keyword = keyword,
            Culture = culture,
            PageKey = pageKey,
            CreateDate = DateTime.UtcNow,
            CreatedBy = createdBy,
        });

        return true;
    }

    /// <inheritdoc />
    public bool Allow(string keyword, Guid pageKey, string culture)
    {
        string key = DecisionKey.Normalise(keyword);
        culture = DecisionKey.Normalise(culture);

        bool removed;

        using (IScope scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            Sql<ISqlContext> sql = scope.SqlContext.Sql()
                .Delete<KeywordSuppressionDto>()
                .Where<KeywordSuppressionDto>(x => x.KeywordKey == key && x.PageKey == pageKey && x.Culture == culture);

            removed = scope.Database.Execute(sql) > 0;
        }

        if (removed)
        {
            _invalidator.InvalidateEverywhere();
        }

        return removed;
    }
}
