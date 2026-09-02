# Umbraco Keyword Auto-Linker — Proof of Concept

## What this is

An Umbraco 17 package that automatically turns keyword mentions in Rich Text Editor
content into links to the corresponding page, with no editor action required.

Example: an editor writes "we tested this with Claude AI last week" in an RTE. If the
keyword `Claude AI` points at a page, the phrase renders as a link to that page. The
editor did nothing, and nothing was added to their document type — keywords are curated
centrally, in the Auto-linking section (decision 8).

Working package name: `Initials.AutoLink` (core), with an optional `Initials.AutoLink.Automate`
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

Rejected as the driver of linking. Kept as an after-the-fact record between mentioning
page and target, giving a free "what links here" panel. Was out of scope for the PoC;
**built later, for a reason the original note did not anticipate** — see decision 9.
Nothing in the render path reads them, so the "not the mechanism" half still holds.

### 4. Keyword collisions are settled by hand, not by heuristic — and then made impossible

Added after the PoC, when two pages tagged with the same keyword turned out to resolve by tags-query order with
the loser dropped silently.

Which page a contested phrase should point at is an editorial decision. A priority number on the doctype, or
"nearest ancestor wins", would silence the symptom without ever recording that a decision was made. So resolution
precedence became **manual mapping, then uncontested tag, then nothing**, with an unresolved collision reported
rather than guessed.

**Superseded by decision 8.** With the tags gone, a keyword has one row per culture and a row has one destination,
so nothing can be contested and the whole conflict subsystem was deleted. The principle survives intact — it is
just enforced by a unique index now instead of by reporting. Keep the principle in mind if a second source of
keywords is ever proposed: it would bring the collisions back with it.

Rows live in `initialsAutoLinkKeywordMapping` (real `MigrationPlan`) and store the **target key rather than a URL**, so a
page that moves still resolves. Several keywords may point at one page, which is how synonyms and plurals are
expressed.

### 5. The audit is a dry-run scan, not stored relations

Also added after the PoC, for "which pages have auto-links applied" plus a way to switch individual ones off.

Decision 3 above kept relations as the audit trail. The audit was built differently: a **dry-run scan** that walks
published content and runs the real linker with a collector instead of a mutation. It needs no storage, no
background job and cannot go stale, and — the deciding argument — the report is the thing an editor clicks
"unlink" on, so it has to come from the same code path the renderer uses or it will offer to suppress links that
were never there.

What the scan cannot answer is what was *served* historically. If that is ever wanted, relations written as pages
render are still the right answer, and they remain unbuilt.

Suppressions (`initialsAutoLinkSuppression`) work at two levels, one page plus one keyword, or a keyword everywhere.
Suppressed keywords keep reserving their span so switching one off cannot promote a shorter overlapping keyword.

### 6. Keywords are per culture, not per site

The site went multilingual mid-PoC (en-US and en-GB, `linkKeywords` varying by culture), which broke the registry
outright: `ITagQuery.GetContentByTagGroup(group)` with no culture argument returns **nothing** once the property
varies, so every tag-driven link vanished silently. That specific trap is gone with the tags query, but it is why
the design is per culture at all.

The snapshot holds **one keyword set per configured language plus an invariant one**, each with its own targets,
matcher and suppressions, and each resolving URLs for its own culture. The renderer selects by
`IVariationContextAccessor.VariationContext.Culture`.

Both tables carry a culture, with empty meaning every culture, so a keyword added before the site varied keeps
applying and gets resolved separately for each language. Culture-specific rows win over all-culture ones.

### 7. An external link is a mapping row, not a second feature

Keywords can point outside the site. Rather than a parallel external-links table with its own precedence, culture
handling and UI, the mapping row gained a nullable `externalUrl` beside the nullable `targetKey`: "somebody chose
where this keyword goes" is one concept whether the destination is a node or a URL.

Consequence worth keeping in mind: **the dashboard creates keywords**, since an external link has no page to tag.
That was the only genuinely new capability — precedence, cultures, suppression, the audit and the caps all worked
unchanged — and it turned out to be the thread that unravelled the tags entirely. See decision 8.

External URLs are validated to absolute http or https at the API **and** again at registry build. This is the first
editor-supplied string the package puts in an href, so it is a security boundary, not formatting.

### 8. Keywords are managed centrally, not on document types

The `linkKeywords` Tags property is gone. Keywords are created on the **Auto-linking** screen, and the destination
is Umbraco's **Multi URL Picker** (`umb-input-multi-url`, capped at one item — how core does a single-link picker),
which is what makes "a page" and "an outside URL" one decision made in one control.

Decision 7 is what argued for this. A tag says "this page answers to this phrase", which reads well until you want
a synonym, a plural, a phrase whose best target carries no tag, or a destination that is not a page at all. Every
one of those already wanted a row in the table, so the table was the real source and the tags were a second way in
that could disagree with it — and the disagreements were the collisions of decision 4.

Rejected alternative: **keep reading tags as a secondary source** for sites already using them. That leaves two
sources of truth, keeps the entire conflict subsystem alive to arbitrate between them, and makes "control it all
from one screen" only half true. Not worth it for an unreleased package.

Consequences to hold on to:

- **No migration was written.** Existing tag values stay in the content, ignored. A site with tags starts with an
  empty screen — which was the explicit call, not an oversight.
- **Creating a target is two steps now**, publish then add the keyword. Retroactivity (decision 1) is untouched;
  the work just moved from the content editor to one screen.
- **A broken destination stops linking rather than falling back.** There is no tag left to fall back to, so the row
  reports as unresolved. Unresolved rows sort first and open themselves.
- **`excludeFromAutoLinking` stayed on the document type.** "Do not scan this page's copy" is genuinely a property
  of the page, not of any keyword, so the schema installer still exists — for that one boolean.
- **Teardown now destroys every keyword**, not just decisions layered over tags. There is no other copy.
- The picker offers **media**, an **anchor**, and **open in new window**. The anchor is hidden, media is refused as
  it is picked, and a set target prints a line saying it will not be used. Silently dropping editor input is the
  thing being avoided in all three.

### 9. Relations exist to make Umbraco do the warning

Deleting the page a keyword points at silently breaks every auto-link to it. That is decision 1 working as
designed — no dead anchors — but it means the damage is invisible at the moment somebody does it.

Rather than build a warning, register the facts Umbraco already knows how to warn about: a relation type with
**`isDependency: true`**, written between mentioning page and target. The Info tab's "Referenced by" list and the
delete dialog's "The following items depend on this" then come for free.

**The direction is the trap, and it is the opposite of the obvious reading.** Umbraco stores a reference with the
*referencing* item as the parent — a Content Picker on A pointing at B is `parent=A, child=B` — and answers "what
uses this" by looking up `childId`. Written target-as-parent it looks fine in the database and warns on entirely
the wrong pages. Check `umbDocument` rows on a real site before trusting either reading.

Written by reconciling against the scan, because that is the only thing that knows which pages carry links, and
only for mentions that actually became anchors. Consequences: relations are as fresh as the last scan, and a
keyword whose target nothing mentions has no relation and so raises no dependency warning.

Deleting the page clears them — but Umbraco does that itself, during the delete, before `ContentDeletedNotification`
fires. Our handler is a backstop that normally finds nothing. Do not "fix" it by assuming it is doing the work.

Full write-up, including the v17 authorization traps that cost the most time, in `docs/build-log.md`.

### 10. Public means promised: interfaces yes, implementations no

Added when the package became NuGet-installable. Everything was `public`, which at 1.0 would make all 65 types a
compatibility promise — and the implementations are exactly what churns.

So: **service interfaces and the models they expose are public, every implementation is internal.** 65 public types
down to 32. Tests reach the implementations through `InternalsVisibleTo`, because the rules live in the
implementations, not the interfaces.

**The split is not a matter of taste, it is forced, and the forcing chain is worth knowing.** ASP.NET Core only
discovers `public` controllers, a public constructor cannot take a less accessible parameter (CS0051), and the
controllers are constructor-injected with `IAutoLinkScanner`, `IKeywordRegistry`, `IKeywordMappingStore`,
`IKeywordSuppressionStore`, `IAutoLinkRelationWriter` and `IAutoLinkUninstaller`. Those six are therefore public
whether you like it or not, and everything reachable through their members follows them out — which is why
`AutoLinkScanReport`, `AutoLinkPlacement`, `KeywordMapping` and friends are still public. `AutoLinkPlacement` was
tried as internal and the compiler refused, via `ScannedPage`.

`IAutoLinker` and `IKeywordRegistryInvalidator` went internal because nothing public injects them. `IAutoLinker`
also could not have been public honestly: `Preview` takes `AutoLinkRequestState`, which is per-request plumbing
nobody outside could construct.

**Compiling clean proves nothing here, so it was checked on a running site.** Umbraco finds migrations, the cache
refresher and the property value converter by reflection and DI, and every one of those failures is silent. Verified
on the Clean site: both migration plans ran from the log, `data-autolink` anchors rendered on three pages including
an external one, the six endpoints appeared in `/umbraco/swagger` and returned 401 rather than 500. Re-check the
same four things if the accessibility of anything registered in the composer changes.

Only `AutoLinkComposer` still relies on being publicly scanned. Do not make it internal.

---

## Architecture

### Keyword registry

Rows in `initialsAutoLinkKeywordMapping`, edited on the Auto-linking screen. Each row is a
keyword, a culture, and either a page key or an absolute URL. There is no second source
— see decision 8. The only page-level property left is the
`excludeFromAutoLinking` boolean, for pages that shouldn't be *scanned*.

### Cached automaton

Singleton service holding `Dictionary<string, KeywordTarget>` plus a single compiled
`Regex` with alternation **sorted longest-first**, so `Claude AI Sonnet` wins over
`Claude`. Resolved URLs cached alongside keys — URL resolution is most of the build cost.
Target names come from one `IContentService.GetByIds` call per rebuild, not one per
keyword per culture.

Rebuilt lazily against a version stamp.

### Invalidation stamp

Global, not per-target. Any page's output depends on the whole keyword set, and you
can't know which pages mention which keywords without rendering them. Coarse but
correct; keyword changes are rare.

Bump on a **content hash of the built dictionary**, not on every publish of a target
doctype. Most target-page edits don't touch keywords or URLs, and a typo fix in body
copy shouldn't nuke site-wide cached output. Build dictionary → hash keyword set +
resolved URLs → only bump if different.

Hooked from `ContentCacheRefresherNotification`, not from the publish/unpublish/delete notifications the original
design named — the field notes below have the two production reasons (cache settling, and other servers).

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

> **All three spikes pass.** Built in `src/Initials.AutoLink/`, verified on the Clean site in `Autolink/`.
> See `docs/build-log.md` for evidence and measurements.
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

- ~~URL resolution outside a request context.~~ **Resolved.** `IPublishedUrlProvider.GetUrl(Guid, UrlMode.Relative,
  culture)` takes a key directly, which keeps the build synchronous — `IPublishedContentCache` is async-only in v17.
  Wrap it in an `IUmbracoContextFactory.EnsureUmbracoContext()` block, since rebuilds also fire from notification
  handlers with no ambient context.
- ~~`RichTextEditorValueConverter` constructor dependencies.~~ **Resolved by sidestepping** — see
  decorate-don't-subclass above.
- ~~Exact tags query API surface.~~ **Moot.** `ITagQuery` was the answer, and it is no longer used at all
  (decision 8). Worth knowing if it ever comes back: it is registered **scoped**, so a singleton registry has to
  resolve it from an `IServiceScope` per rebuild rather than holding one. The stores and services the registry does
  use are scoped for the same reason and handled the same way.

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

## Field notes

The source carries **no narrative comments by policy**: `<summary>` docs stay everywhere (on public types they
ship in the nupkg as IntelliSense; on internal ones they are the one-line signpost), while narrative `<remarks>`
on internal types and every inline comment live here instead. If a change makes one of these wrong, fix it here
in the same commit.

### The linker

- Scan-time suppression is an `AsyncLocal`, not a field: a scan reads many pages inside one async flow and must
  not switch linking off for front-end requests being served concurrently. Reading a converted property value
  runs the value converter — which is where linking happens — so without this the scan would double-link and
  spend the page budget before its own preview ran.
- A cheap raw-string regex scan gates the AngleSharp parse. Most markup contains no keyword at all; a false
  positive inside an attribute only costs a parse.
- Markup is parsed into a detached div because that gives a stable InnerHtml round trip. Any parse failure is
  caught: a markup edge case must never take down a render.
- Precedence inside one property: anything the editor hand-linked wins (already pointing at the target means no
  second link); a page never links to itself; a suppressed mention is reported (naming the suppression row
  actually in force, so the audit lifts that one) but does not burn the keyword's allowance; the budget check
  runs last so a rejected candidate spends nothing. `match.Value` is what lands in the anchor, so the editor's
  casing is preserved.
- External links get `data-autolink-external` as a *second* attribute rather than a different value for
  `data-autolink`, so anything keying on the first keeps working. Outside a request (background render, unit
  test) the budget applies per call.
- `Preview` throws away the rewritten markup; only the reported placements matter.
- `KeywordMatcher` is its own type so longest-first and per-keyword word boundaries are tested directly, and so
  the registry and the tests cannot disagree about what is matchable. Suppressed keywords stay in the automaton
  and reserve their span (decision 5); an unresolved keyword is absent and reserves nothing.
- "First occurrence per page" state (`AutoLinkRequestState`) also tallies skip *reports* separately per reason,
  capped, so five mentions produce one row per reason instead of five identical ones.

### Registry and invalidation

- The rebuilt snapshot is only swapped in when its content hash differs, so re-saving the same destination or
  publishing an unrelated edit on a target page holds the stamp still and invalidates nothing downstream.
- The singleton registry resolves scoped services (stores, `IUmbracoContextFactory`, URL provider) from a fresh
  `IServiceScope` per rebuild. Blocking on the async `ILanguageService` is fine there: rebuilds happen on keyword
  changes, not per render, and there is no synchronisation context to deadlock against.
- One `IContentService.GetByIds` fetches every target page, shared across cultures; `GetCultureName` returns
  null for a non-varying page, hence the `Name` fallback. A failed rebuild logs and returns the empty snapshot —
  render unlinked rather than take the site down.
- Invalidation is hooked to `ContentCacheRefresherNotification`, **not** `ContentPublished`: published fires
  inside the publish before the cache settles (a render at that moment could rebuild stale and mark itself
  clean), and it only fires on the publishing server — the refresher runs on every node via the distributed
  cache. The package's own `AutoLinkCacheRefresher` exists for the same reason: a keyword decision saved through
  the API otherwise invalidated only the node that served the request.

### Persistence

- A missing table (migration not yet run) degrades instead of throwing: the mapping store returns no keywords
  (site behaves as if the package were absent), the suppression store returns none (a link that should be
  suppressed is visible and fixable). Both are survivable in a way a failed request is not.
- The stores invalidate the registry themselves — they are the code that knows rows changed, and an invalidation
  nobody sends leaves other servers resolving the old way until the next content change.
- `keywordKey` is stored lower-cased next to the display-cased `keyword` because SQLite text comparison is
  case-sensitive and SQL Server's default collation is not; the unique index rides the lower-cased copy.
  Suppression `pageKey` uses `Guid.Empty` (not null) for "everywhere" because providers disagree on nulls in
  unique indexes. Suppressing twice is the same decision, not an error.
- Schema changes rebuild the table (read rows out, drop, recreate from the DTO, put rows back) because Umbraco's
  migration layer refuses `ALTER TABLE` on SQLite outright.
- `AddKeywordRelationType` guards by alias *and* by name: `umbracoRelationType` has a unique index on both, a
  foreign relation type carrying our display name would fail the insert, and a failed migration re-runs every
  boot. A name collision is reported and skipped — that relation type is not ours to adopt or rename.

### Scanning and relations

- The scan enumerates keys from `IContentService` because the published cache exposes no root enumeration, then
  reads pages from the published cache. An invariant page is examined once per site language, not once: it is
  served in every language and the renderer picks keywords by request culture. Scanning it invariantly was a
  real gap — a non-varying page rendered links and appeared nowhere in the report.
- The `VariationContext` is what selects a culture for variant values nested inside blocks, where there is no
  per-call culture argument.
- One `AutoLinkRequestState` per page per culture, shared across all its rich text properties, so the report
  honours the caps exactly as a request would. An unroutable variant is recorded, not dropped — "my page is
  missing from the report" is otherwise unanswerable. One broken property logs and continues. No short-circuit
  on an empty snapshot: "0 pages scanned" on a walk that never happened reads as a broken scan.
- Relations: written from the scan (render-time writing would mean database writes on the front end), only for
  mentions that actually became anchors, deduplicated across cultures (`umbracoRelation` has no culture column).
  The mentioning page is the **parent** — decision 9 has the direction trap. A duplicate pair self-heals: keep
  the first, the rest fall into the removal set. A missing relation type logs and skips — the scan report is
  still correct, it just is not recorded. External targets have no node, so no relation.
- The delete/trash warning handler covers the one case with no relation behind it: a keyword pointing at a page
  nothing mentions yet. It warns, never cancels. The `ContentDeleted` cleanup is a backstop that normally finds
  nothing — Umbraco clears a node's relations during the delete (verified 17.6.1 on SQLite: no `ON DELETE
  CASCADE`; the delete itself does it).

### Install, uninstall and migrations

- Two migration plans because they answer independent questions: the decision tables belong to every install and
  run unconditionally; the opt-out schema is configuration-driven, and a plan step is spent for good — consuming
  it before anything is nominated would mean a site configured later never gets the property. The handler
  executes the schema plan only once the feature is configured, logs failures per plan (they fail for unrelated
  reasons), and does nothing mid-install/upgrade — Umbraco starts us again after.
- `InstallAutoLinkSchema` is a migration, not a startup handler: doing it per boot meant editing document types
  every start, and under `InMemoryAuto` models it regenerated models beneath compiled views so the first page
  load after install failed. Everything in it is additive and idempotent; a nominated doctype that does not
  exist *throws deliberately*, leaving the plan state to retry next boot, because unattended installs import
  starter kits around the same time and a one-shot that gave up quietly leaves a site that scans nothing.
- `RemoveLegacyKeywordProperty` is the one place the package deletes data, scoped by *datatype* (only properties
  bound to the datatype this package created), datatype removed last and only when unbound, through the service
  layer so caches stay consistent. `cmsTags` rows survive — core has no unused-tag collection, they are inert,
  and going at the tag tables directly is not worth it. The exclude boolean stays.
- Teardown exists because NuGet removal has no uninstall hook. The dangerous half is migration *state*: dropping
  tables while `umbracoKeyValue` still says the plan completed means a reinstall never recreates them. Only the
  main plan is rewound — rewinding the schema plan would re-add a property somebody removed on purpose. `DROP
  TABLE IF EXISTS` is used because SQLite and every supported SQL Server accept it and it keeps teardown
  idempotent; relations are cleared before the type even though deleting the type would cascade, because a
  half-finished install is exactly what teardown mops up; the registry is invalidated after, since its snapshot
  is built on tables that no longer exist. Failing relation cleanup must not leave tables dropped but state
  untouched — the one combination that comes back broken. `TeardownResult` reports no per-table flag because a
  DDL affected-row count cannot vary with the truth.

### API and security

- Both authorization handlers use `TryGetUmbracoUser`, not `GetUmbracoUser`: the throwing overload turns an
  anonymous request into a 500 instead of a 401.
- Umbraco's own section-access requirement type is internal, so the package brings its own; the check is the
  same one (section alias against `IUser.AllowedSections`). Teardown is a separate authorization concept —
  the group using the dashboard daily is exactly the group that should not be able to drop both tables, and the
  confirmation token prevents an accident, not a permission.
- The keywords endpoint resolves rows against the registry snapshot so the screen cannot disagree with the
  renderer; unresolved rows are returned and marked, because they are the thing needing attention. External URLs
  are validated at save *and* at registry build (decision 7): the first editor-typed string in an href is an XSS
  boundary. Keyword max length matches the column so an over-long keyword is a 400, not a database error.
- The scan endpoint treats relation reconciliation as bookkeeping: its failure must not turn a good scan into a
  failed request. Mutation endpoints are idempotent. Swagger config: 17.6.1 ships Microsoft.OpenApi 2.x, where
  `OpenApiInfo` is no longer under `.Models`.
- Skip reasons are stable codes, not sentences, so the screen can phrase and count them; every one used to be a
  silent skip, which made the audit impossible to trust.

### Telemetry

- Not telemetry of our own: Umbraco's `ReportSiteJob` collects every `IDetailedTelemetryProvider` and posts to
  telemetry.umbraco.com under the site owner's chosen level; nothing runs below `Detailed` and the package
  author never sees it. Counts only — a keyword is editorial content and a destination URL can identify a
  client. Every key carries the `AutoLink` prefix because the report is a flat bag shared with every provider.
  Safe pre-migration (stores return empty), and everything else is caught: telemetry must never fail a health
  job. That Umbraco gates on `Detailed` is deliberately untested — it would be testing their internal class
  with ours as the fixture; registration is proven by booting in Development, where the provider graph validates.

### The dashboard (autolink-keywords.js, lang/en.js)

- `.mention` is a positional four-column grid (name | path | status | action). Children map to columns by
  position, so anything added to a row must share an existing cell (the culture pill lives inside the `.place`
  span with the URL) or every later child shifts a column — this has happened.
- Page-name anchors point at the document workspace. The variant segment is load-bearing: a culture for a
  variant document, the literal `invariant` otherwise, and the wrong one renders a *blank workspace*, not an
  error — which is why the API models carry `VariesByCulture`. No `target="_blank"` on those anchors: an in-app
  click soft-navigates through Umbraco's router, and a cold deep-link to a workspace is exactly what renders
  blank. Front-end URL anchors keep `target="_blank"`.
- Mentions on the all-languages tab include every culture's scan rows (grouped by page *and* culture): scan rows
  always carry a concrete culture, so an exact match against the empty tab culture would report everything as
  mentioned nowhere.
- Stored rows carry lower-cased cultures (index keys); `#displayCulture` maps them back to the registry's
  spelling before showing or routing.
- Localization terms that need values are **functions** — the old `%0%` token style renders literally from
  v14 on. `en.js` registers as base `en`, so it resolves for every English variant; a new language is a copy of
  the file plus a `umbraco-package.json` registration.
- The `?v=` cache-busters are the only thing getting a consumer's browser off a cached dashboard: the backoffice
  pins modules to their URL hard enough that even a hard reload can serve stale JS. When iterating locally, tick
  "Disable cache" in DevTools.

### Packaging and CI

- csproj: the Umbraco dependency range is a floor *and* a ceiling (`[17.6.1, 18)`) — a bare minimum would let
  NuGet resolve against 18 and fail at runtime instead of restore. AngleSharp flows to consumers deliberately:
  the HTML walk is the package. README and icon pack to the nupkg root, which is where `PackageReadmeFile` and
  `PackageIcon` expect them. `ManagePackageVersionsCentrally=false` because the project sits outside the
  `Autolink/` folder that owns `Directory.Packages.props`. CS1591 is excluded rather than answered — ninety-odd
  DI constructors, and a comment written to silence a warning is worse than none.
- The version lives in the csproj, the manifest's `version`, and a `?v=` cache-buster on every asset path in the
  manifest; `tools/bump-version.ps1` moves them all together and the build workflow fails on drift. Don't state
  the count anywhere — it has gone stale twice.
- build.yml runs on every branch push but **not** tags (`branches: ['**']`) — tags belong to release.yml, which
  re-runs restore/build/test itself rather than trusting a parallel run was green. The marketplace listing file
  is schema-validated (`--regex-variant python`: Umbraco's schema contains a `\:` escape that strict ECMA regex
  rejects), and the pack is checked for the manifest, dashboard, XML docs, README and icon — the things that
  silently break a package.
- release.yml: tag must equal the csproj version (nuget.org versions are immutable — a mispush can only be
  unlisted); trusted publishing exchanges the job's OIDC token for a short-lived key via `NuGet/login@v1`, whose
  `user` is the **policy creator** (`scottishcoder`), not the `initials-labs` org that owns the package — the
  org name 401s. The policy names the repo and the workflow *filename*, so renaming release.yml breaks the
  exchange. Pushing the nupkg pushes the snupkg beside it; `--skip-duplicate` makes a rerun of a half-failed
  release safe.
- The Clean site only boots properly with `ASPNETCORE_ENVIRONMENT=Development` set — the connection string lives
  in appsettings.Development.json, and without it the site serves the install wizard with 200s. `article.cshtml`
  renders the `markdownTest` property weakly typed as a verification harness; inert on a fresh clone.

---

## Conventions

- Umbraco 17, .NET / C#
- Razor Class Library packaging via `StaticWebAssetBasePath` if this becomes a package
- Branches are `type/description` — this is a personal project with no Jira, so no ticket
  number in the name. Conventional Commits, PR title prefixes `[FEAT]` / `[BUG]` etc.
- Blog write-up is intended. Casual conversational tone, no em dashes. Working angle:
  "I built an auto-linker and here's why I didn't use Examine."
