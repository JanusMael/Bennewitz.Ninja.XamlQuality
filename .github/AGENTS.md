# AGENTS.md — `.github/`

The workflows and the repository's GitHub settings.

| File | What it is |
|---|---|
| `workflows/ci.yml` | On every push and pull request: build with `-warnaserror` and test on Linux, Windows and macOS; pack both projects; and the `conventions` job, which runs `scripts/repo-conventions.cs check` |
| `workflows/release.yml` | One `publish` job. On a `v*.*.*` tag, or a dispatch with `version` filled in, it refuses to run without `NUGET_USER`, then builds, tests, packs at the tag's version, logs in through `NuGet/login`, pushes both packages with `--skip-duplicate`, and creates the GitHub Release. Dispatched with `version` blank, it only logs in and reports the preflight in the run summary |
| `repository.json` | What this repository's GitHub settings vary by: description, topics, required checks, shipped content paths and exemptions. `scripts/repo-conventions.cs` applies and checks it |
| `copilot-instructions.md` | A pointer to the root `AGENTS.md` for tools that look here |

## Rules

| Rule | Why | Guarded by |
|---|---|---|
| **A job's check name is part of the `main` ruleset.** `Build & Test (ubuntu-latest)`, `Build & Test (windows-latest)`, `Build & Test (macos-latest)`, `Pack (verify the packages build)` and `conventions` are required checks, listed under `requiredChecks`. The matrix job reports one check per OS, so renaming the job or changing its matrix means updating the list and running `repo-conventions apply` in the same change | A required check that no job reports blocks every pull request, and GitHub never says why | `repository.json`; `repo-conventions check` |
| **`release.yml` keeps its file name**, and the credential preflight stays inside it | The trusted-publishing policy on nuget.org names this file, and the OIDC token is bound to the file name; renamed, every login fails | `docs/publishing.md`, step 2 |
| The release names each package in both the push and `gh release create`; nothing globs `*.nupkg` | A glob publishes whatever is in the folder, permanently | `ReleaseWorkflowTests` |
| Every publishing step is gated on `RELEASING`, and the push also on `NUGET_USER` | A blank-version dispatch must never publish, and a tag with the variable unset must fail rather than create a release with no package | `release.yml`, the job-level `env` and each step's `if` |
| `NUGET_USER` is a repository variable, never a secret | A masked value hides the one fact that diagnoses a failed login | `release.yml` comment; `docs/publishing.md`, step 1 |
| `release.yml` holds `id-token: write`, and the login stays immediately before the push | That permission replaces a stored API key; the token it yields is short-lived | `release.yml` comments |
