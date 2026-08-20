# OC.AutoLink

An Umbraco 17 package that turns keyword mentions in Rich Text Editor content into links to the corresponding
page, **at render time**, with no editor action required.

Write "we tested this with Claude AI last week" in an RTE. If a page is tagged with the keyword `Claude AI`, the
phrase renders as a link to it. Write it *before* that page exists, and the link appears the day somebody creates
it — no republishing, no backfill, because stored markup is never touched.

## Layout

| Path | What it is |
|---|---|
| `src/OC.AutoLink/` | The package. Start with its [README](src/OC.AutoLink/README.md) — design, evidence, measurements and the v17 traps. |
| `Autolink/` | An Umbraco 17 site with the Clean starter kit, used to build and verify it. |
| `Autolink/Demo/` | Development-only HTTP harness for driving the whole thing without clicking through the backoffice. |
| `CLAUDE.md` | The design decisions and why the rejected alternatives were rejected. |

## Running it

```bash
dotnet run --project Autolink/Autolink.csproj
```

Then `https://localhost:44307/umbraco`. Two things to know:

- **The backoffice needs HTTPS.** OpenIddict refuses plain HTTP outright, so the HTTP endpoint serves the front
  end only.
- **Set `UnattendedUserPassword`** in `Autolink/appsettings.Development.json` before the first run. It ships as a
  placeholder so a real credential never lands in git, and the site installs itself unattended with whatever you
  put there.

The database, logs and media are deliberately not committed. A clone installs itself, Clean supplies the content,
and `AutoLinkSchemaInstaller` adds the keyword property to the document types — so the site comes up working, but
empty of the demo's own tags. Expect to add a couple of keywords before the auto-linking has anything to do.

## The backoffice

A custom **Auto-linking** section with one Keywords screen. It needs granting to your user group first: Users →
User Groups → Administrators → Sections. A new section is not granted to anybody by default, including
administrators.

The screen shows both halves of every keyword — the page it links to, and the pages that mention it — and lets you
settle keywords two pages both claim, switch individual links off, and point a keyword at an external URL.

## Conventions

Branches `type/jiraNumber-description`, Conventional Commits, PR titles prefixed `[FEAT]` / `[BUG]`.
