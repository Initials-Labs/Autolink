using Umbraco.Cms.Core.Cache;

namespace OC.AutoLink.Caching;

/// <summary>
/// Marks the keyword registry stale on every server, not just this one.
/// </summary>
public interface IKeywordRegistryInvalidator
{
    void InvalidateEverywhere();
}

/// <inheritdoc />
public sealed class KeywordRegistryInvalidator : IKeywordRegistryInvalidator
{
    private readonly DistributedCache _distributedCache;

    public KeywordRegistryInvalidator(DistributedCache distributedCache) => _distributedCache = distributedCache;

    /// <inheritdoc />
    public void InvalidateEverywhere() => _distributedCache.RefreshAll(AutoLinkCacheRefresher.RefresherId);
}
