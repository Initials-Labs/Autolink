using Initials.AutoLink.Registry;
using Umbraco.Cms.Core.Cache;

namespace Initials.AutoLink.Caching;

/// <summary>
/// Carries "the keyword decisions changed" to every server.
/// </summary>
internal sealed class AutoLinkCacheRefresher : ICacheRefresher
{
    /// <summary>Stable identity for this refresher. Must not change once deployed.</summary>
    public static readonly Guid RefresherId = new("3f1c7a52-9e64-4c1f-9c2c-6b1a0f7d5e10");

    private readonly IKeywordRegistry _registry;

    public AutoLinkCacheRefresher(IKeywordRegistry registry) => _registry = registry;

    public Guid RefresherUniqueId => RefresherId;

    public string Name => "Auto-link keyword registry";

    /// <summary>
    /// The registry is invalidated wholesale. Every page's output depends on the whole keyword set, so there is no
    /// smaller unit to invalidate, and the content hash means an identical rebuild costs nothing downstream.
    /// </summary>
    public void RefreshAll() => _registry.Invalidate();

    public void Refresh(int id) => RefreshAll();

    public void Refresh(Guid id) => RefreshAll();

    public void Remove(int id) => RefreshAll();
}
