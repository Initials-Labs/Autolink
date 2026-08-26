using OC.AutoLink.Registry;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace OC.AutoLink.Notifications;

/// <summary>
/// Marks the keyword registry stale whenever the published content cache changes.
/// </summary>
/// <remarks>
/// Hooked to the cache refresher rather than to ContentPublished, and for two reasons that both bite in production.
/// <para>
/// ContentPublished fires <em>inside</em> the publish, before the published cache has settled, so a render happening
/// at that moment could rebuild from stale content and then mark itself clean — staying stale until the next content
/// change. The refresher notification fires after the cache is actually updated.
/// </para>
/// <para>
/// It also only fired on the server that did the publishing. Other nodes learn about content changes through the
/// distributed cache, which runs their refreshers, which raises this notification there too. So one hook replaces
/// five and covers every server.
/// </para>
/// Invalidation stays deliberately global: any page's output depends on the whole keyword set, and the registry only
/// moves its stamp when a rebuild actually hashes differently.
/// </remarks>
internal sealed class KeywordRegistryInvalidationHandler : INotificationHandler<ContentCacheRefresherNotification>
{
    private readonly IKeywordRegistry _registry;

    public KeywordRegistryInvalidationHandler(IKeywordRegistry registry) => _registry = registry;

    public void Handle(ContentCacheRefresherNotification notification) => _registry.Invalidate();
}
