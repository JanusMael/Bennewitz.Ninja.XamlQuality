# AGENTS.md — `scripts/`

File-based C# apps, run with `dotnet run --file`. Each is compiled with this repository's root
`Directory.Build.props`, so warnings are errors here too.

| Script | What it does | Run by |
|---|---|---|
| `repo-conventions.cs` | Checks, and applies, the family's repository conventions: the AI-facing documents, GitHub settings and rulesets, read against `.github/repository.json` | CI's `conventions` job (`check`), and the maintainer (`check --admin`, `apply`) |

## Rules

| Rule | Why |
|---|---|
| **`repo-conventions.cs` is never edited here** | It is a copy of `templates/bbpkg/scripts/repo-conventions.cs` in Bennewitz.Ninja.Templates, identical in every family repository. Change it there and copy it back; run from that repository, `check --repo` reports a copy that differs |
| What this repository varies goes in `.github/repository.json`, not in the script | The family baseline lives in the script's `Baseline` and must not differ between repositories |
| A file-based app builds with everything the root props set, including the `Bennewitz.Ninja.AutoVersioning` reference and central package management | Nothing scopes `Directory.Build.props` to projects under `src`, so a script that fails to build may be failing on a root setting rather than on its own code |
