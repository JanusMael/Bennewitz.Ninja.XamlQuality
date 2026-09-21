# Publishing to nuget.org

This repository publishes through **NuGet.org Trusted Publishing (OIDC)**. There is no API key
stored anywhere: the release workflow mints a short-lived token per run, so nothing in this
repository would still be valid if it leaked.

Two packages ship together from one tag:

| Package | Kind |
|---|---|
| `Bennewitz.Ninja.XamlQuality` | Library |
| `Bennewitz.Ninja.XamlQuality.ThemeAudit` | `dotnet tool` — command `theme-audit` |

---

## Before the first release — one-time setup

Steps 1 and 2 are account-level and have to be done by the nuget.org account owner.

### 1 · Add the `NUGET_USER` variable

```bash
gh variable set NUGET_USER --body <your-nuget.org-profile-name> --repo JanusMael/Bennewitz.Ninja.XamlQuality
```

⛔ **A variable, never a secret.** A profile name is public, so there is nothing to protect — and
masking it costs you the only fact that diagnoses a failed login. A bad value turns nuget.org's
already-ambiguous 401 into `No matching trust policy owned by user '***' was found`, which hides
whether you stored a username, an email, or an API key. That masking cost six blind preflight runs
in a sibling repository on 2026-09-21. As a variable, the error names the value and
`gh variable get NUGET_USER` reads it back.

⛔ **The value is the nuget.org PROFILE NAME, not an email address.** An email is accepted by the
variable store and then fails at login, where the error points at the policy rather than at the value.

⛔ **It is the profile name of whoever CREATED the policy**, which is not always whoever owns the
package. The two differ whenever a policy is created under an organization, and the failure reads as
a problem with the policy rather than with this value.

### 2 · Create the trusted-publishing policy

At <https://www.nuget.org/account/trustedpublishing> → **Add policy**.

| Field | Value |
|---|---|
| Policy owner | `JanusMael` — the individual account, **not** an organization |
| Repository Owner | `JanusMael` |
| Repository | `Bennewitz.Ninja.XamlQuality` |
| Workflow File | `release.yml` |
| Environment | **leave blank** |
| Scopes | **Publish new packages** *and* **Publish new versions of existing packages** |
| Package pattern | `Bennewitz.Ninja.XamlQuality*` — covers both ids |

⛔ **The scope must permit publishing NEW packages.** A policy scoped only to new versions of
existing packages matches nothing until an id exists, and both ids here are new until the first
release lands. This is invisible in the field list and produces a generic 401 that reads as though
the repository name were wrong.

⛔ **Environment must be BLANK.** It is filled in only when the publishing job declares
`environment:`, and this one deliberately does not. A value here matches nothing, and the failure
reads as *"no matching policy"* — which sends you looking at the repository name instead.

⛔ **Workflow File is the exact FILENAME**, no path and no `.github/workflows/` prefix, and it is
**not** the workflow's `name:` field. Matching is case-insensitive.

⚠ **Ownership is chosen at creation and can lapse afterwards.** A policy owned by an organization
goes inactive if its creator leaves that org, and reactivates when they are added back. Choosing the
individual account avoids the whole class.

⚠ **A policy for a PRIVATE repository starts temporarily active for 7 days.** nuget.org needs the
GitHub repository and owner ids — which only a successful publish supplies — to pin the policy
against a delete-and-recreate attack, so it stays provisional until then and goes inactive if
nothing publishes inside the window. The window can be restarted at any time. This repository is
public, so it does not apply here; it explains a policy that silently stops working elsewhere.

⭐ **Neither package exists on nuget.org yet, and that is fine.** The `Bennewitz.Ninja.*` prefix
reservation lets a policy be created for an id that has never been published.

⛔ **One policy with a pattern is the only shape that works for two ids.** Two exact-id policies do
**not** work, and the way they fail is expensive: nuget.org mints **one** API key per token
exchange, scoped to **one** matching policy. Both policies match the same OIDC claims — same owner,
same repository, same workflow — so it picks one, and the package covered by the other is rejected
`403` at push. This was proven the hard way on the first `2026.3.920` release: the library pushed
and became permanent, the tool was forbidden in the same command.

### 3 · Preflight the credentials

**Release** → *Run workflow* → leave **version BLANK** → run.

A blank version means the workflow logs in to NuGet.org and stops. Nothing is built and nothing is
pushed, so this is safe to run as often as you like — it proves the policy, the `id-token`
permission and the `NUGET_USER` variable all line up, at the one moment when finding out is still
free.

⚠ **A green preflight proves the login works, not that every id is covered.** The exchange returns
one key scoped to one policy, so it succeeds long before any package id is considered. The run
summary prints both ids for exactly this reason: confirm a **single** policy whose pattern covers
both, rather than a policy per id. Nothing in a preflight can catch a coverage gap — only the push
can, and by then half the release is permanent.

---

## Choosing the version

The standard across these packages is **`YYYY.Q.MMDD`** — year, quarter, then month-day with the
leading zero dropped. `2026.3.920` is 20 September 2026, in Q3.

⚠ **Day resolution means ONE release per calendar day.** A second tag on the same date collides
with a version that can never be replaced, and the recovery is to wait for tomorrow.

⛔ **A published version is permanent.** It cannot be replaced, re-pushed, or corrected — only
superseded by a higher one. Everything this repository otherwise treats as recoverable stops being
so at the push step.

---

## Releasing

### 4 · Tag and push

```bash
git tag -a v2026.3.920 -m "XamlQuality 2026.3.920"
git push origin v2026.3.920
```

The version comes from the tag (`v` stripped), so the tag and the package agree by construction
rather than by remembering to bump a file.

ⓘ A release can also be started by hand from the Actions tab — **Release** → *Run workflow* →
**enter the version** — which is the path to use when a tag already exists. Filling the version in
is what separates a release from the preflight in step 3; leaving it blank never publishes.

### 5 · Watch the run

```bash
gh run watch --repo JanusMael/Bennewitz.Ninja.XamlQuality --exit-status
```

It builds, runs the full suite, packs both projects, logs in over OIDC, pushes both packages, and
creates the GitHub Release. Every gate runs **before** the push, because the push is the
irreversible part.

### 6 · Verify against the feed, not against the workflow

A green workflow says the steps exited zero. Ask nuget.org:

```bash
curl -s https://api.nuget.org/v3-flatcontainer/bennewitz.ninja.xamlquality/index.json
curl -s https://api.nuget.org/v3-flatcontainer/bennewitz.ninja.xamlquality.themeaudit/index.json
```

Each should list the new version. Indexing takes a few minutes, so a miss immediately after the run
means "not yet", not "failed".

Then prove the tool actually installs and runs:

```bash
dotnet tool install --global Bennewitz.Ninja.XamlQuality.ThemeAudit --version 2026.3.920
theme-audit --help
```

ⓘ `theme-audit --version` reports `Commit♥: <sha>`, not the package version. That is
`Bennewitz.Ninja.AutoVersioning` using `AssemblyInformationalVersion` as a build-provenance slot,
and `System.CommandLine`'s built-in `--version` reading exactly that attribute. The package version
is what `dotnet tool list --global` shows. On a local build with no `GITHUB_SHA` the same slot reads
`Built with ♥`.

---

## When something goes wrong

⛔ **Never re-tag to fix a failed run.** `gh run rerun` replays the YAML from the commit the tag
points at, so moving the tag does not change what runs — and deleting and re-pushing a tag to a new
commit makes the release history disagree with itself. Fix forward: land the fix on `main`, then
either dispatch the workflow by hand or cut the next day's version.

| Symptom | Cause | Fix |
|---|---|---|
| `NuGet/login` returns **403** | Job is missing `id-token: write` | It is present in `release.yml`; if it was edited, restore it |
| Token exchange **401**, *"No matching trust policy owned by user"* | The name in `NUGET_USER`, the policy's owner, or the policy's scope | Work the list below — the message is the same for all three |
| **"no matching policy"** | Workflow filename, owner, repo or Environment does not match the policy | Check the policy's Workflow File is `release.yml` and **Environment is blank**, then re-run the step 3 preflight |
| **Push unauthorized** | The policy is owned by a different account than the package | Confirm the policy owner on nuget.org |
| **Token expired** | More than an hour between login and push | The two steps are adjacent here; suspect a stalled build |
| `already_exists` | Re-running a completed release | Expected — `--skip-duplicate` makes it a no-op |
| GitHub Release **422** | A release already exists for that tag | Delete the conflicting release, then re-run |
| One package pushed, the other **403** | A policy per id: one exchange mints one key scoped to one policy | Replace both with a single policy patterned `Bennewitz.Ninja.XamlQuality*`, then `gh run rerun` — `--skip-duplicate` no-ops the one already live and the other pushes at the same version |

⚠ **The `403` row is the one worth expecting**, because it is the only failure that leaves the two
packages at different versions. It is survivable only because the tag can be replayed: `gh run
rerun` re-reads the same commit, `--skip-duplicate` makes the live package a no-op, and the rejected
one goes up at the same version. Fix the policy first — a rerun against an unchanged policy fails
identically.

### Working the 401

The message is identical whichever of these is wrong, so check them in this order — cheapest first,
and re-run the step 3 preflight after each, which costs nothing:

1. **Read `NUGET_USER` out loud before anything else.** `gh variable get NUGET_USER`. It is the
   profile name, not an email, and it is the name of the account that **created** the policy.
2. **The policy owner is the individual account**, not an organization.
3. **The scope allows publishing new packages** — see the ⛔ in step 2. Until the first release
   lands, every id here is a new package.
4. **A single policy's pattern covers every id** — never one policy per id; see the ⛔ in step 2.
5. **The policy is not showing a pending or inactive warning** in the UI.

ⓘ **In practice it was the first one.** `NUGET_USER` predated the policies, so nothing had ever
validated it; setting it to the profile name turned the 401 green on the next preflight. It was a
**secret** at the time, which is why reading it took six runs instead of one command.

⚠ **The preflight cannot distinguish these for you.** nuget.org returns the same message whichever
one is wrong: it reports that no policy matched, never which field failed to match. Changing one
thing at a time is the only way through.
