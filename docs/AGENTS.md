# AGENTS.md — `docs/`

Documents for humans, and the only Avalonia-specific content in a repository whose code is
framework-neutral. What an agent needs to change the code lives in the `AGENTS.md` files, not here.

| Document | What it is | Kept in step with |
|---|---|---|
| `avalonia-gotchas.md` | Measured Avalonia foot-guns, each entry a symptom, a cause and a fix; an entry marked "Candidate rule" describes a check not yet written | The rules in `src/XamlQuality/Rules/`. An entry that becomes mechanically checkable is promoted to a rule and keeps a line pointing at the rule id, as the entry for `XQ1004` does |
| `ai-drivable-ui.md` | The method for a desktop UI an agent can drive and verify through its automation tree, companion to the gotchas | TailBlazer's `Documents/AiDrivableUi.md`, byte for byte |
| `publishing.md` | The release runbook: the `NUGET_USER` variable, the trusted-publishing policy, the credential preflight, the version rule, releasing, verifying against the feed, and the failure table | `.github/workflows/release.yml`. A step, input or package id changed there is changed here in the same change |

## Rules

| Rule | Why |
|---|---|
| **`avalonia-gotchas.md` keeps its path and its entry headings** | Other repositories cite it by path, listed in `PROGRESS.md`; moving it or retitling a cited entry means updating them |
| **`ai-drivable-ui.md` is edited only by applying the same change to every copy** | Two copies of one method that disagree leave no way to tell which is current |
| Prose in `avalonia-gotchas.md` is for what a markup scan cannot see | A checkable condition left as prose is one nothing enforces; the document's own header says so |
| `publishing.md` names the policy's Workflow File as `release.yml` and the package pattern as `Bennewitz.Ninja.XamlQuality*` | Both are matched by nuget.org against the workflow file name and the packed ids; a mismatch fails every login with the same undiagnosable message |
