# Initials.AutoLink build log

How the package was built and verified: what was tried, what fought back, and what the numbers came out at.

Built and verified against **Umbraco 17.6.1 / .NET 10**.

Companion documents: `src/Initials.AutoLink/README.md` is what a consumer reads on nuget.org,
`docs/technical-spec.md` is the developer's map of the code, and `CLAUDE.md` holds the design decisions
with the rejected alternatives.

---

## Status: proof of concept complete

All three spikes pass. Evidence below is from the Clean starter kit site in `Autolink/`.

| Spike | Question | Result |
|---|---|---|
| 0 | Does converter substitution work in v17, and does it fire for RTEs nested in blocks? | **Pass** |
| 1 | Can the dictionary be built from real tags with resolved URLs? | **Pass** |
| 2 | Does an untouched article pick up a link when a target appears later? | **Pass** |
| 3 | Can a keyword two pages both claim be settled by hand, from the backoffice? | **Pass** |
| 4 | Can an audit show which pages carry links, and can individual ones be switched off? | **Pass** |
| 5 | Do tags, keywords and decisions work per language on a multilingual site? | **Pass** |
| 6 | Can a keyword point somewhere outside the site, per language? | **Pass** |

> **Keywords have since moved off document types entirely.** Spikes 1, 3 and 5 were built on a `linkKeywords` Tags
> property on every target page, and that property is gone: keywords live in the package's own table and are edited
> on the Auto-linking screen, with Umbraco's Multi URL Picker choosing between a page and an outside URL. What the
> spikes proved still holds — render-time substitution, nesting inside blocks, retroactivity, per-language keywords —
> but the evidence below that mentions tagging a page describes how it used to be reached. See
> [Keywords moved to the dashboard](#11-keywords-moved-to-the-dashboard).

### Why Spike 0 was a strong test here

Clean has **no top-level RTE property on `article` at all**. Every piece of article body copy is
`richTextRow.content` inside a Block List. So the nested-in-blocks case was not an extra credit check, it was
the only path that could possibly work. There was no easy case to accidentally pass on.

---

## How it works

### 1. Decorating the built-in converter

`AutoLinkRichTextValueConverter` **wraps** `RteBlockRenderingValueConverter` rather than subclassing it.

This is the single most important implementation decision, and it is not the one the original design assumed.
The design doc expected to subclass a type called `RichTextEditorValueConverter`. In 17.6.1 that type does not
exist. The real type is `RteBlockRenderingValueConverter`, and it has a **sixteen parameter constructor** —
sitting directly alongside an already `[Obsolete]` fourteen parameter overload, i.e. its dependency list moved
within v17's own lifetime.

Subclassing means re-declaring all sixteen parameters and forwarding them to `base`, and re-breaking every time
Umbraco adds a dependency. Injecting the converter instead means **none of those parameters are named in this
codebase at all**.

```csharp
public AutoLinkRichTextValueConverter(
    RteBlockRenderingValueConverter inner,   // whatever it needs, DI already knows
    IAutoLinker linker,
    IOptionsMonitor<AutoLinkOptions> options)
```

Because `Replace<TReplaced, TReplacement>()` removes the built-in converter from the collection *and* with it
its DI registration, the composer re-registers it explicitly so the wrapper can still resolve it:

```csharp
builder.Services.AddTransient<RteBlockRenderingValueConverter>();
builder.PropertyValueConverters()
       .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();
```

Sitting at the value converter layer is what buys "no view changes": rich text inside Block List, Block Grid
and nested blocks is just an ordinary published property that converts the same way.

`IDeliveryApiPropertyValueConverter` is delegated straight through. The Delivery API is out of PoC scope, but
not delegating it would have silently regressed it rather than leaving it alone.

### 2. Keyword registry

`KeywordRegistry` is a singleton holding a `KeywordSnapshot`: the keyword to target lookup, one compiled
`Regex`, and a content stamp.

Keywords come from one place: the rows in `initialsAutoLinkKeywordMapping`. A row names a keyword, a culture, and
either a page key or an absolute URL. There is no second source, which is what makes the whole build a loop over
rows rather than a reconciliation between two of them.

URLs are resolved once at build time via `IPublishedUrlProvider`, since resolution is most of the build cost, and
`GetUrl(Guid, ...)` takes a key directly — which keeps the build synchronous, `IPublishedContentCache` being
async-only in v17.

Target **names** cost one query for the whole rebuild. `IContentService.GetByIds` is called once with every key any
row points at, and the per-culture name comes off `IContentBase.GetCultureName(culture)` with `Name` as the fallback
for a page whose type does not vary. That query exists because the tags query used to hand back published content
with its name already attached; without it, a name would have been a database round trip per keyword per culture.

The stores and Umbraco's services here are **scoped**, so the singleton registry resolves them from a fresh
`IServiceScope` per rebuild rather than holding one.

The matcher is a single alternation sorted longest first, so `content editor` beats `editor`. Word boundaries
are applied **per keyword**, not around the whole group — `\b` only does the right thing next to a word
character, so wrapping the group would stop `C#` ever matching.

### 3. Invalidation

Global, via `ContentCacheRefresherNotification` — one hook where five content notifications used to be. Any
page's output depends on the whole keyword set, and you cannot know which pages mention which keywords without
rendering them.

The choice of hook matters twice over. `ContentPublished` fires *inside* the publish, before the published cache
has settled, so a render at that moment can rebuild from stale content and then mark itself clean, staying stale
until the next content change. It also only fires on the server that did the publishing — other nodes learn about
content changes through the distributed cache, which runs their refreshers, which raises this notification
there too.

The stamp is a **content hash of the built dictionary**, not a publish counter. A rebuild that produces an
identical keyword and URL set keeps the existing snapshot, so a typo fix on a target page does not move the
stamp.

### 4. HTML-aware replacement

AngleSharp, walking text nodes only. Markup is parsed into a detached `<div>` so `InnerHtml` round trips.
Never a regex over raw HTML — that rewrites `href` values and nests anchors inside anchors.

A cheap `Regex.IsMatch` over the raw string gates the parse, so markup with no keyword in it never gets
parsed at all.

Rules enforced:

- First occurrence per keyword per **page**, not per property. A page is many rich text properties — each
  block in a Block List converts separately — so the tally rides on `IRequestCache` and spans all of them.
- Word-boundary matching.
- Editor's original casing preserved (`match.Value`, not the registry keyword).
- Never links a page to itself.
- Skips a keyword if the editor already hand-linked to that target in the same property.
- Skips text inside `a`, `code`, `pre`, headings and friends (configurable).
- Output marked `data-autolink="true"` so it is auditable and strippable.

**The current page comes from `IUmbracoContextAccessor`, not from `owner`.** For rich text nested in a block,
the `IPublishedElement owner` handed to the converter is the *block's* element — it has no Id and no Url, so
self-link detection cannot use it. (`IPropertyRenderingContextAccessor`, new in 17, only carries `Fallback`.)

### 5. One keyword, one destination

A keyword has **one row per culture, and a row has one destination**. The unique index on
`(keywordKey, culture)` is what enforces it, so two pages cannot claim the same phrase and there is nothing for the
code to arbitrate. Resolution is a lookup, not a contest:

| Source | Meaning |
|---|---|
| `Manual` | The row names a page, and that page is routable in this culture |
| `External` | The row names an absolute http or https URL |
| *unresolved* | The row names something that will not resolve here |

A row stores the **page key, not its URL**, so a page that later moves or gets renamed still resolves and the move
shows up in the stamp. Several keywords may point at the same page, which is how synonyms and plurals are expressed.

**An unresolved keyword is left out of the matcher entirely** and reported on the screen. There used to be a fallback
here — a mapping whose target had gone fell back to whatever the tags said — and with the tags gone there is nothing
to fall back to, which is an improvement rather than a loss: the keyword stops linking, loudly, instead of quietly
linking somewhere nobody chose. The three ways a row goes unresolved are a deleted page, a page unpublished or never
published in this language, and a stored URL that is not absolute http or https.

Unresolved keywords do **not** reserve their span, and suppressed ones do. That distinction is deliberate. A
suppressed keyword resolves fine and is being held back on purpose, so it stays in the matcher and keeps its words —
otherwise switching `content editor` off would let `editor` link the same phrase somewhere nobody chose. An
unresolved keyword is a broken row to repair, not a decision to hold ground for.

### 6. Storage, API and backoffice section

`initialsAutoLinkKeywordMapping`, created by a real `MigrationPlan` (`AutoLinkMigrationPlan`) rather than a startup
fixup — the table is where the keywords live, so it is not something to recreate opportunistically. The keyword is
stored twice: `keywordKey` lower-cased carrying the unique index, so uniqueness behaves the same on SQLite
(case-sensitive text by default) as on SQL Server, and `keyword` preserving the casing somebody typed for display.

If the table is missing, `KeywordMappingStore.GetAll()` logs and returns empty, so a failed migration renders a site
that behaves as though the package were not installed rather than taking requests down with it.

```
GET    /umbraco/management/api/v1/autolink/keywords   rows per culture, unresolved first
PUT    /umbraco/management/api/v1/autolink/mapping    { keyword, culture, targetKey | externalUrl, label, nofollow }
DELETE /umbraco/management/api/v1/autolink/mapping    ?keyword=...&culture=...
```

Rows come from the store and their destinations from the registry snapshot, so what the screen shows is what the
renderer resolved. No second query that could disagree with it. A row the registry could not resolve is still
returned, marked unresolved, because that is the one thing on the screen anybody has to act on.

**One screen, organised by keyword.** The first attempt had two dashboards, one for choosing link destinations and
one auditing where links appeared. Both were keyed on keyword, so it was never obvious which direction a table was
showing — the pages listed under a keyword were its *targets* on one screen and the pages *mentioning* it on the
other. A keyword only means anything as the pair, so it is one row with both halves in columns and the detail
underneath:

```
KEYWORD          LINKS TO                              MENTIONS
harrie           About  /us/about/                      2 linked  1 not linked
  Set for en-US by admin@example.com.   [Change destination] [Remove keyword]
  MENTIONED ON 3 PAGES
    About     /us/about/     not linked: this page is the one the keyword points at.
    Features  /us/features/  linked                     [Do not link here]
```

"Links to" is the destination; "Mentions" is where the link actually renders. The keyword list and the scan are both
fetched on load and pivoted by keyword in the browser, which keeps each endpoint independently useful and costs one
extra request.

The UI is one plain ESM file, no build step: the backoffice ships an import map, so bare
`@umbraco-cms/backoffice/...` specifiers resolve at runtime. Static web assets are served from source in
development, so editing it needs only a hard refresh, not a restart.

#### Things that cost time in 17.6.1

- **`AuthorizationPolicies.BackOfficeAccess` 401s on a custom management API controller.** The backoffice reads
  any 401 as an expired session, so the symptom is not an error in the dashboard — it is being **logged out**
  the moment you open the section. Umbraco's own `SectionAccess*` policies are built on
  `AllowedApplicationRequirement`, which is `internal`, so a package adding its own section cannot reuse it.
  `SectionAccessRequirement` + `SectionAccessHandler` here are a ten-line reimplementation over the public
  `IAuthorizationHelper` and `IUser.AllowedSections`.
- **A dashboard must not use `umbHttpClient` for its own endpoints** for the same reason: failures route through
  backoffice error handling that cannot tell a package 401 from a dead session. It carries its own token from
  `UMB_AUTH_CONTEXT` and renders its own errors, so a package screen can never sign the user out.
- **A new section is not granted to anybody, including Administrators.** Tick it on the user group first, or the
  section simply is not in the nav. Worth knowing before demoing it live.
- **`Microsoft.OpenApi` 2.x.** `OpenApiInfo` is no longer under `.Models`, and `SwaggerDoc` needs
  `using Microsoft.Extensions.DependencyInjection`.

### 7. Audit: which pages carry links

Render-time linking records nothing, which makes "which pages have auto-links" a genuinely awkward question. There
are three ways to answer it and they are not equivalent:

| Source | Exact? | Complete? | Answers |
|---|---|---|---|
| **Dry-run scan** (built) | Yes, same code path | Yes, unrendered pages included | What a visitor would get *now* |
| Record as pages render | Yes | Only pages someone has visited | What was actually *served* |
| Examine | No — blind to headings, hand-links, caps, conflicts | Yes | Which pages *mention* a keyword |

`AutoLinkScanner` walks published content and calls `IAutoLinker.Preview`, which is the same method
`ProcessMarkup` uses with a collector attached instead of a mutation. That matters because the report is what you
click "unlink" on: a report from a different code path could offer to suppress a link that was never there.

Two wrinkles worth knowing:

- **Reading a property value links it.** Conversion is where linking happens, so a scan that reads converted
  values would link as a side effect and spend the page budget before the preview ran. `IAutoLinker.Suppress()`
  switches linking off for the current async flow — `AsyncLocal`, not a field, so a scan cannot switch linking off
  for front-end requests being served concurrently.
- **Only rich text and block editors are converted.** Converting every property meant resolving media, pickers and
  the rest: that was 2.6 seconds of a 2.6 second scan. Filtering on editor alias took the same 25 pages from
  ~2,600 ms to ~34 ms with identical results.

Nested rich text is reached by walking `BlockListModel`, `BlockGridModel` and grid areas recursively, with a depth
guard. On this site that is the only path that finds anything at all, since Clean keeps all body copy in blocks.

**Every mention is accounted for, linked or not.** The first version only reported mentions that became links, so
the five reasons a mention is legitimately skipped produced no row and no explanation — a page could mention a
keyword three times, appear once, and there was nothing to say why. Each skipped mention now carries a reason:

| Reason | Meaning |
|---|---|
| `self` | The mentioning page is the page the keyword points at |
| `hand-linked` | The editor already linked to that target in this property |
| `skipped-element` | The mention sits in a heading, an anchor, code |
| `limit` | The per-keyword or per-page allowance was already spent |
| `suppressed` | Held back by an editorial decision |

Rendering still skips text inside headings and anchors without looking at it. An audit looks anyway, so it can say
why a mention sitting in one was left alone — the one place where preview deliberately does more work than the
renderer. Reasons are capped per keyword per reason, so a page mentioning a keyword ten times yields one
explanatory row rather than ten.

**Whole pages are accounted for too.** A page with no routable URL in a culture, usually an unpublished variant, and
a page that opted out both land in the report's `Skipped` list with a reason. A page in neither list was examined and
simply has no keyword in its rich text — which is the answer to "why is my page missing", and it used to be
unanswerable.

**An invariant page is examined once per site language, not once against the invariant keyword set.** This was the
worst of the reporting gaps. A page whose doctype does not vary has no per-culture versions, so the obvious reading is
to scan it invariantly — but it is still *served* in every language, and the renderer picks keywords by the culture of
the request. So it rendered en-GB links while appearing nowhere in the report. On the Clean site that was every blog
post: the report showed two rows where the site was actually linking on ten.

### 8. Suppression: switching a link off

`initialsAutoLinkSuppression`, added by the second migration in the plan. Two levels, one table:

- **This page, this keyword** — the mention on that article stops being a link, everywhere else is untouched.
  Checked in `AutoLinker`, which already knows the current page key.
- **This keyword, everywhere** — a reversible off switch that does not require editing the target page tags.

`pageKey` uses `Guid.Empty` for "everywhere" rather than null, because a nullable column in a unique index means
different things on SQL Server and SQLite.

Suppressed keywords stay resolved in `Targets` and stay in the matcher, so they keep reserving their span. Switching
one off cannot let a shorter keyword hijack the phrase — see [One keyword, one destination](#5-one-keyword-one-destination)
for why an unresolved keyword is treated the other way round.

**A placement names the suppression row in force**, by page key and culture, rather than carrying flags that
describe it. Flags were the first design and they were a bug: `SuppressedAllCultures` was computed per keyword, so a
keyword with an all-languages row on *one* page reported every other page as all-languages too. The audit then
offered to lift `(thisPage, all languages)` — a row that had never existed — and the suppression could not be lifted
from the screen at all. Naming the row removes the whole class of mismatch.

Lifting picks the narrowest matching row: this page in this culture, then this page for all cultures, then every
page in this culture, then every page for all cultures. A keyword switched off both on one page and everywhere stays
suppressed after the page row goes, which is the truth rather than an action that appears to do nothing.

**Both deletes are idempotent.** Lifting a suppression or removing a keyword that is not there returns success, since
the state the caller asked for already holds. They used to return 404, which made a scope mismatch look like an error
an editor could not clear.

A suppressed match does **not** spend the keyword allowance, since nothing was linked. The audit still reports it
at most `MaxLinksPerKeyword` times per page though, tracked separately in `AutoLinkRequestState`. Without that, a
keyword suppressed on a page mentioning it five times produced five rows where linking it produces one.

### 9. Cultures

A site with two languages has two keyword sets. `hello` and `bonjour` point at the same page, but through different
URLs, and each is only meaningful in its own language. So the snapshot is **one `CultureKeywordSet` per configured
language, plus one invariant set**, each with its own targets, suppressions and matcher.
`KeywordSnapshot.For(culture)` picks one, falling back to the invariant set.

The renderer takes the culture from `IVariationContextAccessor.VariationContext.Culture`, which is what Umbraco sets
from the request, with `PublishedRequest.Culture` as a fallback. The scan iterates `IPublishedContent.Cultures` and
sets the variation context per culture before reading values, because rich text nested in a block has no per-call
culture argument to pass.

Both tables carry a `culture` column. The same word in `en-US` is a separate keyword from the one in `en-GB`, so rows
are keyed per culture, with an **empty culture meaning every culture** — which is what a keyword added before the site
went multilingual meant. Culture-specific rows win over all-culture ones.

An all-culture row is resolved separately for each language, so one row pointing at a page gives an `/us/` URL in
`en-US` and a bare one in `en-GB` without anybody duplicating it. Pages not published in the culture being rendered
fall out for free: `GetUrl` returns `#` and the routability check drops them, so an `en-GB` page never links to a
target that only exists in `en-US` — it reports as unresolved in that language instead.

#### The trap

**Umbraco's migration layer refuses `ALTER TABLE` on SQLite outright** — the exception says so in as many words — so
`Alter.Table().AddColumn()` cannot add the culture column. `AddCultureToDecisions` reads the rows out, drops the table,
recreates it from the DTO (which brings the new column and rebuilt unique index with it) and puts the rows back.
Portable across providers, and lossless.

> The other trap here used to be the one that mattered most: `ITagQuery.GetContentByTagGroup(group)` with no culture
> argument returns **nothing** once the keyword property varies by culture, so every tag-driven link on the site
> vanished silently the day it was set to vary. It cost most of a day to find, it is why the snapshot is per culture
> at all, and it no longer applies — the tags query is gone. Recorded because the shape of the failure is worth
> remembering: nothing errored, and the property went on showing the keywords in the content editor the whole time.

### 10. External links

A keyword can point at a URL outside the site. This is not a second feature bolted alongside the mapping table: it
is the **same row**. `initialsAutoLinkKeywordMapping` gained a nullable `externalUrl` beside the nullable `targetKey`,
exactly one of which is meaningful, because "somebody chose where this keyword goes" is one concept whether the
destination is a node or a URL.

Everything downstream therefore needed no special casing:

- **Precedence** needed nothing: the row is the destination either way.
- **Cultures** come free from the column already on the row, so an external link can be per language or for all of
  them, and a keyword can point at different URLs in different languages.
- **Suppression, the audit, mention accounting and the caps** all work untouched, because they operate on resolved
  targets and never asked what kind of target it was.
- **Hand-link detection** compares the target URL against anchors the editor wrote, which works on absolute URLs
  as-is: if somebody already linked to that URL in the paragraph, the linker stands down.
- **Self-linking** stops applying, since there is no node identity to compare. Nothing needed changing; an external
  target carries an empty key, which never matches a real page.

What was genuinely new at the time is that **the dashboard creates keywords** rather than only resolving them. Every
other keyword originated from a tag; an external link had no page to tag, so it had to be typed into the screen. That
turned out to be the interesting half: once the screen could create a keyword, the tags had nothing left to do. See
[Keywords moved to the dashboard](#11-keywords-moved-to-the-dashboard).

#### Validation is a security boundary here

Every URL the linker emitted before this came from Umbraco resolving a node. An external link is the first
editor-supplied string to reach an `href`, which makes `javascript:` and `data:` an XSS vector rather than a
formatting problem. `ExternalUrl.TryNormalise` requires an absolute http or https URL, and is enforced twice: once
when the API accepts the row, and again when the registry builds it, so a row written by any other route still
cannot render a hostile scheme.

#### Markup

```html
<a href="https://umbraco.com" data-autolink="true" title="umbraco.com"
   data-autolink-external="true" rel="nofollow">Umbraco</a>
```

`rel="nofollow"` by default, since a wall of auto-generated outbound links reads like a link scheme; configurable
globally with `ExternalLinkRel` and overridable per link for a domain worth passing authority to. The external
marker is a **second attribute** rather than a different value for `data-autolink`, so anything already keying on the
original attribute keeps working. No `target="_blank"`: that is the visitor's choice to make.

### 11. Keywords moved to the dashboard

The `linkKeywords` Tags property is gone. Keywords are created on the Auto-linking screen, and the destination is
Umbraco's **Multi URL Picker** — `umb-input-multi-url`, the input behind `Umbraco.MultiUrlPicker` — capped at one
item, which is how core itself does a single-link picker.

The argument for it is the one external links exposed. A tag says "this page answers to this phrase", which reads
well until you want a synonym, a plural, a phrase whose best target carries no tag, or a destination that is not a
page at all. Every one of those wanted a row in the table instead, so the table was already the real answer and the
tags were a second way in that could disagree with it. Now there is one place a keyword can come from, and picking a
page and typing a URL are the same act performed in the same control.

What that deleted:

| Gone | Why it could go |
|---|---|
| `ITagQuery` lookup, `CollectCandidates` | Rows are the only source |
| `KeywordCandidate`, `KeywordConflict` | A unique `(keywordKey, culture)` index cannot hold two destinations |
| `AutoLinkSkipReason.Contested`, `KeywordSource.Tag` | Nothing can be contested, and nothing comes from a tag |
| Conflict reservation in `KeywordMatcher` | No keyword resolves to nothing on purpose any more |
| `TagGroup`, `KeywordsPropertyAlias` config | Nothing reads a tag group or a property alias |
| The Tags datatype installer | Only the scan opt-out property is installed now |
| The dashboard's "choose one of these pages" panel | Replaced by "change destination" on every row |

`excludeFromAutoLinking` **stayed on the document type**, which is the one thing here that genuinely belongs on a
page: it says "do not scan *this page's* copy", which is a property of the page and not of any keyword.

Three consequences worth being explicit about:

- **Creating a target is now two steps, not one.** Tagging a page made it a target in the same save. Now you publish
  the page and add the keyword on the screen. Retroactivity is untouched — an article written months ago picks the
  link up on its next render, which was always the point — but the work moved from the content editor to one central
  screen, which is the trade being made deliberately.
- **A broken destination stops linking instead of falling back.** There is no tag left to fall back to, so a keyword
  whose page has gone reports as unresolved and links nothing. Those rows sort first and open themselves.
- **Teardown is more destructive than it was.** These tables used to hold decisions layered over tags; they now hold
  the keywords themselves. `DELETE /autolink/data` takes the lot.

The picker offers three things the linker has no concept of. The anchor field is hidden (`hide-anchor`). A **media**
pick is refused as it is made, with a message saying so, because a media URL is site-relative and
`ExternalUrl.TryNormalise` insists on absolute http or https on purpose — that check is a security boundary, and
loosening it so a keyword can point at a PDF is not a trade worth making here. **Open in new window** cannot be
hidden at all, on `umb-input-multi-url` or on the built-in property editor, so ticking it prints a line saying
auto-links will not use it. Dropping editor input silently is the thing being avoided in all three cases.

### 12. Relations, and warning before a linked page is deleted

Keywords made it easy to break a page without noticing: delete the page a keyword points at and every auto-link to
it silently stops, which is the flip side of decision 1 working as designed. Umbraco already has the machinery to
warn about that — it just needs telling.

`initialsAutoLinkKeyword` is a relation type with **`isDependency: true`**, created by a migration on the main plan. That
one flag is the whole feature: Umbraco's tracked references read dependency relations, so the Info tab grows a
"Referenced by" list and the delete confirmation grows **"The following items depend on this"**, naming the pages
that would lose links. No UI of ours involved.

**The direction is the part to get right, and it is the opposite of the obvious reading.** Umbraco stores a
reference with the *referencing* item as the parent: a Content Picker on page A pointing at page B is
`parent=A, child=B`, and "what uses this item" is a lookup on `childId`. Written the intuitive way round —
target as parent — the relations exist, the database looks reasonable, and the warning fires on the *mentioning*
pages instead of on the target. That is exactly backwards from the case worth warning about, and the only symptom
is an empty "Referenced by" panel on the page that has five things pointing at it. Confirmed against Clean's own
`umbDocument` rows before settling it.

Relations are reconciled from the **scan**, not written as pages render:

- Only mentions that actually became links count. One sitting in a heading, one past the per-page cap, or one
  switched off by hand renders no anchor, so recording it would make the warning claim links that are not there.
- External links are skipped — there is no node to relate to.
- Pairs are deduplicated across cultures, since `umbracoRelation` has no culture column.
- The reconcile is a set difference, so it is self-healing: rows that no longer hold are deleted, missing ones
  added. That is what repaired the whole table after the direction was corrected.

It runs off the back of `GET /scan`, which the dashboard calls on load. A read with a write behind it is not
lovely, and the honest trade is that the scan is the only thing that knows the answer and already walks every
published page; the alternative is relations that go stale and a delete warning that lies. `POST /relations`
does the same thing explicitly, for a scheduled job.

**Deleting the page clears the relations, and Umbraco does that part itself.** It removes a node's relations
during the delete, before `ContentDeletedNotification` fires — verified on 17.6.1, and not a database cascade:
the foreign keys on `umbracoRelation` carry no `ON DELETE CASCADE`. The handler here is a backstop that finds
nothing in the normal path, kept because a relation surviving by some other route would show up as a phantom
dependency on whatever takes that node id next.

One gap the relations cannot cover: a keyword pointing at a page **nothing currently mentions** has no relation
behind it, so there is nothing for Umbraco to warn about. `AutoLinkRelationHandler` adds an `EventMessage` naming
the keywords in that case. Whether the v14+ backoffice surfaces event messages from a package notification handler
is **not verified** — the log warning it writes alongside definitely lands, and the dashboard flags the keyword as
unresolved afterwards either way.

Teardown drops the relation type and its relations along with the tables.

---

## Verified behaviour

From the Clean site, `/blog/meetups/`. Runs marked **[tag era]** were taken when keywords came from a `linkKeywords`
Tags property; the linking behaviour they demonstrate is unchanged, but "tag page X" is now "add a keyword on the
screen pointing at page X", and the collision run describes a state the table can no longer reach.

**Skips hand-authored links and headings.** The article contains `Umbraco Leeds` in two places:

```html
<!-- untouched: inside both an <a> and an <h2> -->
<h2><a href="https://www.meetup.com/umbLeeds/">Umbraco Leeds Meetup</a></h2>

<!-- linked: plain paragraph text -->
<p><a href="/blog/community/" data-autolink="true" title="Community">Umbracians</a> is a monthly ...</p>
```

**Retroactivity (Spike 2).** [tag era] With no keywords registered the article renders plain. Tagging *other* pages
and publishing them makes links appear on the next render of the article, which was never opened or saved:

```
before        stamp=empty                    0 auto-links
tag Features   "content editor"              stamp=96e9fcb0f0f6172d
tag Community  "Umbracians"                  stamp=1a9138949fd517fe
after         (article untouched)            <a href="/features/" data-autolink="true">content editor</a>
                                             <a href="/blog/community/" data-autolink="true">Umbracians</a>
```

**No dead anchors.** Unpublishing `Features` removes `content editor` from the registry, and the link on the
untouched article *heals to the next best match* rather than leaving a broken `<a>`:

```
before unpublish   content editor -> /features/
after  unpublish   editor         -> /about/      (shorter keyword now wins)
```

**Self-linking suppressed.** `Meetups` is tagged `meetup, meetups`; its own page links neither, while
`/blog/community/` links `meetups` to it.

**No stemming.** `\bmeetup\b` correctly refuses to match inside "meetup**s**". Plurals need their own keyword.

**Collisions (Spike 3).** [tag era, no longer reachable] `Features` and `Community` both tagged `content editor`,
driven from the backoffice screen and confirmed on the front end. One row per keyword per culture now makes the
contested state impossible to create, which is what retired this whole path:

```
both tagged            content editor      conflict, not linked   stamp=506963caf4bfc145
                                           the phrase still renders as plain text
                                           Umbracians still resolves - one bad keyword poisons nothing
                                           and "editor" does not hijack the phrase either
click Features         content editor  ->  /features/   manual     stamp=d8c9a2a5949b6299
pick About (untagged)  content editor  ->  /about/      manual     stamp=7b713b70a640f277
Clear                  content editor      conflict, not linked   stamp=506963caf4bfc145
```

**Every mention accounted for.** Three mentions of `Harrie` across two en-GB pages previously produced a single
report row:

```
[en-GB] /aboot/     (About)     Harrie -> About   self     <- the target page itself
[en-GB] /feetures/  (Features)  Harrie -> About   linked
[en-GB] /feetures/  (Features)  harrie -> About   limit    <- second mention on the page
```

Only the middle row used to show. The other two were correct behaviour, reported as nothing at all.

**Invariant pages on a multilingual site.** Blog posts do not vary by culture on this site, so they were scanned
against the empty invariant keyword set and were invisible. Scanning them per language took the report from 2 rows to
10, and the same page now correctly reports a different target per language:

```
[en-GB] /blog/join-the-umbraco-community-on-mastodon/     harrie -> About     /aboot/
[en-US] /us/blog/join-the-umbraco-community-on-mastodon/  harrie -> Features  /us/features/
```

Both verified against the rendered HTML, which carries exactly those anchors. One page, two languages, two
destinations.

The stamp returning to its exact earlier value on Clear is the content hash doing its job. Every one of those
render changes appeared on an article that was never opened or republished, and the stored row recorded
`set by admin@example.com`, taken from the token claims.

**Audit and suppression (Spike 4).** A scan of the Clean site, 25 published pages:

```
scan            25 scanned, 0 opted out, 5 pages carrying links, 34 ms
                /blog/community/            meetups        -> Meetups
                /blog/meetups/              Umbracians     -> Community
                /blog/meetups/              content editor -> Features
                /blog/podcasts-and-videos/  conference     -> Conferences
                /blog/popular-blogs/        conference     -> Conferences
                /blog/popular-blogs/        Umbracians     -> Community
                /blog/youtube-tutorials/    conference     -> Conferences

unlink conference on /blog/popular-blogs/ only
                that page      Umbracians only, conference now plain text
                other pages    conference still linked
unlink Umbracians everywhere
                every page     reported "off everywhere", no anchors rendered
allow both      stamp back to f860a91460904b0d, the pre-suppression value
```

Three of those pages were ones nobody had thought to check, which is the argument for the scan being complete
rather than driven by what has been visited. Every report row was verified against the rendered HTML.

**Cultures (Spike 5).** [tag era] Two languages, `en-US` default and `en-GB`, with `linkKeywords` set to vary by
culture and one keyword tagged in `en-US` only. Per-culture rows do the same job now, without depending on a property
varying:

```
tag store        en-US: harrie          en-GB: (none)
culture query    en-US: About, Features at /us/about/, /us/features/
NO-CULTURE query nothing at all                       <- what the registry used to call
registry         invariant: 0    en-GB: 0    en-US: harrie contested by two pages
map harrie -> Features, culture en-US
registry         en-US: harrie -> /us/features/ (manual)      en-GB: still empty
scan             25 scanned, 1 row: [en-US] /us/about/ harrie -> Features
                 reported off for "this page, all languages", matching the stored suppression exactly
```

The decision applied to `en-US` and did not leak into `en-GB`, and URLs resolved through the `/us/` prefix
throughout. Fully exercising `hello` versus `bonjour` needs an `en-GB` variant published with its own keyword, which
is a content job rather than a code one.

**Relations and the delete warning.** Against the Clean site, after a scan:

```
reconcile        5 added, 0 removed          Community <- Feetures, Popular blogs, Podcasts and
                                             Videos, YouTube Tutorials, Join the Umbraco Community
Info tab         "Referenced by" lists all five
Trash Community  "The following items depend on this" + the five pages   <- cancelled, not deleted

duplicate a mentioning page, publish it
reconcile        1 added, 0 removed          6 rows, the copy now references Community
delete the copy  relation gone, 5 rows       Umbraco cleared it during the delete; our handler found none
```

The first run of this had the direction inverted, which the database showed immediately: five rows reading
`Community -> Feetures` and a "Referenced by" panel saying "This item has no references." Corrected, the reconcile
removed all five and rewrote them the other way round with no intervention.

**External links (Spike 6).** `Umbraco` pointed at `https://umbraco.com` in en-GB only:

```
javascript:alert(1)   rejected, HTTP 400
/relative/path        rejected, HTTP 400
https://umbraco.com   accepted -> External, rel=nofollow
en-GB  /blog/popular-blogs/     <a href="https://umbraco.com" ... rel="nofollow">Umbraco</a>
en-US  /us/blog/popular-blogs/  not linked, the row is en-GB only
scan   linked on 9 en-GB pages, second mentions reported as "limit"
```

---

## Performance

Measured on `/blog/meetups/` (~8 rich text blocks), 150 requests over one connection after warm-up,
Development configuration:

| | ms/request |
|---|---|
| Auto-linking off | 6.57 |
| Auto-linking on | 7.69 |
| **Overhead** | **1.12 ms (17%)** |

That 1.12 ms covers both the AngleSharp pass *and* the loss of property caching, since the wrapper reports
`PropertyCacheLevel.None`. Default `Elements` caching would serve stale markup after another page's keywords
changed, so `None` is the honest setting.

**Conclusion: the cache layer is premature.** As the design doc suspected, AngleSharp on article-length markup
is fast enough that a stamp-keyed `IAppPolicyCache` is not worth the complexity yet.

---

## Configuration

```json
{
  "Initials": {
    "AutoLink": {
      "Enabled": true,
      "ExcludePropertyAlias": "excludeFromAutoLinking",
      "ExternalLinkRel": "nofollow",
      "MaxLinksPerKeyword": 1,
      "MaxLinksPerPage": 25,
      "InstallSchema": true,
      "InstallOnDocumentTypes": [ "article", "content", "home", "category", "author" ]
    }
  }
}
```

**Nothing here needs configuring to make linking work.** Keywords are added on the screen, so there is no tag group
to match and no property alias to get right — which retired the one setup trap in this package that failed silently.
It used to be possible to bind `linkKeywords` to the stock Tags datatype (group `default`) while the configured
`TagGroup` said `autolink`: every save worked, wrote real relations, showed the keywords in the content editor, and
the registry saw nothing at all.

Bound through `IOptionsMonitor`, so edits apply **without a restart**.

### Schema install

`InstallAutoLinkSchema` adds `excludeFromAutoLinking` to the nominated document types, and nothing else. Idempotent
and additive: an existing property is left alone, and nothing is ever renamed, moved or deleted. It is optional in
every sense — without it, every page stays scannable.

**One migration does delete something, and it is the exception that proves the rule.**
`RemoveLegacyKeywordProperty` takes away the `linkKeywords` Tags property and the `Auto-link Keywords` datatype an
earlier version installed. Keywords live in the package's own table now, so that property reads exactly like the
one that used to work while doing nothing at all — and a field that silently does nothing is worse than no field.

It is scoped by **datatype, not by configuration**: the property goes only where it is bound to the datatype this
package created, so a `linkKeywords` somebody rebound to a Tags datatype of their own is left alone, and the
datatype itself is deleted only once nothing points at it. If the datatype is absent the migration does nothing,
which covers hand-built installs and every subsequent boot. It sits on the main plan rather than the schema one,
because a site that installed the property and later turned `InstallSchema` off still needs it taken away.

Removing the properties takes their stored values with them. It does **not** clear `cmsTags`: the rows in the tag
group survive with no relationships pointing at them, which is ordinary Umbraco behaviour — core has no notion of
collecting unused tags — and they are inert, since nothing queries that group any more.

It is a **migration**, in its own `Initials.AutoLink.Schema` plan, so it runs **once** rather than at every startup. That
was the last startup-fixup left in the package, and it was the wrong shape for a shipping one: editing somebody's
document types every time their site boots is nobody's expectation.

Two consequences worth knowing, both falling out of "once":

- **The plan is not executed until the feature is configured.** A plan step is spent for good, so consuming it on an
  install that nominated nothing would mean a site that configured `InstallSchema` afterwards never got the schema.
  `AutoLinkMigrationHandler` only runs this plan when `InstallSchema` is true *and* `InstallOnDocumentTypes` is
  non-empty.
- **A nominated document type that does not exist throws**, rather than being skipped. That leaves the plan state
  where it was, so the next boot tries again. An unattended install imports its starter kit around the same runtime
  the migration hooks, so the document types can genuinely arrive after the first attempt — and a one-shot that
  quietly gave up on them would leave a site that looks installed and links nothing. A wrong alias in configuration
  lands in the same place, and says so in the log once per boot until somebody fixes it.

Once the plan has run, a document type nominated *later* is not retro-fitted. Add the property to it in the
backoffice, the same way you would any other.

> **First run only:** because the site uses `InMemoryAuto` models, changing content types regenerates the models
> under already-compiled views, and the first page load after the schema install fails with
> `ModelBindingException: ... application is in an unstable state and should be restarted`. Restart once and it is
> gone. As a migration this can now only happen on the single boot that installs the schema, rather than being a
> thing every boot could do.

---

## Known limitations

- **No stemming or plurals.** Each surface form needs its own keyword.
- **Hand-link detection is per property**, so a manual link in a *later* Block List item is not seen when an
  earlier one is converted. Blocks convert in document order.
- **Delivery API output is not linked**, only delegated. Deliberate, out of scope.
- **CDN.** Render-time healing stops at the edge. Either wire a purge into the stamp bump or accept that
  retroactive links appear on natural TTL expiry.
- **The API is gated on section access, not on a per-keyword permission.** Anybody who can see the section can
  point any keyword anywhere, or delete it.
- **A keyword cannot be renamed.** The keyword is the row's identity, and suppressions are keyed on it, so renaming
  would orphan them. Remove it and add the new spelling.
- **A keyword is removed for the culture it was created in**, not the culture being viewed, so an all-languages
  keyword can be removed while looking at a single language.
- **A keyword added from the screen is always culture-specific** when the site has languages, since the screen adds
  it to the language being viewed. All-culture rows exist for single-language sites and for keywords added before a
  site went multilingual; making one deliberately on a multilingual site means writing the row through the API with
  an empty culture.
- **Media is not a destination.** The picker offers it, and it is refused: a media URL is site-relative and the URL
  validation deliberately insists on absolute http or https.
- **"Open in new window" is ignored.** The picker offers it and cannot be told not to; the screen says it will not
  be used.
- **Segments are not handled.** `VariationContext` carries a segment as well as a culture; only culture is used.
- **Stored decision cultures are lower-cased**, since they are index keys compared case-insensitively. The screen
  shows culture codes from the registry, not from the stored row, so `en-US` still displays properly.
- **The scan says what would happen now, not what was served.** If you need "which pages carried this link last
  month", that needs observations written down as pages render. The relations are a snapshot of the last scan, not
  a history.
- **Relations are only as fresh as the last scan.** They update when somebody opens the dashboard or calls
  `POST /relations`. A page published since then can carry a link that no relation records yet, so the delete
  warning can undercount.
- **A keyword whose target nothing mentions raises no dependency warning**, because there is no rendered link and
  so no relation. The notification handler still logs it.
- **The scan is synchronous.** 34 ms for 25 pages extrapolates fine to a few hundred, but a site with thousands of
  pages wants it backgrounded with progress rather than held open on one request.
- **A skipped mention is reported once per keyword per reason**, not once per occurrence, so a page mentioning a
  keyword ten times shows one "only the first mention is linked" row rather than nine.
- **Suppression is per keyword per page, not per occurrence.** With `MaxLinksPerKeyword: 1` that distinction rarely
  matters, since only the first mention is linked anyway.
- **A keyword pointing at an unpublished page reports as unresolved**, not as "publish this". The registry reads the
  published cache, so a target that exists only as a draft is indistinguishable from one that was deleted.

---

## Production readiness

Fixed, with the reasoning worth keeping:

- **Invalidation hooks the cache refresher, not ContentPublished.** `ContentPublishedNotification` fires inside the
  publish, before the published cache settles, so a render at that moment could rebuild from stale content and mark
  itself clean — stale until the next content change. It also only fired on the publishing node.
  `ContentCacheRefresherNotification` fires after the cache updates and on every node, so one hook replaced five and
  fixed cross-node invalidation at the same time.
- **Decision writes go through the distributed cache.** The mapping and suppression tables are ours, so no Umbraco
  refresher carries them: a decision saved on one node left every other node serving old links. There is now a
  registered `ICacheRefresher` and writes call `DistributedCache.RefreshAll`.
- **Migrations use `AsyncMigrationBase`.** `MigrationBase` is obsolete and scheduled for removal in Umbraco 18.
- **AngleSharp 1.7.1**, clearing GHSA-pgww-w46g-26qg. It runs on every page render, so an advisory there is not
  something to carry.
- **The schema install is opt-in, nominates nothing, and runs once.** `InstallSchema` defaults to false and
  `InstallOnDocumentTypes` to empty. Guessing alias names was fine for a spike; a package editing document types at
  every boot, on types nobody nominated, is not. It is now a migration in its own plan, executed only once the
  feature is configured so that configuring it later still works. It also has far less to do than it did: one
  optional boolean, now that keywords are not a property.
- **Both database providers, verified rather than assumed.** A clean install was run end to end on SQLite and on
  SQL Server: both plans apply, both tables come out with the right column types (`uniqueidentifier`, `bit`,
  `nvarchar` lengths), and the second boot does no work on either. The three places a provider difference could
  bite are handled by design, not by luck — `keywordKey` is lower-cased so uniqueness behaves the same under SQL
  Server's case-insensitive collation as under SQLite's case-sensitive one; `pageKey` uses `Guid.Empty` rather than
  `NULL` because a unique index treats two nulls as equal on one and distinct on the other; and both index keys stay
  well under SQL Server's 900-byte limit, which raising `[Length]` on `keywordKey` past ~430 would breach on SQL
  Server alone. The one raw statement in the package, `DROP TABLE IF EXISTS` in the teardown, needs SQL Server 2016,
  which is Umbraco 17's documented minimum.
- **41 tests**, covering what must not silently regress: word boundaries, longest-keyword-first, skipped elements,
  never nesting an anchor, not rewriting attributes, the per-keyword cap, hand-link detection, self-linking, external
  markup and rel, suppressed keywords reserving their span, culture fallback, row precedence, and the narrowest
  suppression row winning. Verified by mutation: breaking longest-first fails a test.
- **Warnings are errors** in the package, and CI fails the build if the dashboard or the backoffice manifest stops
  being packed.

### Removing the package

Umbraco has no uninstall hook for a package delivered over NuGet: removing the reference removes the assembly and
leaves the tables. So teardown is an explicit call.

```
DELETE /umbraco/management/api/v1/autolink/data?confirm=remove-autolink-data
```

It drops both decision tables and **resets the migration state**, which is the part that matters. Dropping the tables
while `umbracoKeyValue` still records the plan as complete means a reinstall never recreates them, and the package
comes back up permanently broken with stores that log and return empty.

**It takes every keyword with it**, which it did not used to. These tables once held decisions layered over tags, so a
teardown lost the decisions and left the keywords in the content; they are now the only place keywords exist. There is
no other copy.

Document types are left alone on purpose, so `excludeFromAutoLinking` and any values in it survive. Delete the
property yourself if you want it gone.

Deliberately not a button in the dashboard: a destructive action one click from the screen editors use every day is a
mistake waiting to happen. The confirmation token exists for the same reason.

**It needs an administrator, not just section access.** Every other endpoint here is gated on access to the
Auto-linking section, which is the permission an editor curating keywords holds — not the permission to drop both
tables. So teardown carries a second policy requiring the admin group, and both apply: the administrator running
it needs the section granted too, which is the same tick that let them open the dashboard. The token stops a mistake;
it was never authorization.

### Accessibility

The keyword list has **list semantics, not table roles**. It looks like a table, but each row contains its own detail
panel, and no table role permits that — `role="row"` with a non-cell child is invalid and screen readers handle it
unpredictably. A list of keywords with expandable regions is what it actually is.

Each toggle has a real name (`Show detail for Harrie`, not an unlabelled glyph), `aria-controls` pointing at its
panel, and the panel is a labelled region. The chevron and the external arrow are `aria-hidden`, so the pill reads
"external" rather than "external north east arrow". The language switcher is a labelled group with `aria-pressed`,
the header row is hidden from assistive tech because column labels mean nothing in a list, and the one custom control
has a visible focus ring.

None of it has been through an actual screen reader. Sound structure, unverified behaviour.

### Localisation

Every string the screen shows lives in `wwwroot/lang/en.js`, and the element asks for terms through
`this.localize.term('initialsAutoLink_alias')`. Section and dashboard names use the `#initialsAutoLink_alias` convention in the
manifest, the same way core does.

To add a language: copy the file, translate the values, and register it with its own culture. Nothing else changes.

Two things caught me out, both worth knowing before writing one of these:

- **A term that takes values is a function, not a token string.** `%0%` substitution is the pre-v14 convention and
  renders literally now. Terms are `(count) => count === 1 ? '1 keyword' : \`${count} keywords\``, which is also why
  pluralisation belongs in the language file rather than being assembled from fragments in the element.
- **There is no fallback from a specific culture to its base.** A dictionary registered only for `en-gb` never loads
  for a backoffice user set to English (United States), and nothing errors — the terms simply do not resolve. Core
  ships both `en.js` and `en-us.js` for this reason. This one is registered for `en`, `en-gb` and `en-us`, all
  pointing at the same file.

The skip reasons were rewritten while doing this. They used to trail off from whatever preceded them — "also
mentioned here, sits in a heading or an existing link" left the reader working out what *sits* anywhere. Each one now
names its own subject, so it reads whether it follows "Not linked:" or "Another mention on this page."

### Still outstanding

| Item | Why it is not done |
|---|---|
| Consumer documentation | This README is a build log. A shipping package needs a shorter one aimed at somebody installing it. |
| Delivery API | Delegated but not linked. Decide whether to support it or document it as out of scope. |
| Accessibility, verified | The structure is sound, but nothing has been through a screen reader. |
| Segments | `VariationContext` carries a segment as well as a culture; only culture is used. |
| Narrowing what invalidates the registry | See below. Needs verifying against a real publish before it can be trusted. |

#### Invalidation is wider than it looks

`ContentCacheRefresherNotification` fires for **plain draft saves**, not just publishes: `ContentService.Save`
raises a tree change, Umbraco's own handler turns that into `RefreshContentCache`, and that raises this
notification. So saving a draft of any page marks the registry stale, and the next render pays for a rebuild whose
content hash almost always comes out identical — the stamp does not move, so cached output survives, but the tags
query and the URL resolution are done again for nothing.

The payload (`ContentCacheRefresher.JsonPayload[]`) carries `ChangeTypes`, `PublishedCultures` and
`UnpublishedCultures`, so the handler could invalidate only on a publish-state change. It is not done yet because
getting it wrong fails in the direction that matters: a publish that does not invalidate is a keyword that never
starts linking, which is the entire feature. This site has already been bitten twice by invariant content taking a
different path from varying content, so the filter wants verifying against both before it ships — not reasoning
about.

---

## Deliberately not built

Automate actions, Tiptap editor decoration, orphan keyword digest, Delivery API support, the stamp-keyed cache
layer. All known territory; they add days and prove nothing the spikes did not.

The relations audit trail *was* on this list, on the grounds that the dry-run scan answers "which pages carry
links" without storing anything. It got built anyway for a different reason: not to answer that question, but so
Umbraco can warn before somebody deletes a page other pages link to. See section 12.
