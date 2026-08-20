# Umbraco Keyword Auto-Linker — Proof of Concept

## What this is

An Umbraco 17 package that automatically turns keyword mentions in Rich Text Editor
content into links to the corresponding page, with no editor action required.

Example: an editor writes "we tested this with Claude AI last week" in an RTE. If a
page in the content tree is tagged with the keyword `Claude AI`, the phrase renders as
a link to that page. The editor did nothing.

Working package name: `OC.AutoLink` (core), with an optional `OC.AutoLink.Automate`
for side effects. Tentative, not yet committed to.

---

## Design decisions already made

These were reasoned through before the repo existed. **Don't re-litigate them without
new information** — if you're about to suggest one of the rejected options below, read
why it was rejected first.

### 1. Transform at RENDER time, not publish time

The single most important decision. Stored RTE markup is never mutated.

Why:

- **Retroactivity.** An article written today mentioning a keyword with no target page
  picks up the link automatically when a target is created six months later. No
  backfill, no republish. This is the killer feature and publish-time baking destroys it.
- Editors never see anchors appear in markup they didn't write.
- Unpublishing a target doesn't leave dead `<a>` tags in the database.
- **Critical for this author specifically:** a publish-time approach would require
  republishing every page mentioning a new keyword. That fires
  `ContentPublishedNotification`, which is hooked by the author's existing social
  automation packages (`OC.Automate.Mastodon`, `OC.Automate.Bluesky`,
  `OC.Automate.LinkedIn`). A keyword backfill across 200 old articles would post all
  200 to three social networks. Survivable with a suppression flag, but a nasty way to
  find out.

### 2. Examine is NOT used for the lookup

Rejected. The lookup direction is keyword → target node, which is a reverse dictionary
of a few hundred entries. That belongs in memory as a compiled regex or Aho–Corasick
automaton, not an index round trip per property render.

Examine remains legitimately useful for the *opposite* direction — "which published
pages mention this keyword" — for auditing or invalidation reporting. Not core path.

### 3. Relations are an audit trail, not the mechanism

Rejected as the driver of linking. Kept as an after-the-fact record: `autoLinkedTo`
relations between mentioning page and target, written on a background job. Gives a free
"what links here" panel and a precise invalidation set. **Out of scope for the PoC.**

### 4. Keyword collisions are settled by hand, not by heuristic

Added after the PoC, when two pages tagged with the same keyword turned out to resolve by tags-query order with
the loser dropped silently.

Which page a contested phrase should point at is an editorial decision. A priority number on the doctype, or
"nearest ancestor wins", would silence the symptom without ever recording that a decision was made. So:
resolution precedence is **manual mapping, then uncontested tag, then nothing**, and an unresolved collision is
reported rather than guessed.

Mappings live in `ocAutoLinkKeywordMapping` (real `MigrationPlan`), store the **target key rather than a URL**,
and may point at a page carrying no tag at all — which is also how synonyms and plurals get expressed without
polluting a target's tag list. Editing happens in a custom **Auto-linking** backoffice section reading straight
off the registry snapshot, so the options offered are the ones the renderer considered.

### 5. The audit is a dry-run scan, not stored relations

Also added after the PoC, for "which pages have auto-links applied" plus a way to switch individual ones off.

Decision 3 above kept relations as the audit trail. The audit was built differently: a **dry-run scan** that walks
published content and runs the real linker with a collector instead of a mutation. It needs no storage, no
background job and cannot go stale, and — the deciding argument — the report is the thing an editor clicks
"unlink" on, so it has to come from the same code path the renderer uses or it will offer to suppress links that
were never there.

What the scan cannot answer is what was *served* historically. If that is ever wanted, relations written as pages
render are still the right answer, and they remain unbuilt.

Suppressions (`ocAutoLinkSuppression`) work at two levels, one page plus one keyword, or a keyword everywhere.
Suppressed keywords keep reserving their span so switching one off cannot promote a shorter overlapping keyword.

### 6. Keywords are per culture, not per site

The site went multilingual mid-PoC (en-US and en-GB, `linkKeywords` varying by culture), which broke the registry
outright: `ITagQuery.GetContentByTagGroup(group)` with no culture argument returns **nothing** once the property
varies, so every tag-driven link vanished silently.

So the snapshot holds **one keyword set per configured language plus an invariant one**, each with its own targets,
matcher, conflicts and suppressions, and each resolving URLs for its own culture. The renderer selects by
`IVariationContextAccessor.VariationContext.Culture`. Invariant tags merge into every culture, so a site mixing
varying and non-varying doctypes works.

Both decision tables carry a culture, with empty meaning every culture, so a decision made before the site varied
keeps applying. Culture-specific rows win over all-culture ones.

### 7. An external link is a mapping row, not a second feature

Keywords can point outside the site. Rather than a parallel external-links table with its own precedence, culture
handling and UI, the mapping row gained a nullable `externalUrl` beside the nullable `targetKey`: "somebody chose
where this keyword goes" is one concept whether the destination is a node or a URL.

Consequence worth keeping in mind: **the dashboard now creates keywords**, since an external link has no page to tag.
That is the only genuinely new capability; precedence, cultures, suppression, the audit and the caps all worked
unchanged.

External URLs are validated to absolute http or https at the API **and** again at registry build. This is the first
editor-supplied string the package puts in an href, so it is a security boundary, not formatting.

Full write-up, including the v17 authorization traps that cost the most time, in `src/OC.AutoLink/README.md`.

---

## Architecture

### Keyword registry

A `linkKeywords` property (Tags datatype) on any doctype that can be a link target.
Tags gives a queryable store for free and editors already know the UI. Sibling
`excludeFromAutoLinking` boolean for pages that shouldn't be *scanned*.

### Cached automaton

Singleton service holding `Dictionary<string, KeywordTarget>` plus a single compiled
`Regex` with alternation **sorted longest-first**, so `Claude AI Sonnet` wins over
`Claude`. Resolved URLs cached alongside keys — URL resolution is most of the build cost.

Rebuilt lazily against a version stamp.

### Invalidation stamp

Global, not per-target. Any page's output depends on the whole keyword set, and you
can't know which pages mention which keywords without rendering them. Coarse but
correct; keyword changes are rare.

Bump on a **content hash of the built dictionary**, not on every publish of a target
doctype. Most target-page edits don't touch keywords or URLs, and a typo fix in body
copy shouldn't nuke site-wide cached output. Build dictionary → hash keyword set +
resolved URLs → only bump if different.

Hooked from `ContentPublishedNotification`, `ContentUnpublishedNotification`,
`ContentDeletedNotification`.

### Wrapping property value converter

**Decorate, don't subclass.** Verified against 17.6.1: `RichTextEditorValueConverter` does not exist. The real
type is `RteBlockRenderingValueConverter` (in `Umbraco.Infrastructure`, deriving from
`SimpleRichTextValueConverter`), and it has a **sixteen parameter constructor**, sitting next to an already
`[Obsolete]` fourteen parameter overload — its dependency list moved within v17's own lifetime.

So: implement `IPropertyValueConverter`, take `RteBlockRenderingValueConverter` as a constructor dependency,
delegate every member, and post-process the markup from `ConvertIntermediateToObject`. None of those sixteen
parameters get named in our code, so the wrapper survives that churn. This removes what was flagged below as
the most likely thing to eat Spike 0's timebox.

`Replace<TReplaced, TReplacement>()` drops the built-in converter from the collection *and* its DI
registration, so re-register it explicitly (`builder.Services.AddTransient<RteBlockRenderingValueConverter>()`)
or the decorator cannot resolve it.

Delegate `IDeliveryApiPropertyValueConverter` straight through. Not implementing it doesn't leave the Delivery
API alone, it silently regresses it.

This is the elegant part: **it automatically covers RTEs nested inside Block List,
Block Grid, and nested blocks with zero view changes.** That premise is load-bearing —
see Spike 0 exit criteria.

Set `PropertyCacheLevel.None` on the wrapper and layer your own `IAppPolicyCache`
keyed on `(contentKey, propertyAlias, keywordStamp)`. Default `Elements` caching would
keep stale output when *another* page's keywords change.

If exposing over Delivery API, `ConvertIntermediateToDeliveryApiObject` needs the same
treatment. Out of scope for PoC.

### HTML-aware replacement

**Do not regex over raw HTML.** It will rewrite `href` attribute values and nest
anchors inside anchors. Parse with AngleSharp, walk text nodes only:

```csharp
var doc = _parser.ParseFragment(markup, contextElement);
foreach (var textNode in doc.Descendants<IText>().ToList())
{
    if (textNode.Ancestors<IElement>().Any(e =>
        e.LocalName is "a" or "code" or "pre" or "h1" or "h2" or "h3"))
        continue;
    // match, split, insert <a data-autolink="true"> siblings
}
```

Rules to encode:

- First occurrence per page only (or configurable max) — SEO caution
- Word-boundary matching
- Preserve the editor's original casing
- Never link a page to itself
- Skip a keyword if the editor already hand-linked to that target on the page
- Mark output `data-autolink="true"` so it's auditable and strippable later

---

## PoC scope

Three spikes. Total estimate **1–2 focused days**. Risk is concentrated in Spike 0 and
is binary, not gradual.

> **All three spikes pass.** Built in `src/OC.AutoLink/`, verified on the Clean site in `Autolink/`.
> See `src/OC.AutoLink/README.md` for evidence and measurements.
>
> Spike 0 turned out to be a stronger test than planned: Clean has **no top-level RTE property on `article`
> at all** — every piece of article body copy is `richTextRow.content` inside a Block List. The nested case
> wasn't extra credit, it was the only path that could work, so there was no easy case to pass on by accident.

### Spike 0 — hardcoded dictionary, real pipeline (half a day)

Register the wrapping converter. Hardcode `Dictionary<string,string>` with two entries
pointing at real node URLs as **literal strings**. AngleSharp text-node walk. Render.

**Test page must include an RTE nested inside a Block Grid or Block List from the
start.**

Exit criteria — both must pass:

1. Converter substitution takes effect at all in v17
2. It fires for RTEs nested inside blocks

If (2) fails, the "no view changes needed" premise is gone and the approach needs
rethinking. **Timebox to one day.** Blowing through it is signal to reconsider, not to
push harder.

### Spike 1 — real dictionary (2–3 hours)

Swap hardcoded strings for tags query + URL resolution. This is where the main API
uncertainty lands (see below).

### Spike 2 — prove retroactivity (1–2 hours)

Stamp bump on publish, cache keyed on it. Then:

1. Publish an article mentioning "Widget Foo", no target exists. Renders as plain text.
2. Create a page, tag it `Widget Foo`, publish.
3. Reload the **original article without touching it**. Link appears.

If that passes, the design is validated. This is also the blog post screenshot and the
conference demo.

### Explicitly cut from PoC

Relations, Automate actions, Tiptap editor decoration, orphan keyword digest, Delivery
API support, configurable link caps. All known territory — they add days and prove
nothing.

**Also skip the caching layer initially.** Render without it and measure. AngleSharp on
article-length markup may be fast enough that the cache is premature.

> **Measured, and it is.** On `/blog/meetups/` (~8 rich text blocks), 150 requests over one connection:
> 6.57 ms/req off vs 7.69 ms/req on — **1.12 ms overhead, 17%**. That figure covers both the AngleSharp pass
> and the loss of property caching, since the wrapper reports `PropertyCacheLevel.None`. The stamp-keyed
> `IAppPolicyCache` is not worth the complexity yet.

---

## Known unknowns — all resolved against 17.6.1

- ~~URL resolution outside a request context.~~ **Resolved.** `ITagQuery.GetContentByTagGroup(group)` returns
  `IEnumerable<IPublishedContent>` straight off the published cache, so the tags query and URL resolution
  become the same call. Resolve with `IPublishedUrlProvider.GetUrl(content, UrlMode.Relative)` inside an
  `IUmbracoContextFactory.EnsureUmbracoContext()` block, since rebuilds also fire from notification handlers.
- ~~`RichTextEditorValueConverter` constructor dependencies.~~ **Resolved by sidestepping** — see
  decorate-don't-subclass above.
- ~~Exact tags query API surface.~~ **Resolved.** `ITagQuery` — note it is registered **scoped**, so a
  singleton registry must resolve it from an `IServiceScope` per rebuild rather than holding one.

### Gotcha that wasn't in the original design

**`owner` is the block's element, not the page.** For rich text nested in a Block List or Block Grid, the
`IPublishedElement owner` handed to the converter is the block's element — no Id, no Url. So "never link a
page to itself" cannot be derived from it. Use `IUmbracoContextAccessor` → `PublishedRequest.PublishedContent`.
(`IPropertyRenderingContextAccessor`, new in 17, only carries `Fallback` and does not help here.)

Related: "first occurrence per page" cannot be tracked within one property either, because a page *is* many
rich text properties. It needs request-scoped state — `IRequestCache`.

---

## Where Umbraco.Automate fits

Not in the core path — render-time means there's no publish-time work to hook. Reaching
for it there would mean going back to publish-time baking.

It fits the **side effects**, which are the same fire-and-forget-with-retry shape as the
existing social packages:

- Writing the relation audit trail (diff found links vs existing relations, reconcile)
- **Orphan keyword digest** — the most valuable one. Terms appearing frequently with no
  target page are a content strategy signal. "'Block Grid' appeared in 14 articles this
  month and has no hub page."
- Notification when a new target goes live and N existing pages started linking to it.
  Satisfying, and doubles as a regression check on invalidation.

All post-PoC.

---

## Other caveats

**CDN.** Render-time healing stops at the edge. Invalidating Umbraco's cache doesn't
touch cached HTML in Cloudflare or similar. Either wire a purge into the stamp bump, or
accept that retroactive links appear on natural TTL expiry.

**SEO.** Real argument for capping links per page. `data-autolink` marking exists so
links can be audited or stripped wholesale if search engines get grumpy.

---

## Conventions

- Umbraco 17, .NET / C#
- Razor Class Library packaging via `StaticWebAssetBasePath` if this becomes a package
- PRs follow the org `git-pr-standards` skill: branch `type/jiraNumber-description`,
  Conventional Commits, PR title prefixes `[FEAT]` / `[BUG]` etc.
- Blog write-up is intended. Casual conversational tone, no em dashes. Working angle:
  "I built an auto-linker and here's why I didn't use Examine."
