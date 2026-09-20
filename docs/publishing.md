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

Both steps are account-level and have to be done by the nuget.org account owner.

### 1 · Add the `NUGET_USER` secret

```bash
gh secret set NUGET_USER --repo JanusMael/Bennewitz.Ninja.XamlQuality
```

It prompts for the value, so it stays out of shell history.

⛔ **The value is the nuget.org PROFILE NAME, not an email address.** An email is accepted by the
secret store and then fails at login, where the error points at the policy rather than at the value.

### 2 · Create the trusted-publishing policies

At <https://www.nuget.org/account/trustedpublishing> → **Add policy**. **Once per package id — two
policies**, identical except for the package name:

| Field | Value |
|---|---|
| Package | `Bennewitz.Ninja.XamlQuality` *(then repeat for `Bennewitz.Ninja.XamlQuality.ThemeAudit`)* |
| Repository Owner | `JanusMael` |
| Repository | `Bennewitz.Ninja.XamlQuality` |
| Workflow File | `release.yml` |
| Environment | **leave blank** |

⛔ **Environment must be BLANK.** It is filled in only when the publishing job declares
`environment:`, and this one deliberately does not. A value here matches nothing, and the failure
reads as *"no matching policy"* — which sends you looking at the repository name instead.

⛔ **Workflow File is the exact FILENAME**, no path and no `.github/workflows/` prefix, and it is
**not** the workflow's `name:` field. Matching is case-insensitive.

⭐ **Neither package exists on nuget.org yet, and that is fine.** The `Bennewitz.Ninja.*` prefix
reservation lets a policy be created for an id that has never been published.

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

### 3 · Tag and push

```bash
git tag -a v2026.3.920 -m "XamlQuality 2026.3.920"
git push origin v2026.3.920
```

The version comes from the tag (`v` stripped), so the tag and the package agree by construction
rather than by remembering to bump a file.

ⓘ A release can also be started by hand from the Actions tab — **Release** → *Run workflow* → enter
the version — which is the path to use when a tag already exists.

### 4 · Watch the run

```bash
gh run watch --repo JanusMael/Bennewitz.Ninja.XamlQuality --exit-status
```

It builds, runs the full suite, packs both projects, logs in over OIDC, pushes both packages, and
creates the GitHub Release. Every gate runs **before** the push, because the push is the
irreversible part.

### 5 · Verify against the feed, not against the workflow

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

---

## When something goes wrong

⛔ **Never re-tag to fix a failed run.** `gh run rerun` replays the YAML from the commit the tag
points at, so moving the tag does not change what runs — and deleting and re-pushing a tag to a new
commit makes the release history disagree with itself. Fix forward: land the fix on `main`, then
either dispatch the workflow by hand or cut the next day's version.

| Symptom | Cause | Fix |
|---|---|---|
| `NuGet/login` returns **403** | Job is missing `id-token: write` | It is present in `release.yml`; if it was edited, restore it |
| **"no matching policy"** | Workflow filename, owner, repo or Environment does not match the policy | Check the policy's Workflow File is `release.yml` and **Environment is blank** |
| **Push unauthorized** | The policy is owned by a different account than the package | Confirm the policy owner on nuget.org |
| **Token expired** | More than an hour between login and push | The two steps are adjacent here; suspect a stalled build |
| `already_exists` | Re-running a completed release | Expected — `--skip-duplicate` makes it a no-op |
| GitHub Release **422** | A release already exists for that tag | Delete the conflicting release, then re-run |
| One package pushed, the other did not | Only one policy was created | Add the missing policy; re-run — `--skip-duplicate` skips the one already live |

⚠ **The last row is the one worth expecting**, because it is the only failure that leaves the two
packages at different versions. Both policies, before the first tag.
