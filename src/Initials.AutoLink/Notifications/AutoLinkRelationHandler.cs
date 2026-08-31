using Microsoft.Extensions.Logging;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Relations;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Initials.AutoLink.Notifications;

/// <summary>
/// Warns before a page other pages auto-link to is deleted, and clears the relations once it is.
/// </summary>
/// <remarks>
/// The warning an editor actually sees comes from Umbraco, not from here: the relation type is registered as a
/// dependency, so the delete confirmation lists the pages that reference this one without the package doing
/// anything. This handler covers the case that has no relation behind it — a keyword pointing at a page that
/// nothing currently mentions. There is no rendered link to break yet, so no dependency is recorded, but the
/// keyword still stops resolving the moment the page goes, and saying so beats letting somebody find out from the
/// dashboard afterwards.
/// <para>
/// It warns rather than cancels. Whether a page should exist is not a decision this package gets to make; it only
/// knows a consequence worth mentioning first.
/// </para>
/// </remarks>
internal sealed class AutoLinkRelationHandler :
    INotificationHandler<ContentMovingToRecycleBinNotification>,
    INotificationHandler<ContentDeletingNotification>,
    INotificationHandler<ContentDeletedNotification>
{
    private const string MessageCategory = "Auto-linking";

    private readonly IKeywordMappingStore _mappings;
    private readonly IAutoLinkRelationWriter _relations;
    private readonly ILogger<AutoLinkRelationHandler> _logger;

    public AutoLinkRelationHandler(
        IKeywordMappingStore mappings,
        IAutoLinkRelationWriter relations,
        ILogger<AutoLinkRelationHandler> logger)
    {
        _mappings = mappings;
        _relations = relations;
        _logger = logger;
    }

    /// <summary>The backoffice delete button, which moves to the recycle bin rather than deleting outright.</summary>
    public void Handle(ContentMovingToRecycleBinNotification notification) =>
        Warn(notification.MoveInfoCollection.Select(move => move.Entity), notification.Messages, permanent: false);

    /// <summary>Deleting for good, including emptying the recycle bin.</summary>
    public void Handle(ContentDeletingNotification notification) =>
        Warn(notification.DeletedEntities, notification.Messages, permanent: true);

    /// <summary>
    /// The page is gone, so the relations naming it are meaningless.
    /// </summary>
    /// <remarks>
    /// In practice this finds nothing, and that is the expected outcome rather than a bug. Umbraco clears a node's
    /// relations as part of deleting it — before this notification fires — so by the time we look, ours have gone
    /// with the rest. Verified on 17.6.1 against SQLite: the foreign keys on <c>umbracoRelation</c> carry no
    /// <c>ON DELETE CASCADE</c>, so it is the delete itself doing the work, not the database.
    /// <para>
    /// Kept as a backstop, not as the mechanism. It costs one indexed query on an operation that already touches
    /// half the content tables, and it means a row surviving by some route we have not thought of — a relation
    /// written for a node removed outside the service layer, or a change to that cleanup in a later version — does
    /// not linger as a phantom dependency on whatever takes that id next.
    /// </para>
    /// </remarks>
    public void Handle(ContentDeletedNotification notification)
    {
        foreach (IContent content in notification.DeletedEntities)
        {
            try
            {
                _relations.RemoveFor(content.Id);
            }
            catch (Exception ex)
            {
                // A failed cleanup must not fail the delete. The next reconcile drops the relation anyway, because
                // the scan will stop reporting a page that no longer exists.
                _logger.LogError(
                    ex,
                    "Could not remove the auto-link relations for deleted page {PageId}. The next scan will clear them.",
                    content.Id);
            }
        }
    }

    private void Warn(IEnumerable<IContent> entities, EventMessages messages, bool permanent)
    {
        IReadOnlyList<KeywordMapping> all;

        try
        {
            all = _mappings.GetAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the auto-link keywords, so no delete warning was raised.");
            return;
        }

        foreach (IContent content in entities)
        {
            List<string> keywords = all
                .Where(mapping => !mapping.IsExternal && mapping.TargetKey == content.Key)
                .Select(mapping => mapping.Keyword)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (keywords.Count == 0)
            {
                continue;
            }

            int mentioning = _relations.MentioningPages(content.Key).Count;

            string message = Describe(content.Name ?? "This page", keywords, mentioning, permanent);

            messages.Add(new EventMessage(MessageCategory, message, EventMessageType.Warning));

            _logger.LogWarning(
                "{Name} is the destination of {Count} auto-link keyword(s) ({Keywords}) and is being {Action}. {Mentioning} page(s) currently link to it.",
                content.Name,
                keywords.Count,
                string.Join(", ", keywords),
                permanent ? "deleted" : "moved to the recycle bin",
                mentioning);
        }
    }

    /// <summary>
    /// The sentence an editor reads. Names the keywords, because "this page is referenced" is not actionable and
    /// "Claude AI stops linking" is.
    /// </summary>
    private static string Describe(string name, IReadOnlyList<string> keywords, int mentioning, bool permanent)
    {
        string list = keywords.Count <= 3
            ? string.Join(", ", keywords.Select(keyword => $"“{keyword}”"))
            : $"“{keywords[0]}”, “{keywords[1]}” and {keywords.Count - 2} more";

        string what = keywords.Count == 1 ? "keyword" : "keywords";

        string consequence = mentioning switch
        {
            0 => "No page links to it yet, so nothing on the site changes today.",
            1 => "One page currently links to it, and that link will stop appearing.",
            _ => $"{mentioning} pages currently link to it, and those links will stop appearing.",
        };

        string fate = permanent
            ? "The keyword will be left pointing at nothing"
            : "The keyword will be left pointing at nothing while the page is in the recycle bin";

        return $"{name} is where the auto-link {what} {list} point. {consequence} "
               + $"{fate}, and the Auto-linking screen will flag it so you can send it somewhere else.";
    }
}
