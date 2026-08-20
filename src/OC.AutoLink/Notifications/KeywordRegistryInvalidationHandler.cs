using OC.AutoLink.Registry;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace OC.AutoLink.Notifications;

/// <summary>
/// Marks the keyword registry stale whenever content changes.
/// </summary>
/// <remarks>
/// Invalidation is global rather than per target, and deliberately so: any page's output depends on the whole
/// keyword set, and there is no way to know which pages mention which keywords without rendering them. The
/// cost of being coarse is paid back by the registry itself, which only moves its stamp when the rebuilt
/// keyword set actually hashes differently.
/// </remarks>
public sealed class KeywordRegistryInvalidationHandler :
    INotificationHandler<ContentPublishedNotification>,
    INotificationHandler<ContentUnpublishedNotification>,
    INotificationHandler<ContentDeletedNotification>,
    INotificationHandler<ContentMovedNotification>,
    INotificationHandler<ContentMovedToRecycleBinNotification>
{
    private readonly IKeywordRegistry _registry;

    public KeywordRegistryInvalidationHandler(IKeywordRegistry registry) => _registry = registry;

    public void Handle(ContentPublishedNotification notification) => _registry.Invalidate();

    public void Handle(ContentUnpublishedNotification notification) => _registry.Invalidate();

    public void Handle(ContentDeletedNotification notification) => _registry.Invalidate();

    public void Handle(ContentMovedNotification notification) => _registry.Invalidate();

    public void Handle(ContentMovedToRecycleBinNotification notification) => _registry.Invalidate();
}
