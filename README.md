# Initials.AutoLink

An Umbraco 17 package that turns keyword mentions in Rich Text Editor content into links to the corresponding
page, **at render time**, with no editor action required.

Write "we tested this with Claude AI last week" in an RTE. If the keyword `Claude AI` points at a page, the phrase
renders as a link to it. Write it *before* that page exists, and the link appears the day somebody adds the keyword —
no republishing, no backfill, because stored markup is never touched.

Keywords are managed in one place, a custom **Auto-linking** section, using Umbraco's Multi URL Picker to send each
one at a page or at an address outside the site. Nothing is added to your document types and there is nothing for an
editor to fill in per page.

## Layout

| Path | What it is |
|---|---|
| `src/Initials.AutoLink/` | The package. Its [README](src/Initials.AutoLink/README.md) is what a consumer reads on nuget.org: install it, grant the section, add a keyword. |
| `Autolink/` | An Umbraco 17 site with the Clean starter kit, used to build and verify it. |
| `tests/Initials.AutoLink.Tests/` | Unit tests over the linker and the resolution rules. |
| `docs/technical-spec.md` | A developer's map of the code: component by component, the render path, the data model, the migrations. |
| `docs/build-log.md` | How it was built and verified — evidence, measurements and the v17 traps. |
| `CLAUDE.md` | The design decisions and why the rejected alternatives were rejected. |
| `umbraco-marketplace.json` | Listing metadata. The Marketplace reads it from this branch's root, so it has to stay here. |

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

The database, logs and media are deliberately not committed. A clone installs itself and Clean supplies the content,
so the site comes up working but with no keywords in it. Add a couple on the Auto-linking screen before expecting the
linker to have anything to do.

## The backoffice

A custom **Auto-linking** section with one Keywords screen. It needs granting to your user group first: Users →
User Groups → Administrators → Sections. A new section is not granted to anybody by default, including
administrators.

The screen shows both halves of every keyword — where it links to, and the pages that mention it — and is where you
add keywords, change where one points, and switch individual links off.

## Contributing

Issues and pull requests are welcome, at [Initials-Labs/Autolink](https://github.com/Initials-Labs/Autolink/issues).

- Branches are `type/description` — `feat/markdown-support`, `bug/scan-culture-filter`. There is no issue tracker
  number to carry in the name.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/), PR titles are prefixed
  `[FEAT]` / `[BUG]` and the like.
- `dotnet build Autolink.slnx` must come out clean — the package treats warnings as errors — and
  `dotnet test tests/Initials.AutoLink.Tests/Initials.AutoLink.Tests.csproj` must pass.
- Bump the version with `./tools/bump-version.ps1 <version>`, never by hand: the number lives in the csproj, the
  package manifest and its asset cache-busters, and the build fails if they drift. Releasing is then tagging the
  same number.
- Anything touching the composer, the migrations or the value converters should also be checked on the running
  site in `Autolink/`. Umbraco discovers those by reflection and DI, so getting them wrong compiles fine and
  fails silently — `docs/build-log.md` records what to look for.
