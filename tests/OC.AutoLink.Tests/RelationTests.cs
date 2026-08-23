using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OC.AutoLink.Models;
using OC.AutoLink.Relations;
using OC.AutoLink.Scanning;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace OC.AutoLink.Tests;

/// <summary>
/// What the relation audit trail records, and what it deliberately does not.
/// </summary>
/// <remarks>
/// These matter because the relations drive Umbraco's delete warning. A relation recorded for a mention that never
/// became a link makes the warning claim links that are not there, which is worse than no warning at all.
/// </remarks>
public class RelationTests
{
    private static readonly Guid Target = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Mentioning = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const int TargetId = 101;
    private const int OtherId = 102;
    private const int MentioningId = 103;

    [Fact]
    public void A_linked_mention_becomes_one_relation_from_mentioning_page_to_target()
    {
        (IRelationService service, IRelationType type) = Service();

        Reconcile(service, Page(Mentioning, "en-GB", Linked("Umbraco", Target)));

        // Parent is the page doing the mentioning, child is the page it links to — the same way round Umbraco
        // stores a Content Picker reference. Inverted, the delete warning fires on the wrong page entirely.
        service.Received(1).Relate(MentioningId, TargetId, type);
    }

    [Theory]
    [InlineData(AutoLinkSkipReason.SkippedElement)]
    [InlineData(AutoLinkSkipReason.LimitReached)]
    [InlineData(AutoLinkSkipReason.SelfLink)]
    [InlineData(AutoLinkSkipReason.HandLinked)]
    [InlineData(AutoLinkSkipReason.Suppressed)]
    public void A_mention_that_did_not_link_is_not_recorded(string reason)
    {
        (IRelationService service, _) = Service();

        Reconcile(service, Page(Mentioning, "en-GB", Skipped("Umbraco", Target, reason)));

        service.DidNotReceive().Relate(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IRelationType>());
    }

    [Fact]
    public void An_external_link_is_not_recorded_because_there_is_no_node_behind_it()
    {
        (IRelationService service, _) = Service();

        // An external target carries an empty key, which is how the linker represents "not a page".
        Reconcile(service, Page(Mentioning, "en-GB", Linked("Umbraco", Guid.Empty)));

        service.DidNotReceive().Relate(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IRelationType>());
    }

    [Fact]
    public void The_same_pair_in_two_cultures_is_one_relation()
    {
        // umbracoRelation has no culture column, so a page linking to another in both languages is one row, not two.
        (IRelationService service, IRelationType type) = Service();

        Reconcile(
            service,
            Page(Mentioning, "en-GB", Linked("Umbraco", Target)),
            Page(Mentioning, "en-US", Linked("Umbraco", Target)));

        service.Received(1).Relate(MentioningId, TargetId, type);
    }

    [Fact]
    public void A_relation_the_scan_no_longer_reports_is_removed()
    {
        IRelation stale = Relation(MentioningId, OtherId);
        (IRelationService service, IRelationType type) = Service(stale);

        AutoLinkRelationChanges changes = Reconcile(
            service, Page(Mentioning, "en-GB", Linked("Umbraco", Target)));

        service.Received(1).Delete(stale);
        service.Received(1).Relate(MentioningId, TargetId, type);
        Assert.Equal(new AutoLinkRelationChanges(1, 1), changes);
    }

    [Fact]
    public void A_relation_that_still_holds_is_left_alone()
    {
        (IRelationService service, _) = Service(Relation(MentioningId, TargetId));

        AutoLinkRelationChanges changes = Reconcile(
            service, Page(Mentioning, "en-GB", Linked("Umbraco", Target)));

        service.DidNotReceive().Relate(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IRelationType>());
        service.DidNotReceive().Delete(Arg.Any<IRelation>());
        Assert.False(changes.Any);
    }

    [Fact]
    public void A_missing_relation_type_is_reported_rather_than_thrown()
    {
        // The migration has not run. A scan is still a valid answer; it just is not being recorded.
        var service = Substitute.For<IRelationService>();
        service.GetRelationTypeByAlias(AutoLinkRelation.Alias).Returns((IRelationType?)null);

        AutoLinkRelationChanges changes = Reconcile(
            service, Page(Mentioning, "en-GB", Linked("Umbraco", Target)));

        Assert.False(changes.Any);
        service.DidNotReceive().Relate(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IRelationType>());
    }

    [Fact]
    public void Deleting_a_page_removes_its_relations_in_both_directions()
    {
        // Both directions on purpose. The deleted page might be the target other pages linked to, or one of the
        // pages doing the linking, and either way the rows naming it are now about a node that does not exist.
        IRelation asTarget = Relation(MentioningId, TargetId);
        IRelation asMentioner = Relation(TargetId, OtherId);

        var service = Substitute.For<IRelationService>();
        service.GetByParentOrChildId(TargetId, AutoLinkRelation.Alias).Returns([asTarget, asMentioner]);

        var writer = new AutoLinkRelationWriter(service, IdMap(), NullLogger<AutoLinkRelationWriter>.Instance);

        Assert.Equal(2, writer.RemoveFor(TargetId));
        service.Received(1).Delete(asTarget);
        service.Received(1).Delete(asMentioner);
    }

    [Fact]
    public void The_pages_linking_to_one_are_the_parents_of_its_relations()
    {
        // Read the way round they are written: this page is the child, the pages mentioning it are the parents.
        IRelation inbound = Relation(MentioningId, TargetId);
        IRelation outbound = Relation(TargetId, OtherId);

        var service = Substitute.For<IRelationService>();
        service.GetByParentOrChildId(TargetId, AutoLinkRelation.Alias).Returns([inbound, outbound]);

        var writer = new AutoLinkRelationWriter(service, IdMap(), NullLogger<AutoLinkRelationWriter>.Instance);

        Assert.Equal([MentioningId], writer.MentioningPages(Target));
    }

    private static AutoLinkRelationChanges Reconcile(IRelationService service, params ScannedPage[] pages)
    {
        var writer = new AutoLinkRelationWriter(service, IdMap(), NullLogger<AutoLinkRelationWriter>.Instance);

        return writer.Reconcile(new AutoLinkScanReport("test", pages.Length, 0, pages, []));
    }

    private static (IRelationService Service, IRelationType Type) Service(params IRelation[] existing)
    {
        var type = new RelationType(
            AutoLinkRelation.Name,
            AutoLinkRelation.Alias,
            isBidrectional: false,
            parentObjectType: Constants.ObjectTypes.Document,
            childObjectType: Constants.ObjectTypes.Document,
            isDependency: true,
            key: null)
        {
            Id = 7,
        };

        var service = Substitute.For<IRelationService>();
        service.GetRelationTypeByAlias(AutoLinkRelation.Alias).Returns(type);
        service.GetAllRelationsByRelationType(type.Id).Returns(existing);

        return (service, type);
    }

    private static IIdKeyMap IdMap()
    {
        var map = Substitute.For<IIdKeyMap>();
        map.GetIdForKey(Target, UmbracoObjectTypes.Document).Returns(Attempt.Succeed(TargetId));
        map.GetIdForKey(Other, UmbracoObjectTypes.Document).Returns(Attempt.Succeed(OtherId));
        map.GetIdForKey(Mentioning, UmbracoObjectTypes.Document).Returns(Attempt.Succeed(MentioningId));
        map.GetIdForKey(Guid.Empty, UmbracoObjectTypes.Document).Returns(Attempt.Fail<int>());

        return map;
    }

    private static IRelation Relation(int parentId, int childId)
    {
        var relation = Substitute.For<IRelation>();
        relation.ParentId.Returns(parentId);
        relation.ChildId.Returns(childId);

        return relation;
    }

    private static ScannedPage Page(Guid key, string culture, params AutoLinkPlacement[] placements) =>
        new(key, "Page", "/page/", culture, placements);

    private static AutoLinkPlacement Linked(string keyword, Guid targetKey) =>
        new(keyword, keyword, targetKey, "Target", "/target/", null, null, null);

    private static AutoLinkPlacement Skipped(string keyword, Guid targetKey, string reason) =>
        new(keyword, keyword, targetKey, "Target", "/target/", null, null, reason);
}
