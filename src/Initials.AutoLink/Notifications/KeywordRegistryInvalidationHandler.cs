using Initials.AutoLink.Registry;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Initials.AutoLink.Notifications;

/// <summary>
/// Marks the keyword registry stale whenever the published content cache changes.
/// </summary>
internal sealed class KeywordRegistryInvalidationHandler : INotificationHandler<ContentCacheRefresherNotification>
{
    private readonly IKeywordRegistry _registry;

    public KeywordRegistryInvalidationHandler(IKeywordRegistry registry) => _registry = registry;

    public void Handle(ContentCacheRefresherNotification notification) => _registry.Invalidate();
}
