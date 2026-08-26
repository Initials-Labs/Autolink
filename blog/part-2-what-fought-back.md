# Four things that caught me out building the auto-linker

**Part two of three: a sixteen argument constructor, a test site that saved me, and a lookup that went quiet**

[Part one](part-1-why-not-examine.md) covered the idea and the decision to do the linking at render time rather than publish time. This post is the parts of Umbraco 17 that didn't go the way I expected.

The plan was simple enough. Umbraco has a step where the rich text you've stored gets turned into the HTML that goes out to the page, and that's the value converter. Get in there, add the links on the way past, done.

The plan was fine. Getting in there was the interesting part.

## The sixteen argument constructor

My first thought was to inherit from Umbraco's built in rich text converter, let it do its normal job, then adjust what came back.

The class I expected doesn't exist any more, which is fair enough, so I went looking for the real one. It's `RteBlockRenderingValueConverter`, and its constructor takes sixteen things. Sitting next to it in the same file is an older version taking fourteen, already marked `[Obsolete]`.

That's worth a second look, because it tells you something useful. The list of dependencies changed twice inside the lifetime of one major version. If I inherit from that class I have to declare all sixteen myself and pass them up to the base, and the next time Umbraco adds a seventeenth my package stops compiling, for a list of things I never wanted and never touch.

So instead of being a converter, my class asks for one:

```csharp
public AutoLinkRichTextValueConverter(
    RteBlockRenderingValueConverter inner,   // whatever it needs, DI already knows
    IAutoLinker linker,
    IOptionsMonitor<AutoLinkOptions> options)
```

Umbraco builds the inner one with all sixteen of whatever they are, I hand it the work, take the HTML back, add my links and pass it on. The number sixteen appears nowhere in my code, so it can be forty next year and I won't notice.

One thing to watch if you do this. Replacing the built in converter takes it out of the collection and its DI registration goes with it, so you have to register it again yourself or your wrapper can't resolve the thing it wraps:

```csharp
builder.Services.AddTransient<RteBlockRenderingValueConverter>();
builder.PropertyValueConverters()
       .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();
```

Sitting at this layer is also what buys the useful bit: rich text nested inside a Block List or Block Grid is just another property that converts the same way, so it works in blocks with no changes to any views.

## The test site did me a favour

I built this against the Clean starter kit, and I got lucky with it.

Clean articles don't have a rich text property on them at all. Not one. Every bit of body copy lives inside a Block List, in rows, one level below the page.

I had been planning to get the simple case working first and deal with blocks afterwards. There was no simple case. Nested rich text was the only path that could possibly work, so it had to work on day one or the idea was finished.

That was a gift. If Clean had a plain rich text property I'd have got that working, felt pleased with myself, and found out about blocks much later when it would have been far more annoying to fix.

If you're testing something, test it somewhere awkward. A tidy test site tells you what you want to hear.

## Don't use regular expressions on HTML

You know this already and so did I, but it's worth being specific about why it bites here.

The obvious approach is to search the HTML for your keyword and replace it with an anchor tag. Two things go wrong quickly.

You match text inside a tag rather than text on the page. If a keyword happens to appear inside a link's `href`, you rewrite the address and break the link.

And you put anchors inside anchors. If somebody already linked `Block Grid` by hand, you've now nested a link inside a link, which browsers handle by doing whatever they feel like.

The answer is to stop treating the HTML as a string. I parse it with AngleSharp, walk the text nodes only, and leave everything else alone. Then there's a short list of elements to skip: anything that's already a link, code and `pre`, and headings, because a heading full of links looks wrong.

There's a cheap trick worth having as well. Before parsing anything, run the keyword regex over the raw string to see whether there's a keyword in there at all. Most paragraphs don't have one. A scan over a string costs very little, so you only pay for parsing on the text that's actually going to change.

## The one that cost me a day

This is the one I'd most want to warn people about.

The site went bilingual partway through, English and American English for the demo, and I set the keyword property to vary by culture. That's obviously correct, because keywords differ per language.

Every link on the site stopped working.

No error. Nothing in the log, nothing in the browser console. The keywords were still sat in the property when I opened a page, exactly where I'd typed them. The site had just quietly stopped linking anything, anywhere.

The cause is that `ITagQuery.GetContentByTagGroup(group)` takes an optional culture. I wasn't passing one, because until that morning there was nothing to pass. Once the property varies by culture, that call with no culture returns nothing at all. Not an error, not a warning, an empty list. Passing the culture, `GetContentByTagGroup(group, "en-US")`, returns them correctly.

Six keywords before, zero afterwards, with no other change.

Two things I took away from it.

An empty result is not the same as no result. Anywhere you ask a question and get nothing back, it's worth knowing whether nothing means there genuinely aren't any, or means you asked wrong. Those look identical and mean completely different things.

The screens that lie to you are the expensive ones. The keywords were visible in the editor the whole time, so everything looked correct. If the property had gone blank I'd have found it in five minutes.

This is also why the whole package is built around cultures now. There's one keyword set per language plus an invariant one, each with its own targets and URLs, and the renderer picks the right one from the variation context.

The trap doesn't exist any more, as it happens, because in part three I take the tags out altogether for entirely unrelated reasons. I'm still glad I found it on a demo site.

## Is it fast enough

Reasonable question, given I'm now parsing HTML on every page view.

I nearly built a caching layer for this without checking whether I needed one. I measured it instead. Same page, eight rich text blocks on it, 150 requests over one connection, feature off and then on:

| | ms per request |
|---|---|
| Auto-linking off | 6.57 |
| Auto-linking on | 7.69 |

About 1.1 milliseconds, and that figure includes losing property caching, because the wrapper reports `PropertyCacheLevel.None` on purpose. Default caching would serve stale markup after another page's keywords changed.

So the cache I was about to build would have added a pile of complexity, plus a whole new class of "why is this page stale" bugs, to save one millisecond. I didn't build it.

Measure first. Almost every time I've done that, the thing I was worried about was fine and something I hadn't thought about was the actual problem.

## Next

At this point it worked properly. Links appeared, old posts picked up new pages, and the demo was good.

It also wasn't ready for anybody else to use, and part three is the gap between those two things.
