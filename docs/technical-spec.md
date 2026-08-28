# Initials.AutoLink technical specification

A developer's map of the package: what each piece does, how a request flows through it, what is
stored where, and which behaviours are load bearing.

Written against **Umbraco 17.6.1 / .NET 10**. Companion documents: `docs/build-log.md` is
the build log with evidence and measurements, and `CLAUDE.md` holds the design decisions with the
rejected alternatives.

---

## 1. What it does

A keyword is a phrase plus a destination. When any published page's rich text contains that phrase,
the phrase is rendered as a link to that destination.

The transformation happens **as the page is served**, never at publish time. Stored rich text is
never modified. Two consequences follow from that and everything else is downstream of them:

1. A page written before the destination existed picks the link up on its next render, with nobody
   editing or republishing it.
2. Nothing needs cleaning up when a destination goes away. The link simply stops appearing.

Keywords are managed in a custom backoffice section. There is no property on any document type
that holds them.

---

## 2. Component map

```
Request                                     Build / edit
-------                                     ------------
AutoLinkRichTextValueConverter              KeywordMappingController      (API)
  wraps Umbraco's RTE converter               AutoLinkReportController    (API)
        |                                     AutoLinkDataController      (API)
        v                                             |
    IAutoLinker  (AutoLinker)                          v
        |  reads                              IKeywordMappingStore
        v                                     IKeywordSuppressionStore
  IKeywordRegistry ---------------------------------> (SQL tables)
        |  holds                                      |
        v                                             | invalidate
   KeywordSnapshot                            IKeywordRegistryInvalidator
     CultureKeywordSet per culture                    |
       Targets, Suppressions, Matcher                 v
                                              AutoLinkCacheRefresher
                                                (distributed cache)
Audit / relations
-----------------
IAutoLinkScanner (AutoLinkScanner) -> AutoLinkScanReport
        |                                   |
        | uses IAutoLinker.Preview          v
        |                          IAutoLinkRelationWriter -> umbracoRelation
        v
   AutoLinkRelationHandler (delete warnings + cleanup)
```

### Namespaces at a glance

| Namespace | Responsibility |
|---|---|
| `PropertyEditors` | The value converter that hooks the render pipeline |
| `Linking` | The actual HTML rewriting and per-request budget |
| `Registry` | The in-memory keyword set, its matcher and its content stamp |
| `Persistence` | The two tables, their DTOs, stores and migrations |
| `Relations` | Umbraco relation writing, for delete warnings |
| `Scanning` | Dry-run audit over published content |
| `Api` | Management API controllers, auth policies, Swagger |
| `Notifications` | Invalidation, migrations at startup, delete handling |
| `Install` | The optional document type schema, and legacy cleanup |
| `Caching` | Distributed invalidation |
| `Uninstall` | Teardown |
| `wwwroot` | Backoffice manifest, dashboard element, localisation |

---

## 3. Render path

This is the hot path. It runs for every rich text property on every page view.

### 3.1 Getting into the pipeline

`AutoLinkRichTextValueConverter` implements `IPropertyValueConverter` and
`IDeliveryApiPropertyValueConverter`. It **wraps** `RteBlockRenderingValueConverter` rather than
inheriting from it, taking one as a constructor dependency and delegating every member.

The reason is that the inner type has a sixteen parameter constructor, next to an already
`[Obsolete]` fourteen parameter one. Inheriting means re-declaring all sixteen and breaking when a
seventeenth appears. Wrapping means none of them are named in this codebase.

Registered in `AutoLinkComposer`:

```csharp
builder.Services.AddTransient<RteBlockRenderingValueConverter>();
builder.PropertyValueConverters()
       .Replace<RteBlockRenderingValueConverter, AutoLinkRichTextValueConverter>();
```

`Replace` removes the built-in converter from the collection **and its DI registration**, hence the
explicit `AddTransient`. Without it the wrapper cannot resolve what it wraps.

Only `ConvertIntermediateToObject` does anything of ours: it takes the `IHtmlEncodedString` the
inner converter produced, passes the markup through `IAutoLinker.ProcessMarkup`, and re-wraps only
if the string changed. Everything else, including the whole Delivery API surface, delegates
untouched. Delegating the Delivery API rather than omitting it matters: not implementing the
interface would silently regress it to unconverted output rather than leaving it alone.

Sitting at this layer is what makes rich text nested inside Block List and Block Grid work with no
view changes. Each nested rich text is an ordinary published property converting the same way.

### 3.2 Cache level

```csharp
public PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) =>
    _options.CurrentValue.Enabled ? PropertyCacheLevel.None : _inner.GetPropertyCacheLevel(propertyType);
```

`None` is deliberate. Output depends on the **whole** keyword set, so the default `Elements` level
would serve stale markup after an unrelated page's keywords changed. The measured cost of this plus
the AngleSharp pass is about 1.1 ms per request on a page with eight rich text blocks, which is why
there is no stamp-keyed cache layer yet.

### 3.3 AutoLinker.ProcessMarkup

Order of operations, and each step exists to avoid work:

1. Bail if the markup is blank, or if a scan is in progress (see 3.5).
2. Pick the culture's keyword set from the registry via `IVariationContextAccessor`, falling back
   to `IUmbracoContext.PublishedRequest.Culture`.
3. Bail if that set is empty.
4. **Cheap gate:** `set.Matcher.IsMatch(markup)` over the raw string. Most markup contains no
   keyword, and a regex scan is far cheaper than a parse. A false positive inside an attribute
   costs only a wasted parse.
5. Resolve the current page and bail if it carries `excludeFromAutoLinking`.
6. Bail if the request has already hit `MaxLinksPerPage`.
7. Rewrite. Any exception is caught and logged, returning the original markup, so a markup edge
   case cannot take a page down.

### 3.4 The rewrite itself

`Rewrite` parses into a detached `div` so `InnerHtml` round trips cleanly, then walks
`Descendants<IText>()`. Never a regex over raw HTML, which would rewrite `href` values and nest
anchors inside anchors.

Per text node, `RewriteTextNode` runs the matcher and for each match applies these gates in this
order. **The order is load bearing.**

| Check | Skip reason | Why here |
|---|---|---|
| No resolved target | none, `continue` | Unreachable: the matcher is built from resolved keywords only |
| Inside a skipped element | `skipped-element` | Headings, anchors, code |
| Page budget spent | `limit` | |
| Target is the current page | `self` | Never link a page to itself |
| Editor already linked this URL here | `hand-linked` | Hand-authored links win |
| Suppressed | `suppressed` | Does **not** spend the keyword allowance |
| Keyword allowance spent | `limit` | Checked **last** so a rejected candidate does not burn it |

A placed link looks like:

```html
<a href="/blog/community/" data-autolink="true" title="Community">Umbracians</a>
```

External adds `data-autolink-external="true"` and `rel` (see 6.3). The anchor text is
`match.Value`, not the registry keyword, so the editor's original casing survives.

`data-autolink` exists so the output is auditable and strippable wholesale if search engines ever
object.

### 3.5 Per-request state and the scan flag

`AutoLinkRequestState` rides on `IRequestCache` under a fixed key. It has to be request scoped, not
property scoped, because "first occurrence per page" spans many properties: every rich text block
in a Block List is its own property conversion.

It keeps two tallies:

- `CountFor` / `Record` for links actually placed, governing `MaxLinksPerKeyword` and
  `MaxLinksPerPage`.
- `ReportsFor` / `RecordReport` keyed on keyword **plus reason**, used only by the audit. Separate
  because a mention that was not linked must not spend the linking allowance, but the audit still
  needs a cap or a keyword mentioned five times produces five identical rows.

Outside a request the state is per call, which is what tests and background renders get.

`IAutoLinker.Suppress()` returns a scope that switches linking off for the current async flow. It
is `AsyncLocal`, not a field, so a scan cannot switch linking off for front-end requests being
served concurrently. The scan needs it because reading a converted property value *is* what runs
the converter, so without it a scan would both double-link and spend the budget before previewing.

---

## 4. The registry

`KeywordRegistry` is a singleton holding a `KeywordSnapshot`. It rebuilds lazily behind a `Lock`
when a `volatile bool _dirty` is set.

### 4.1 Build

The stores and Umbraco's services are **scoped**, so the singleton resolves them from a fresh
`IServiceScope` per rebuild rather than holding one.

```
mappingStore.GetAll()          all keyword rows
suppressionStore.GetAll()      all suppression rows
languageService.GetAllAsync()  configured cultures (blocking wait is safe here)
umbracoContextFactory.EnsureUmbracoContext()   rebuilds also fire from notification handlers,
                                               so there may be no ambient context
FetchPages()                   one IContentService.GetByIds for every key any row points at
```

Then one `CultureKeywordSet` per configured culture, plus one for the invariant culture (empty
string key).

`FetchPages` is a deliberate optimisation. Names used to arrive free with the tags query; without
it, a name would be a database round trip per keyword per culture. One `GetByIds` per rebuild
replaces that, and the per-culture name comes off `IContentBase.GetCultureName(culture)` with
`Name` as the fallback for a non-varying type.

### 4.2 Resolution

Per keyword, per culture, `Resolve` produces a `KeywordTarget` or nothing:

- **External row:** revalidate with `ExternalUrl.TryNormalise`. Valid gives
  `KeywordSource.External` with a `Rel`. Invalid logs and resolves to nothing.
- **Page row:** `IPublishedUrlProvider.GetUrl(key, UrlMode.Relative, culture)`. A URL that is
  blank or `#` is unroutable, so it resolves to nothing. Otherwise `KeywordSource.Manual`.

There is no fallback. A row that will not resolve drops out of `Targets` and the dashboard reports
it as unresolved. That is the loud failure replacing what used to be a silent fall-back to tags.

`GetUrl` taking a `Guid` directly is what keeps the build synchronous, since the published content
cache is async-only in v17.

### 4.3 The matcher

`KeywordMatcher.For(targets.Keys)` builds one compiled `Regex` alternation:

- Sorted **longest first**, so `Claude AI Sonnet` beats `Claude` where both start at the same
  position.
- Word boundaries applied **per keyword**, not around the group, because `\b` only behaves next to
  a word character. Wrapping the group would stop `C#` ever matching.
- `IgnoreCase | Compiled | CultureInvariant`.

Built from resolved keywords only. Suppressed keywords resolve, so they are in the matcher and
keep their span: switching one off cannot promote a shorter overlapping keyword onto the same
words. Unresolved keywords are absent and reserve nothing, because a broken row is a fault to fix
rather than a decision to hold ground for.

### 4.4 The stamp

`ComputeStamp` hashes every culture's resolved targets (keyword, URL, source) and suppressions into
a SHA-256, truncated to 16 hex characters, using ASCII control characters as separators.

The point is that a rebuild producing an identical hash **keeps the existing snapshot**:

```csharp
if (_snapshot is not null && string.Equals(_snapshot.Stamp, rebuilt.Stamp, StringComparison.Ordinal))
{
    _dirty = false;
    return _snapshot;     // stamp does not move, downstream caches survive
}
```

So a typo fix in body copy on a target page costs a rebuild and nothing else.

A failed rebuild is caught and returns `KeywordSnapshot.Empty`, rendering unlinked rather than
taking the site down.

---

## 5. Invalidation

| Trigger | Route | Notes |
|---|---|---|
| Content published, unpublished, deleted, saved | `ContentCacheRefresherNotification` -> `KeywordRegistryInvalidationHandler` | One hook where five content notifications used to be |
| Keyword or suppression written | store calls `IKeywordRegistryInvalidator.InvalidateEverywhere()` | `DistributedCache.RefreshAll(AutoLinkCacheRefresher.RefresherId)` |

Two reasons the cache refresher notification is used rather than `ContentPublishedNotification`:

1. `ContentPublished` fires **inside** the publish, before the published cache settles. A render at
   that moment could rebuild from stale content and then mark itself clean, staying stale until the
   next content change.
2. It only fires on the server that did the publishing. Other nodes learn through the distributed
   cache, which runs their refreshers, which raises this notification there too.

The stores invalidate themselves rather than leaving it to callers, because they are the code that
knows the rows changed.

**Known wider-than-ideal behaviour:** `ContentCacheRefresherNotification` also fires for plain
draft saves, so saving any draft marks the registry stale and the next render pays for a rebuild
whose hash almost always comes out identical. The payload carries `ChangeTypes` and could be
filtered, but getting that wrong fails in the direction that matters (a publish that does not
invalidate is a keyword that never starts linking), so it is unfiltered until verified against both
variant and invariant content.

`AutoLinkCacheRefresher.RefresherId` is a hard-coded GUID and **must not change once deployed**.

---

## 6. Data model

### 6.1 `initialsAutoLinkKeywordMapping`

The only source of keywords.

| Column | Type | Notes |
|---|---|---|
| `id` | int identity | |
| `keywordKey` | nvarchar(255) | Lower-cased. Unique index with `culture` |
| `culture` | nvarchar(20) | Empty means every culture |
| `keyword` | nvarchar(255) | Original casing, for display only |
| `targetKey` | uniqueidentifier | Page key, or `Guid.Empty` for external |
| `externalUrl` | nvarchar(2048) null | Absolute http(s), or null for a page |
| `label` | nvarchar(255) null | External anchor title, defaults to host |
| `nofollow` | bit null | Null follows configuration |
| `updateDate` | datetime | |
| `updatedBy` | nvarchar(255) null | |

Unique index `IX_..._keywordKey_culture`. Exactly one of `targetKey` and `externalUrl` is
meaningful; `KeywordMappingStore.Apply` clears whichever does not apply, so a row can never claim
both.

The keyword is stored twice on purpose. `keywordKey` is lower-cased and carries the index, so
uniqueness behaves the same on SQLite (case-sensitive text by default) as on SQL Server (usually
not). `keyword` preserves what somebody typed.

### 6.2 `initialsAutoLinkSuppression`

| Column | Type | Notes |
|---|---|---|
| `id` | int identity | |
| `keywordKey` | nvarchar(255) | Lower-cased. Unique index with `pageKey` and `culture` |
| `culture` | nvarchar(20) | Empty means every culture |
| `keyword` | nvarchar(255) | Original casing |
| `pageKey` | uniqueidentifier | `Guid.Empty` means every page |
| `createDate` | datetime | |
| `createdBy` | nvarchar(255) null | |

`Guid.Empty` rather than null for "everywhere", because a nullable column in a unique index treats
two nulls as equal on SQL Server and distinct on SQLite.

### 6.3 Precedence rules

**Which row is in force for a culture** (`KeywordMapping.InForce`, shared by the registry and the
API so the screen cannot disagree with the renderer): all-culture rows first, then culture-specific
rows overwrite them.

**Which suppression to lift** (`CultureKeywordSet.FindSuppression`), narrowest first:

1. this page, this culture
2. this page, all cultures
3. every page, this culture
4. every page, all cultures

Narrowest first so lifting makes visible progress. A keyword switched off both on one page and
everywhere stays suppressed after the page row goes, which is the truth rather than an action that
appears to do nothing.

**`rel` for an external link** (`KeywordRegistry.RelFor`): the row's `nofollow` if set, otherwise
whether `ExternalLinkRel` contains `nofollow`. Result is `ExternalLinkRel` or `"nofollow"`.

### 6.4 Cross-provider constraints

Three places a provider difference would bite, handled by design:

- `keywordKey` lower-cased, so case-insensitive collations and case-sensitive ones agree.
- `pageKey` uses `Guid.Empty` rather than `NULL`, per 6.2.
- Both index keys stay well under SQL Server's 900-byte limit. Raising `[Length]` on `keywordKey`
  past roughly 430 would breach it on SQL Server alone.

---

## 7. Migrations

Two plans, because they answer independent questions. Executed from
`AutoLinkMigrationHandler` on `UmbracoApplicationStartedNotification`, only at
`RuntimeLevel.Run`, each wrapped so a failure logs rather than stopping boot.

### `AutoLinkMigrationPlan` ("Initials.AutoLink") - every install

| Step state | Class | Does |
|---|---|---|
| `autolink-keyword-mapping-table` | `AddKeywordMappingTable` | Creates the mapping table |
| `autolink-keyword-suppression-table` | `AddKeywordSuppressionTable` | Creates the suppression table |
| `autolink-decisions-culture` | `AddCultureToDecisions` | Adds `culture` to both tables |
| `autolink-external-links` | `AddExternalLinkColumns` | Adds `externalUrl`, `label`, `nofollow` |
| `autolink-relation-type` | `AddKeywordRelationType` | Creates the `initialsAutoLinkKeyword` relation type |
| `autolink-remove-legacy-keyword-property` | `RemoveLegacyKeywordProperty` | Removes the obsolete `linkKeywords` property and datatype |

The two column-adding steps **rebuild rather than alter**. Umbraco's migration layer refuses
`ALTER TABLE` on SQLite outright, so the portable route is: read the rows out into a private DTO
describing the old shape, drop the table, `Create.Table<CurrentDto>()` (which brings the new column
and rebuilt index), insert the rows back. Lossless and provider independent.

### `AutoLinkSchemaMigrationPlan` ("Initials.AutoLink.Schema") - opt in

| Step state | Class | Does |
|---|---|---|
| `autolink-keyword-schema` | `InstallAutoLinkSchema` | Adds `excludeFromAutoLinking` to nominated document types |

Its own plan, and only executed when `InstallSchema` is true **and** `InstallOnDocumentTypes` is
non-empty. A plan step is spent for good, so consuming it on an install that nominated nothing
would mean a site configuring the feature later never got the schema.

A nominated document type that does not exist **throws**, which leaves the plan state where it was
so the next boot retries. An unattended install imports its starter kit around the same runtime
this hooks, so document types can genuinely arrive after the first attempt.

> **First run only:** on a site using `InMemoryAuto` models, changing content types regenerates
> models under already-compiled views, so the first page load after a schema change fails with
> `ModelBindingException`. Restart once.

### The one destructive migration

`RemoveLegacyKeywordProperty` is the only place the package deletes anybody's data. It is scoped by
**datatype, not configuration**: it removes `linkKeywords` only where bound to the `Auto-link
Keywords` datatype this package created, then deletes that datatype only once nothing points at it.
Absent datatype means a no-op. It sits on the main plan because a site that installed the property
and later turned `InstallSchema` off still needs it removed.

Removing the properties takes their stored values. It does **not** clear `cmsTags`; those rows
survive with no relationships, which is ordinary Umbraco behaviour.

---

## 8. Relations and delete warnings

The problem: deleting a page silently stops every link pointing at it. That is decision 1 working
as designed, but the damage is invisible at the moment somebody does it.

The approach is to register the facts with Umbraco and let its own UI do the warning.

`AutoLinkRelation.Alias` is `initialsAutoLinkKeyword`, created with `isDependency: true`. Dependency
relations are what Umbraco's tracked references read, so the delete confirmation grows "The
following items depend on this" and the Info tab grows a "Referenced by" panel, with no UI of ours.

### 8.1 Direction

**Parent is the mentioning page. Child is the target.**

This is the opposite of the intuitive reading and the single easiest thing to get wrong. Umbraco
stores a reference with the *referencing* item as the parent (a Content Picker on A pointing at B
is `parent=A, child=B`) and answers "what uses this item" by looking up `childId`. Written the
other way round the rows look reasonable, but the warning fires on the mentioning pages and the
target reports no references.

### 8.2 Reconciliation

`AutoLinkRelationWriter.Reconcile(report)` is a set difference against a scan report. The wanted
set is built from placements where:

- `SkipReason is null`, so only mentions that actually became anchors count. Recording a skipped
  mention would make the warning claim links that are not there.
- `TargetKey` is present and not `Guid.Empty`, so external links are excluded.
- Parent and child differ.
- Pairs are deduplicated across cultures, since `umbracoRelation` has no culture column.

Missing relations are added, relations no longer in the wanted set are deleted. Being a set
difference makes it self-healing, which is what repaired the whole table when the direction was
corrected.

Guid to int goes through `IIdKeyMap.GetIdForKey`, memoised per reconcile since the same pages recur
across cultures.

Triggered from `GET /scan` (which the dashboard calls on load) and from `POST /relations`. A read
with a write behind it is not lovely; the trade is that the scan is the only thing that knows the
answer and already walks every published page, and stale relations mean a delete warning that lies.

**Consequence:** relations are only as fresh as the last scan.

### 8.3 The handler

`AutoLinkRelationHandler` handles three notifications:

| Notification | Action |
|---|---|
| `ContentMovingToRecycleBinNotification` | Warn (the backoffice delete button) |
| `ContentDeletingNotification` | Warn (permanent delete, emptying the bin) |
| `ContentDeletedNotification` | `RemoveFor(content.Id)` |

The warn path reads the keyword table directly for rows whose `TargetKey` matches, which covers the
case relations cannot: a keyword whose target no page currently mentions has no rendered link and
therefore no relation, so Umbraco has nothing to warn about. It adds an `EventMessage` and logs.
**Whether the v14+ backoffice surfaces event messages from a package handler is unverified;** the
log warning definitely lands and the dashboard flags the keyword as unresolved afterwards.

It warns rather than cancels. Whether a page should exist is not this package's decision.

Cleanup takes `content.Id`, not the key, because the notification fires **after** the node is gone
and a key lookup would resolve nothing, making the cleanup a silent no-op. In practice it finds
nothing anyway: Umbraco clears a node's relations during the delete, and not via a database cascade
(the foreign keys on `umbracoRelation` carry no `ON DELETE CASCADE`). It is a documented backstop,
not the mechanism.

---

## 9. The audit scan

`AutoLinkScanner.ScanAsync` answers "which pages carry auto-links" by running the real linker in
dry-run mode. Render-time linking records nothing, so there are three possible answers and they are
not equivalent:

| Source | Exact? | Complete? | Answers |
|---|---|---|---|
| **Dry-run scan** (built) | Yes, same code path | Yes, unvisited pages included | What a visitor would get now |
| Record as pages render | Yes | Only visited pages | What was actually served |
| Examine | No, blind to headings, hand-links, caps | Yes | Which pages *mention* a keyword |

The deciding argument for the first is that **the report is what an editor clicks "unlink" on**. A
report from a different code path could offer to suppress a link that was never there.

Mechanics:

- Enumerates published keys via `IContentService.GetRootContent` and `GetPagedDescendants`, because
  the published cache exposes no root enumeration.
- Only converts properties whose editor alias is rich text, Block List or Block Grid. Converting
  every property meant resolving media and pickers, which was 2.6 seconds of a 2.6 second scan.
  Filtering took 25 pages from ~2,600 ms to ~34 ms with identical results.
- Walks `BlockListModel`, `BlockGridModel` and grid areas recursively with a depth guard of 10.
- Wraps value reads in `IAutoLinker.Suppress()`.
- Sets `IVariationContextAccessor.VariationContext` per culture before reading, because nested rich
  text has no per-call culture argument.
- Gives each page and culture a fresh `AutoLinkRequestState`, so the report honours the caps the
  same way a real request would.

**An invariant page is examined once per site language, not once against the invariant set.** It has
no per-culture versions but it is still served in every language, and the renderer picks keywords by
request culture. Scanning it invariantly was a real gap: on the Clean site that was every blog post,
and the report showed 2 rows where the site was linking on 10.

Every mention is accounted for, and whole pages too: opted-out and unroutable pages land in
`Skipped` with a reason, so "why is my page missing" is answerable. Reasons are capped per keyword
per reason.

---

## 10. Cultures

The snapshot holds one `CultureKeywordSet` per configured language plus an invariant one, each with
its own targets, suppressions and matcher. `KeywordSnapshot.For(culture)` selects, falling back to
invariant, which matters both ways: a single-language site has everything in the invariant set, and
a request with no culture on a varying site would otherwise get nothing.

Both tables carry a culture, empty meaning every culture, so a keyword added before the site went
multilingual keeps applying and gets resolved separately per language. Culture-specific rows win.

Stored cultures are lower-cased, because they are index keys compared case-insensitively. The
dashboard maps them back to the registry's spelling for display, so a row stored as `en-gb` shows as
`en-GB`.

Segments are not handled. `VariationContext` carries one; only culture is used.

> **Historical note, since it shaped the design:** `ITagQuery.GetContentByTagGroup(group)` with no
> culture returns **nothing** once the queried property varies by culture. No error, no warning, and
> the property still shows its values in the editor. That silently killed every link on the site and
> is why the design is per culture at all. The tags query is gone now, so the trap is unreachable.

---

## 11. API surface

All under `/umbraco/management/api/v1/autolink`, versioned, with their own Swagger document at
`/umbraco/swagger` via `ConfigureAutoLinkSwaggerGenOptions`.

| Method | Route | Purpose |
|---|---|---|
| GET | `/keywords` | Rows per culture, unresolved first |
| PUT | `/mapping` | Create a keyword or repoint one |
| DELETE | `/mapping?keyword=&culture=` | Remove a keyword |
| GET | `/scan` | Dry-run report, and reconciles relations |
| POST | `/relations` | Reconcile relations explicitly |
| PUT | `/suppression` | Switch a keyword off |
| DELETE | `/suppression?keyword=&pageKey=&culture=` | Switch it back on |
| DELETE | `/data?confirm=remove-autolink-data` | Teardown, admin only |

Both deletes are **idempotent**: removing something absent returns success, because the state the
caller asked for already holds. They used to 404, which made a scope mismatch look like an error an
editor could not clear.

`PUT /mapping` validates that exactly one of `targetKey` and `externalUrl` is supplied, caps the
keyword at 255 characters to match the column, and normalises an external URL through
`ExternalUrl.TryNormalise`.

### Authorization

Two policies, registered in the composer:

- `Initials.AutoLink.SectionAccess` on `AutoLinkControllerBase`, requiring the
  `Initials.AutoLink.Section` grant. Umbraco's own `SectionAccess*` policies are built on an `internal`
  requirement type, so `SectionAccessRequirement` and `SectionAccessHandler` are a small
  reimplementation over the public `IAuthorizationHelper` and `IUser.AllowedSections`.
- `Initials.AutoLink.Teardown` additionally requiring an administrator, on the data controller only.
  Both apply, so the administrator doing a teardown also needs the section.

> **Trap worth knowing:** using `AuthorizationPolicies.BackOfficeAccess` on a custom management API
> controller 401s. The backoffice reads any 401 as an expired session, so the symptom is not an
> error in the dashboard, it is being **logged out** the moment you open the section.

---

## 12. Backoffice

`wwwroot/umbraco-package.json` registers five extensions: three localisation entries (`en`, `en-gb`,
`en-us`, all pointing at the same file), one section, one dashboard.

Asset URLs carry `?v=0.1.0`, taken from the manifest version. Without it a published update is
shadowed by the browser's cached module, since the assets are served with no `Cache-Control`. Bump
the manifest version to bust it.

`autolink-keywords.js` is one plain ESM file, no build step: the backoffice ships an import map so
bare `@umbraco-cms/backoffice/...` specifiers resolve at runtime.

Notable points:

- **It does not use `umbHttpClient`.** That client routes failures through backoffice error
  handling, where a 401 from a package endpoint is indistinguishable from a dead session and signs
  the user out. It carries its own token from `UMB_AUTH_CONTEXT` and renders its own errors.
- The destination field is `umb-input-multi-url` from `@umbraco-cms/backoffice/multi-url-picker`,
  the input behind `Umbraco.MultiUrlPicker`, with `max="1"` and `hide-anchor`. A document pick
  yields `unique` (the page key); anything else is treated as external.
- Its modal is a **route**, needing `UMB_ROUTE_CONTEXT`. That resolves because
  `umb-section-main-views` renders dashboards inside `umb-router-slot`.
- A **media** pick is refused on change with a message, because a media URL is site-relative and
  `ExternalUrl` deliberately requires absolute http(s). "Open in new window" cannot be hidden on
  either the input or the built-in property editor, so a set target prints a line saying it will not
  be used.
- **List semantics, not table roles.** It looks like a table, but each row contains its own detail
  panel and no table role permits that.
- Every string lives in `wwwroot/lang/en.js`. A term taking values is a **function**; the pre-v14
  `%0%` token style renders literally. There is **no fallback from a specific culture to its base**,
  which is why `en`, `en-gb` and `en-us` are all registered.

> **A new section is not granted to anybody, including administrators.** Users, User Groups,
> Sections, tick it, or the section simply is not in the nav.

---

## 13. Configuration

Bound from `Initials:AutoLink` through `IOptionsMonitor`, so edits apply without a restart.

```json
{
  "Initials": {
    "AutoLink": {
      "Enabled": true,
      "ExcludePropertyAlias": "excludeFromAutoLinking",
      "ExternalLinkRel": "nofollow",
      "MaxLinksPerKeyword": 1,
      "MaxLinksPerPage": 25,
      "SkipInsideElements": [ "a", "code", "pre", "h1", "h2", "h3" ],
      "InstallSchema": false,
      "InstallOnDocumentTypes": []
    }
  }
}
```

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` | False delegates straight through to Umbraco's converter |
| `ExcludePropertyAlias` | `excludeFromAutoLinking` | Boolean opting a page out of being *scanned*; it can still be a target |
| `ExternalLinkRel` | `nofollow` | Empty omits it. Per-row override available |
| `MaxLinksPerKeyword` | `1` | SEO caution: the first mention is the useful one |
| `MaxLinksPerPage` | `25` | Counted across every rich text property in the request |
| `SkipInsideElements` | `a, code, pre, kbd, samp, script, style, textarea, button, select, option, h1-h6` | |
| `InstallSchema` | `false` | Off by default: adding properties is not a package's decision |
| `InstallOnDocumentTypes` | `[]` | Empty disables the installer |

**Nothing here needs configuring for linking to work.** Keywords are added on the screen, so there
is no tag group to match and no property alias to get right.

---

## 14. Failure modes

Everything degrades toward "renders unlinked" rather than "throws".

| Failure | Behaviour |
|---|---|
| Registry build throws | Logged, `KeywordSnapshot.Empty`, site renders unlinked |
| Mapping table missing | Store logs, returns empty, nothing auto-links |
| Rewrite throws on odd markup | Logged, original markup returned |
| Mapped page deleted or unpublished | Keyword resolves to nothing, reported unresolved, sorts first |
| Stored external URL not absolute http(s) | Revalidated at build, logged, resolves to nothing |
| Relation type missing | Reconcile logs and no-ops; the scan report is still correct |
| Relation reconcile throws | Logged; `GET /scan` still returns its report |
| Relation cleanup throws on delete | Logged; the delete still succeeds |
| Migration plan fails | Logged per plan, boot continues |

---

## 15. Tests

54 tests in `tests/Initials.AutoLink.Tests`, xUnit and NSubstitute.

| File | Covers |
|---|---|
| `AutoLinkerTests` | Word boundaries, longest-first, skipped elements, no nested anchors, attributes untouched, per-keyword cap, hand-link detection, self-linking, external markup and `rel`, suppressed keywords reserving their span |
| `ResolutionTests` | `ExternalUrl` validation including `javascript:` and `data:`, culture fallback, row precedence, narrowest suppression, budget vs reporting tallies |
| `RelationTests` | Relation direction, only-linked-mentions recorded, external excluded, culture dedup, stale removal, cleanup in both directions, missing relation type |
| `TestLinker` | Builds an `AutoLinker` over a hand-made set using the **real** `KeywordMatcher`, so tests exercise the shipped matching rules |

`TestLinker` using the real matcher is deliberate: what ships and what is tested cannot disagree
about which keywords are matchable.

---

## 16. Uninstall

Umbraco has no uninstall hook for a NuGet-delivered package, so teardown is explicit:

```
DELETE /umbraco/management/api/v1/autolink/data?confirm=remove-autolink-data
```

`AutoLinkUninstaller.RemoveData` drops both tables with `DROP TABLE IF EXISTS`, removes the relation
type and its relations, resets the migration state to the plan's initial value, and invalidates the
registry.

**Resetting the migration state is the part that matters.** Dropping the tables while
`umbracoKeyValue` still records the plan as complete means a reinstall never recreates them, and the
package comes back up permanently broken with stores that log and return empty.

Only the main plan is rewound, not the schema plan: re-adding properties somebody removed on purpose
is not what "remove the data" means.

**It takes every keyword with it.** These tables are the only place keywords exist. That changed
when keywords moved off document types, and it is why the endpoint needs an administrator plus a
confirmation token.

---

## 17. Known limitations

- No stemming or plurals. Each surface form needs its own keyword.
- Hand-link detection is per property, so a manual link in a later Block List item is not seen when
  an earlier one converts.
- Delivery API output is delegated, not linked.
- CDN caching: render-time healing stops at the edge. Wire a purge into the stamp bump or accept
  that retroactive links appear on TTL expiry.
- The API is gated on section access, not a per-keyword permission.
- A keyword cannot be renamed; suppressions are keyed on it. Remove and re-add.
- A keyword added from the screen is culture-specific. All-culture rows need the API with an empty
  culture.
- Media is not a valid destination.
- "Open in new window" from the picker is ignored.
- Relations are only as fresh as the last scan, so the delete warning can undercount.
- A keyword whose target nothing mentions raises no dependency warning.
- The scan is synchronous. 34 ms for 25 pages extrapolates fine to a few hundred, not to thousands.
- Suppression is per keyword per page, not per occurrence.
- Segments are not handled.

---

## 18. Where to start reading

Following one link from request to output:

1. `PropertyEditors/AutoLinkRichTextValueConverter.cs` - the hook
2. `Linking/AutoLinker.cs` - `ProcessMarkup`, then `Rewrite`, then `RewriteTextNode`
3. `Registry/KeywordRegistry.cs` - `Build`, `BuildSet`, `Resolve`
4. `Registry/KeywordMatcher.cs` - the two rules that matter

Following one keyword from the screen to the database:

1. `wwwroot/autolink-keywords.js` - `#saveKeyword`
2. `Api/Controllers/KeywordMappingController.cs` - `Save`
3. `Persistence/KeywordMappingStore.cs` - `Write`, then `Apply`
4. `Caching/IKeywordRegistryInvalidator.cs` - how the other servers find out
