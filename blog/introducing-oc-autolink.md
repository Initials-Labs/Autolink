# I built an auto-linker for Umbraco, and here's how it works

I mention things on this blog all the time without linking to them. A tool I'm using, a meetup I spoke at, a topic I keep coming back to. Eventually one of those gets its own proper page, and by then it's already been name-dropped across a dozen old posts, none of which point at it.

The fix is supposed to be that I go back and edit the old posts. I have a couple of hundred of them and I have never once done this.

So I built a package that does it for me. It's called OC.AutoLink, it's for Umbraco 17, and this post is a tour of what it does and the decisions underneath it. There's a three part series that goes into the detail, but this is the short version.

## What it does

You give the package a keyword and a destination. The keyword is a phrase like `Block Grid`, and the destination is either a page on your site or an address somewhere else. That's the whole setup.

From then on, anywhere that phrase appears in rich text content, it renders as a link to that destination. An editor writes "we tested this with Claude AI last week", and if something answers to `Claude AI`, that phrase becomes a link on the page. The editor did nothing. They just wrote the sentence.

The part I care about most is that it works backwards in time. Write the sentence today, add the keyword in six months, and every old post that mentions the phrase links to it the next time somebody reads it. Nobody edits anything, nobody republishes anything.

## The one decision everything hangs off

That comes from a single choice: the links are added when a page is served, not when it's saved.

If you're newer to CMS development, here's the distinction. When an editor hits publish, Umbraco stores their content in the database. Later, when a visitor requests the page, that stored content gets turned into the HTML that goes out. Those are two different moments, and you can do work at either one.

Doing the work at publish time sounds tidy. Find the keywords, insert the links, store the result, done once. But it means the links are frozen at whatever the keywords were on the day the editor pressed the button. A new keyword doesn't reach old posts unless you republish all of them. Deleting a target page leaves dead links sat in the database. And editors open their own content and find anchor tags in it they never typed, which I wouldn't like on my own posts and I don't think anyone else would either.

Doing the work at render time means the stored content is never touched. The links are worked out fresh every time the page goes out, so they can't be stale, they can't be orphaned, and there's nothing to clean up.

There was also a reason specific to me. I have packages that post to Mastodon, Bluesky and LinkedIn when I publish something. A publish time approach would have meant republishing two hundred posts to backfill one new keyword, which is six hundred social posts from a single change. If a feature needs a flag to stop it spamming everyone I know, it's in the wrong place.

## How the linking actually happens

Three pieces do the work.

**The keywords live in memory.** The lookup is "here's a phrase, which page owns it", and there are maybe a few hundred of those on a busy site. That's not a search, it's a dictionary, so it sits in memory as one. Alongside it is a single compiled regular expression built from every keyword, sorted longest first so `Claude AI Sonnet` wins over `Claude`. It rebuilds when the keywords change and otherwise just sits there. People sometimes assume this is a job for Examine, Umbraco's search index, and it isn't. Asking a search index a dictionary question on every paragraph of every request is a lot of moving parts around something a `Dictionary` answers instantly.

**The package sits in the value converter.** In Umbraco, a value converter is the step that turns stored rich text into the HTML your templates receive. Mine wraps the built in one: Umbraco's converter does its normal job, my wrapper takes the HTML on the way past, adds the links, and hands it on. The reason for working at that layer is coverage. Rich text nested inside a Block List or Block Grid converts through exactly the same path, so the linker works inside blocks with no changes to any views. On a modern Umbraco site that's not an edge case, it's most of the content.

**The HTML is parsed, not string replaced.** You cannot run a find and replace over raw HTML, because you'll rewrite a keyword that happens to sit inside a link's `href` and break it, or nest an anchor inside an anchor somebody already made by hand. So the markup is parsed properly with AngleSharp and only the actual text on the page is touched. Anything already inside a link, inside code, or inside a heading gets left alone.

There are a few rules on top that keep the output looking like a human did it. Only the first mention on a page gets linked, because a post with the same phrase linked five times looks like spam. A page never links to itself. The editor's original casing is preserved. And if the editor already linked a phrase to that target by hand, the package stays out of the way. Every link it does make carries a `data-autolink="true"` attribute, so you can always tell mine from theirs, style them differently, or strip the lot if you change your mind.

## One screen, one source of truth

Keywords are managed in a custom Auto-linking section in the backoffice. One screen, showing every keyword, where it points, and which pages mention it.

It didn't start that way. In the first version, you made a page linkable by adding tags to the page itself: tag the Block Grid page with `Block Grid` and the linker picked it up. That felt natural, because the keyword lives with the page it describes.

The problem is that a tag can only say one thing: this page answers to this exact phrase. It has nowhere to put a plural or a synonym without tagging the page again for every variation. It can't point a keyword at a page you don't control the tags on. And it can't point at an external site at all, because there's no page to put the tag on. Each of those needed the package to store its own record of "this phrase goes here", separate from any tag.

Once those records existed, the tags were a second copy of the same information, and two copies can disagree. Which one wins when they do? Rather than write rules for that, I removed the tags and kept the records. Keywords now live in one place, and there is nothing for that place to argue with.

The destination control is Umbraco's own Multi URL Picker capped at one item, so pointing a keyword at a page and pointing it at an external site are the same action in the same control. Keywords are per culture too, because a bilingual site wants different phrases per language, and each language resolves its own URLs.

One consequence of the single table that I'm particularly pleased with: a keyword can only exist once per culture, enforced by a unique index. Two pages fighting over the same phrase, and the code silently picking a winner, simply can't happen. The database won't accept the second row.

## The safety nets

Render time linking has a flip side. Deleting a page doesn't leave broken links behind, which is good, but it also means every link pointing at that page silently stops appearing, and nobody is told.

I didn't build a warning for that, because Umbraco already has one. The package writes relations between mentioning pages and their targets, flagged as dependencies, and Umbraco's own delete dialog then says "the following items depend on this" and lists them. The Info tab grows a "Referenced by" panel with the same list. No screen of mine anywhere.

There's also an audit. Because nothing is stored, "which pages have auto-links on them" isn't written down anywhere, so the package answers it by running the real linker over the published content in a dry run and reporting what it would do. That report is the thing an editor clicks "turn this off" on, and because it comes from the same code the renderer uses, it can never offer to switch off a link that was never there. Suppressions work per page or site wide, whichever you need.

## Is it fast enough

Fair question, given it parses HTML on every page view. I measured it on a page with eight rich text blocks: 6.57 milliseconds per request with the feature off, 7.69 with it on. About a millisecond. I nearly built a caching layer before checking, and I'm glad I checked, because the cache would have added complexity and a whole new class of stale page bugs to save one millisecond.

## Go and have a look

The package is on [GitHub](https://github.com/OwainWilliams/OC.AutoLink), with a Clean starter kit site in the repo you can run to see it working. Install it, grant yourself the Auto-linking section, add a keyword, and watch a post you wrote months ago quietly grow a link.

If you want the longer story, including the sixteen argument constructor, the bug that killed every link on the site without logging a single error, and the relation I wired up backwards, the three part series has all of it.
