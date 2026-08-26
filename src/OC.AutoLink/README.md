# OC.AutoLink

Turns keyword mentions in Umbraco rich text into links to the corresponding page, **as the page is served**.
No editor action, no property on your document types, and stored markup is never modified.

An editor writes "we tested this with Claude AI last week". If the keyword `Claude AI` points at a page, the
phrase renders as a link to it. The editor did nothing.

Write it *before* that page exists and the link appears the day somebody adds the keyword. No republishing, no
backfill, no dead anchors when a target goes away — because nothing was ever baked into the content.

Requires **Umbraco 17** on **.NET 10**.

---

## Installing it

```bash
dotnet add package OC.AutoLink
```

The Umbraco dependency is expressed as `[17.6.1, 18)`, so restore will tell you plainly if you are on a major this
has not been built against, rather than the site failing at runtime.

Then three things, in order:

1. **Grant the section.** Users → User Groups → your group → Sections → **Auto-linking**. A new section is granted
   to nobody by default, administrators included, so until you do this the screen is not reachable and the package
   looks like it did not install.
2. **Add a keyword.** Auto-linking → Keywords → add the phrase, and pick where it goes with the Multi URL Picker —
   a page, or an address outside the site.
3. **Reload a page that mentions it.** That is the whole setup. Nothing is added to your document types, there is
   nothing for an editor to fill in per page, and no page needs republishing.

## The Auto-linking section

One screen, Keywords, showing both halves of every keyword: where it points, and which published pages currently
carry a link because of it. It is where you add keywords, change where one goes, and switch individual links off —
either one keyword on one page, or a keyword everywhere.

A keyword whose destination has broken sorts to the top and opens itself. It stops linking rather than falling back
to a guess.

## What it does to your markup

```html
<a href="/blog/claude-ai/" data-autolink="true">Claude AI</a>
```

External destinations get `data-autolink-external="true"` and `rel="nofollow"` by default. Everything is marked, so
auto-links can be audited, styled, or stripped wholesale later.

The rules it follows, none of which are configurable per keyword:

- The editor's own casing is preserved, and matching respects word boundaries.
- One link per keyword per page by default. The first mention is the useful one.
- Never inside an existing `<a>`, `<code>`, `<pre>` or a heading, and never a page linking to itself.
- Skipped if the editor already hand-linked to that destination on the page.
- Longest keyword wins, so `Claude AI Sonnet` beats `Claude`.

Rich text nested inside Block List, Block Grid and nested blocks is covered with no view changes.

## Multilingual

Keywords are per culture, each with its own destination and its own resolved URL. A keyword added with no culture
applies to every language and is resolved separately for each; a culture-specific one wins over it.

## Configuration

**Nothing here needs setting to make linking work.** Keywords are added on the screen, so there is no tag group to
match and no property alias to get right.

```json
{
  "OC": {
    "AutoLink": {
      "Enabled": true,
      "ExternalLinkRel": "nofollow",
      "MaxLinksPerKeyword": 1,
      "MaxLinksPerPage": 25
    }
  }
}
```

Bound through `IOptionsMonitor`, so edits apply without a restart. The full set of options, including the optional
`excludeFromAutoLinking` property for pages that should not be scanned at all, is documented on `AutoLinkOptions` —
the package ships its XML docs, so your IDE has them.

## Removing it

Umbraco has no uninstall hook for a NuGet package: removing the reference removes the assembly and leaves the two
tables. Teardown is an explicit call, and it needs an administrator.

```
DELETE /umbraco/management/api/v1/autolink/data?confirm=remove-autolink-data
```

**It takes every keyword with it.** Those tables are the only place keywords live; there is no other copy. It also
resets the migration state, which is what makes a reinstall work rather than coming back up with empty tables.

Document types are left alone, so `excludeFromAutoLinking` and any values in it survive.

## Digging deeper

| Document | What it covers |
|---|---|
| [Technical specification](https://github.com/OwainWilliams/OC.AutoLink/blob/main/docs/technical-spec.md) | A developer's map: component by component, the render path, the data model, the migrations. |
| [Build log](https://github.com/OwainWilliams/OC.AutoLink/blob/main/docs/build-log.md) | How it was built and verified, with evidence, measurements and the Umbraco 17 traps that cost the most time. |
| [Design decisions](https://github.com/OwainWilliams/OC.AutoLink/blob/main/CLAUDE.md) | Why it renders instead of publishing, why not Examine, and what the rejected alternatives were. |

Issues and pull requests: [github.com/OwainWilliams/OC.AutoLink](https://github.com/OwainWilliams/OC.AutoLink).

Licensed MIT.
