# CLAUDE.md — the shape of this repository and the reasoning behind it

> Audience: a human or agent who needs to understand this repository before changing it.
> [`AGENTS.md`](./AGENTS.md) is the companion: the invariants a change must not break, each tied to
> what guards it, plus the commands and the checklists for recurring work. When the two disagree,
> `AGENTS.md` is the enforceable one. It is imported at the end of this file, so Claude Code loads
> both.
>
> Work state lives in [`PROGRESS.md`](./PROGRESS.md), updated in the same change as the work it
> describes: what is published, what is on `main` and not yet released, who depends on what, the
> rule backlog, and the questions open for the developer.

## What this repository is

Static analysis for XAML markup, shipped as two packages from one tag:

| Package | Project | What it is |
|---|---|---|
| `Bennewitz.Ninja.XamlQuality` | `src/XamlQuality` | The rules library: `IXamlRule` implementations a consumer runs from its own tests |
| `Bennewitz.Ninja.XamlQuality.ThemeAudit` | `src/XamlQuality.ThemeAudit` | The `theme-audit` dotnet tool: argument parsing and reporting over the library's `ThemeAudit` namespace |

The rules began as guards hand-rolled inside applications and copied from one to the next, each copy
having learned a different subset of the cases. Extracting them was the point. `XQ1001` and `XQ1002`
each turned out stricter than the guard it replaced, in ways nobody predicted: the guard behind
`XQ1001` had passed `AutomationProperties.Name=""` for months.

## Three design commitments

**A rule reports; it never asserts.** `IXamlRule.Analyze` returns an `XamlRuleResult`, findings
rather than exceptions, so nothing in the library references a test framework. The consumer picks
the runner and the severity: the same finding can fail an xUnit test, warn in CI or print in a
report.

**No dependencies, Avalonia included.** Rules read markup as text or XML. Where a contract has a
compiled half, a rule reads the consumer's own assemblies through `System.Reflection`. The library
works against WPF and MAUI markup as it does against Avalonia's. Only the notes in `docs/` are
Avalonia-specific, and that split is deliberate.

**Zero findings is not a result on its own.** A rule whose selector stops matching reports nothing
and looks exactly like a clean codebase. So every result carries `Inspected`, how much the rule
examined, and `Skipped`, which names what it saw but could not check. A consumer asserts on all
three.

## How a scan works

`XamlScanContext.Load(root)` reads every `.axaml` and `.xaml` file under a root once, skips `bin`
and `obj`, and sorts the files so findings are stable from run to run. Each `XamlFile` offers its raw
`Text` and its parsed `Document`, because some rules ask about structure and others about spelling.
A file that does not parse carries a `ParseError` instead of disappearing. `WithAssemblies` returns
a new context that also carries compiled assemblies, for the rules that need them.

The rules live in `src/XamlQuality/Rules`, and the README's generated table lists every one. These
are the ones that shaped the design:

- **`XQ1001` and `XQ1002`** require a non-empty `AutomationProperties.Name`, in either spelling,
  through the shared `AutomationName` helper. `XQ1002` covers what a keyboard can reach and leaves
  `Expander` to `XQ1001`, so a consumer runs both.
- **`XQ1003`** reads the `PART_` names a control's compiled code looks up, and requires each in that
  control's own `ControlTheme`. A misspelt part fails silently at runtime, and no compiler can see
  it, because the two halves are in different languages. The literals come from IL, through
  `CompiledStrings`, because a string passed straight to a lookup exists only as an instruction in
  a method body.
- **`XQ1004`** reports a control whose declared size exceeds the fixed Grid row or column it sits in.
  It asks about the automation tree, which clipping never changes, and that is what keeps it
  decidable from markup.

There is no rule registry: a consumer constructs each rule by name. That makes the README's rules
table the only enumeration of the rules anywhere, which is why a test generates it from the rule
types rather than trusting anyone to add the row.

## ThemeAudit

The `ThemeAudit` namespace inventories the resource keys a theme defines and scans consumers for the
keys they reference. It reports what a theme leaves undefined, and which colour pairs fall below a
contrast floor. It also generates compat dictionaries: the keys one theme defines and another lacks,
mapped onto the latter's tokens. The reviewed mappings ship as `Mappings/*.json` beside the library.
`AuditRunner.Run` is the entry point. Each theme and consumer carries a content digest, so a
committed report changes when what was audited changes, and only then.

This analysis moved here from DiffView, and its surface shipped public. DiffView binds to it, so
narrowing it is a breaking change rather than a tidy-up. `src/XamlQuality/Properties/AssemblyInfo.cs`
records which types are public and which stay internal.

## The documents in `docs/`

| File | What it is |
|---|---|
| `docs/avalonia-gotchas.md` | Measured Avalonia foot-guns, each with symptom, cause and fix. Moved here from ClaudeForge |
| `docs/ai-drivable-ui.md` | TailBlazer's method for a desktop UI an agent can drive and verify, kept identical to TailBlazer's copy |
| `docs/publishing.md` | Trusted publishing, the version rule, and verifying a release against the feed |

The gotchas document lives beside the rules on purpose. An entry that can be checked mechanically is
promoted to a rule, and the entry keeps a line pointing at the rule's id. `XQ1001`, `XQ1002` and
`XQ1004` all began as entries there. Other repositories contribute entries by message and cite the
document by path.

## Who consumes this

Other repositories in the same family consume both packages. ScopedEditors' tests run the rules, and
DiffView runs the audit and commits its report. That is why the public surface is treated as a
contract. `PROGRESS.md` lists which members each consumer uses.

## Releases

The release workflow publishes through nuget.org trusted publishing (OIDC), so no API key exists.
The version is the tag, in the family's `YYYY.Q.MMDD` form. That allows one release per calendar
day, and a published version can never be replaced. `docs/publishing.md` is the runbook.

## Operational rules

@AGENTS.md
