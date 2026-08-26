# Getting the auto-linker ready for real use

**Part three of three: the collision nobody was told about, moving the keywords off the document types, and letting Umbraco do the warning**

[Part one](part-1-why-not-examine.md) was the idea, [part two](part-2-what-fought-back.md) was the parts of Umbraco that fought back. This post is everything between "it works on my machine" and "somebody else could install this", which turned out to be most of the work.

The demo was fine. Links appeared in old posts and it did what I wanted. But a demo only has to survive me, and I know what it does and what not to type.

Nearly everything I fixed from here was the same problem in a different shape. There are moments where the code was quietly deciding something a person should decide, and not mentioning it.

## Two pages wanting the same keyword

I tagged two pages with `content editor` to see what happened.

One of them won. The other was dropped, silently, and the winner was whichever one the tags query happened to return first. Nothing anywhere recorded that a second page had ever wanted that keyword, so if the wrong page won you would never find out.

My first instinct was a tie breaker. Nearest page in the tree wins, or a priority number on the document type. Both are wrong, and it took me a while to see why.

Which page a phrase should point at is an editorial decision. It depends what you meant by it. A rule that picks one and moves on doesn't answer that, it just stops the code looking indecisive while making the call on your behalf.

So it doesn't guess. If two pages claim a keyword and nobody has said which, that keyword doesn't link at all, and it turns up on a screen asking somebody to choose. That felt wrong for a day or so and then it didn't, because a confidently wrong link is worse than no link. No link is a missed opportunity. A wrong link sends your reader somewhere you didn't mean.

There's a smaller version of the same bug hiding underneath it. When I dropped `content editor` for being contested, the shorter keyword `editor` matched the same words and linked them to a third page that was never in the running. Declining to choose has to mean leaving the phrase alone, not passing it to the next bidder, so contested keywords still hold their span even though they resolve to nothing.

## Working out which pages actually have links

Because the links are worked out as each page is served, nothing is written down. Which is the point, until somebody asks which pages have links on them.

There were three ways to answer it.

I could record every link as it's served. That tells you what visitors actually saw, but only for pages somebody has visited.

I could use Examine to find pages mentioning each keyword. Fast, but it knows none of the rules. It would list pages where the keyword sits in a heading, or where you'd already linked it by hand, and call them links.

Or I could run the linker again and ask it.

I went with the third. `AutoLinkScanner` walks the published content and calls the same method the renderer calls, with a collector attached instead of a mutation, so it reports what it would have done without changing anything.

That last bit matters more than it sounds. The report is the thing an editor clicks "turn this off" on. If the report came from different code to the renderer, it could offer to suppress a link that was never there. Same code path means it can't disagree.

Two things I fixed while I was in there.

The first version only reported the links it made, so a page could mention a keyword three times, appear once, and there was no explanation for the other two. Now every mention is accounted for with a reason: this page is the one being linked to, somebody already linked it by hand, it's inside a heading, or it's the second mention and only the first gets linked.

Whole pages were missing too. A page with no routable URL in a language, usually an unpublished variant, just wasn't in the report at all. Now it's listed with the reason. "Why isn't my page in this list" used to be unanswerable, which is a bad place for a report to be.

## Moving the keywords off the document types

This is where the package changed shape.

Keywords lived in a Tags property on every document type that could be linked to. That's a reasonable starting point and it's where I began. It holds up until you want any of these:

- a plural, or a synonym
- a keyword whose best target isn't tagged and shouldn't be
- a keyword pointing at something that isn't a page at all, like an external site

Every one of those wanted a row in a table rather than a tag on a page. Which meant the table was already the real source of truth, and the property was a second way of saying the same thing that could disagree with the first.

So the property is gone. Keywords live on one screen in a custom Auto-linking section, and the destination uses Umbraco's Multi URL Picker, capped at one item. Picking a page and typing an external address are the same action in the same control, rather than two features with their own rules.

The part I liked most is what fell out of it. One keyword, one culture, one row, with a unique index across the two. Two pages can no longer both claim the same keyword, so the collision problem from earlier in this post didn't get solved, it stopped being possible to create.

That let me delete the candidate tracking, the conflict reporting, the choosing screen, the contested skip reason and the tag group configuration. A good day's work, most of which was removing things.

There's a real trade in it. Before, tagging a page made it a target in the same save. Now it's two steps, publish the page then add the keyword. What you get back is one place to look and no way for it to contradict itself.

There's also one thing I deliberately left on the document type. `excludeFromAutoLinking` is a True/false property that stops a page's rich text being scanned, and that genuinely is a property of a page rather than of any keyword, so it stayed.

## Letting Umbraco do the warning

The last one is the flip side of the good behaviour from part one.

Links being worked out fresh means deleting a page doesn't leave broken links behind. It also means deleting a page silently stops every link pointing at it, and nobody is told.

I could have built a warning screen. I didn't, because Umbraco already has one.

Relations are just a record that two things are connected, and Umbraco uses them for the "this item is used in these places" warning you get when you delete something other content depends on. So the package writes a relation between each mentioning page and the page it links to, with the relation type flagged as a dependency, and reconciles them from the scan.

That's all it takes. Try to trash a page that four others link to and Umbraco's own confirmation dialog says "The following items depend on this" and lists them. The Info tab grows a "Referenced by" panel with the same list. There's no screen of mine involved anywhere.

Deleting the page clears the relations too, and Umbraco does that part itself as well, during the delete.

## A couple of gotchas

Two things caught me out here, both worth flagging.

The first is the direction of a relation, and I had it backwards. Umbraco stores a reference with the referencing item as the parent, so a Content Picker on page A pointing at page B is stored as parent A, child B, and "what uses this item" is a lookup on the child. I'd stored the target as the parent, which reads as the exact opposite.

Everything looked fine. The right number of rows with the right pages in them. But the warning fired on the mentioning pages instead of the target, and the page that four things pointed at showed "This item has no references". I only found it by going and looking at how Umbraco's own relation rows were stored and comparing. When you're plugging into somebody else's convention, go and read what they actually do rather than reasoning about what seems sensible, because two people can both pick the obvious direction and pick differently.

The second was the cleanup running too late. My handler for the delete notification looked the node up by its GUID key to find its relations. That notification fires after the node has gone, so the lookup has nothing left to resolve and the cleanup would have quietly done nothing. The notification hands you the entity, which still has its integer id on it, so use that. It turns out Umbraco clears the relations itself anyway, so the handler is now a backstop that normally finds nothing, but it would have been a silent no-op sat there looking like it worked.

## The bits nobody sees

A few smaller things that came under the heading of ready for live rather than working.

Uninstalling. Removing a NuGet package removes the assembly and leaves the tables, and Umbraco has no uninstall hook to do anything about it. So there's an explicit teardown endpoint that drops the tables, removes the relation type and resets the migration state, because dropping the tables while leaving the plan recorded as complete means a reinstall never recreates them.

Invalidation. This hooks the cache refresher rather than `ContentPublishedNotification`, which fires inside the publish before the published cache has settled, and only on the server that did the publishing. The cache refresher fires afterwards and on every server, so one hook replaced five and fixed multi server at the same time.

And the keyword registry rebuilds on a content hash rather than a counter, so editing a typo on a target page doesn't invalidate every cached page on the site.

## What I'd take from all of this

Three things, and they're all the same thing really.

Decide where the truth lives early. Every good decision here came from that, and everything I had to unpick came from having two sources that could argue with each other.

Watch for the places you're about to guess on somebody's behalf. Most of this work was finding those and putting the decision back where it belonged.

And silence is the worst failure. The links vanishing with no error cost me a day. A keyword pointing at a deleted page used to just stop working. Both of those now say so, in a place somebody will actually look.

The package is on [GitHub](https://github.com/OwainWilliams/OC.AutoLink) if you want a look.
