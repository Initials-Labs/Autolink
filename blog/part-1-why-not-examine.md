# Turning keyword mentions into links, automatically

**Part one of three: why the auto-linker works at render time, and why I left Examine out of it**

On this blog I mention things in passing all the time. I'll write about Block Grid in March because it's relevant to whatever I'm building that week, then get round to writing the proper Block Grid page in September. The March post never links to it. Neither does the one from May, or the two from June. They all mention it, none of them point at it.

The fix is supposed to be that I go back and edit the old posts. I have a couple of hundred of them and I have never once done this.

So I wanted a package that does it for me. You give a page the words it should be found by, and anywhere those words appear in someone else's rich text, they turn into a link to that page. Write "we tested this with Claude AI last week", and if something on the site answers to `Claude AI`, that phrase becomes a link. The editor doesn't do anything, they just write the sentence.

The whole package hangs off one decision, which is when the linking actually happens.

## Where the linking happens

There are two places to do the work.

The first is at publish time. You hit save, the package finds the keywords in your rich text, inserts the links and stores the result. The work happens once and it's done.

The second is at render time. The stored content is left alone completely, and the links are added on the way out each time the page is served.

I went with render time, and there are four reasons for it.

Old posts pick up new links on their own. This is the main one. The September page goes live, and the next time somebody reads the March post the link is there. Nobody edited the March post and nobody republished it. The links are worked out fresh each time, so they can't be out of date.

The stored content stays exactly as the editor typed it. With publish time linking, an editor opens a post they wrote and finds anchor tags in it they didn't add. I wouldn't like that on my own content and I don't think anyone else would either.

Unpublishing a page doesn't leave a mess. With publish time linking, every post that linked to that page now has a dead link sat in the database. With render time linking the link simply stops appearing, because it was never stored anywhere.

The fourth reason is specific to me, but it's the one that settled it. I have three packages that push to Mastodon, Bluesky and LinkedIn when I publish something, and they work by hooking the publish event. Publish time linking would need the same event.

So think about adding one new keyword. To get it into all my old posts I would have to republish all my old posts. Two hundred of them, each firing the publish event, each posting to three social networks. Six hundred social posts from one keyword.

I could have added a flag to suppress the social push while a backfill runs, and that would have worked. But if a feature needs a flag to stop it spamming everyone I know, that's a decent sign it's in the wrong place.

## Why not Examine

Examine is the search engine built into Umbraco. It keeps an index of your content so you can query it quickly, and when you describe this feature out loud it sounds like exactly the job for it. I'm looking things up, Umbraco has a thing for looking things up.

I didn't use it, and I think that's right.

Look at what the lookup actually is. I have the phrase `Block Grid` and I want to know which page it belongs to. That's a word pointing at a page, and there might be a few hundred of them on a busy site. That isn't a search, it's a dictionary. A few hundred entries sits in memory quite happily and answers in no measurable time.

Now compare that to asking a search index the same question on every paragraph, of every rich text field, on every page, on every request. That's a lot of moving parts around something a `Dictionary` does instantly.

There is a question where Examine is the right tool, and it's the other way round. "Which pages mention the phrase Block Grid?" is a genuine search across all your content, and that's what Examine is for. It's useful for reporting, and I ended up needing something like it later on, which I'll come back to in part three. It just isn't the thing that puts the links in.

So the package holds the keywords in memory as a dictionary, alongside one compiled regular expression built from all of them, sorted longest first so `Claude AI Sonnet` wins over `Claude`. It rebuilds when the keywords change and otherwise sits there.

## What I'd take from this

The useful bit for me was working out where the truth lived before picking any tools. The truth here was a short list of words and where each one points. Once I'd written that sentence down, the search index stopped looking like the answer and started looking like a detour.

It's easy to reach for the bigger tool because the problem sounds bigger when you say it quickly.

## Next

That's the idea and the one decision everything else depends on. It worked sooner than I expected, which in my experience is when you should start looking for what you've missed.

Part two is the four things that caught me out building it, including one that stopped every link on the site working and never showed me a single error.
