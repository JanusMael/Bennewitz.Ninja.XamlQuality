# AGENTS.md — XamlQuality

> For anyone changing this repository, human or agent. The first half is the shape of the
> repository and the reasoning behind it; the second, under **Operational rules**, is the
> invariants a change must not break, each tied to what guards it, plus the commands and the
> checklists for recurring work. Where the two disagree, the operational rules are the enforceable
> ones. Each top-level directory has an `AGENTS.md` of its own for what only its files show.
>
> Work state lives in [`PROGRESS.md`](./PROGRESS.md), updated in the same change as the work it
> describes: what is published, what is on `main` and not yet released, who depends on what, the
> rule backlog, and the questions open for the developer. What every repository in this family
> carries, and how it is checked, is prescribed in
> [`docs/repository-conventions.md`](https://github.com/JanusMael/Bennewitz.Ninja.Templates/blob/main/docs/repository-conventions.md)
> in Bennewitz.Ninja.Templates.

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
promoted to a rule, and the entry keeps a line pointing at the rule's id, as `XQ1004`'s does.
`XQ1001` and `XQ1002` began as guards hand-rolled in application test suites, not as entries.
Other repositories contribute entries by message and cite the document by path.

## Who consumes this

Other repositories in the same family consume both packages. ScopedEditors' tests run the rules, and
DiffView runs the audit and commits its report. That is why the public surface is treated as a
contract. `PROGRESS.md` lists which members each consumer uses.

## Releases

The release workflow publishes through nuget.org trusted publishing (OIDC), so no API key exists.
The version is the tag, in the family's `YYYY.Q.MMDD` form. That allows one release per calendar
day, and a published version can never be replaced. `docs/publishing.md` is the runbook.

## Operational rules

This half is **fact-shaped**: every claim cites a file, a type, a member or a test, so drift surfaces
as a missing symbol under `grep` rather than as stale prose. No source-line numbers, no dates, no
counts.

### 1. Hard invariants

| Invariant | Failure signature if broken | Canonical source |
|---|---|---|
| The rules library takes no package reference and no framework dependency | A consumer's test project inherits dependencies it did not choose, and the rules stop working against WPF or MAUI markup | `src/XamlQuality/XamlQuality.csproj`; `Directory.Packages.props`, whose `Tool` group is for the CLI only; test `AssemblyQualityTests.AQ1003_no_assembly_references_a_framework_or_another_family_package` |
| Every shipped assembly passes the family's AssemblyQuality rules, and the scan finds each project under `src` by the assembly name its csproj declares | A package ships a violation of a rule nothing ran, or a scan that silently drops `ThemeAudit.dll` reads as clean | `AssemblyQualityTests`, one test per rule and `Every_shipped_assembly_is_in_the_scan`; `Directory.Packages.props`, whose `Testing` group pins `Bennewitz.Ninja.AssemblyQuality` |
| A rule returns findings; it never throws or asserts on a violation | The library chooses the consumer's test runner and severity for them | `IXamlRule`, `XamlRuleResult`, `XamlFinding` |
| Every result reports `Inspected`, and a rule names what it saw but could not check in `Skipped` | A rule that checked nothing returns zero findings, which reads as a clean codebase | `XamlRuleResult.Inspected`, `XamlRuleResult.Skipped`, `XamlSkip`; tests `ExpanderAutomationNameRuleTests.MarkupWithNoExpanders_InspectsNothingAndReportsNothing`, `GridSlotOverflowRuleTests.MarkupWithNoGrid_InspectsNothing`, `TemplatePartRuleTests.WithoutAssemblies_EveryThemedControlIsNamedAsSkipped` |
| Elements are matched by local name, never by qualified name | A rule goes silently inert on the frameworks whose markup namespace it did not name | `InteractiveAutomationNameRule` remarks; test `ExpanderAutomationNameRuleTests.WpfMarkup_IsScannedToo` |
| An attached property is read in both spellings, attribute and property element | Whatever is written in the unread spelling passes | `AutomationName.IsDeclaredOn`; `GridSlotOverflowRule` (its private `Definitions`); tests `InteractiveAutomationNameRuleTests.ThePropertyElementSpelling_Counts`, `GridSlotOverflowRuleTests.ThePropertyElementSpellingOfRowDefinitions_Counts` |
| An empty automation name is not a name, in either spelling | `Name=""` or an empty `<AutomationProperties.Name/>` passes, and a screen reader announces nothing | `AutomationName.IsDeclaredOn` and its private `HasContent`; tests `InteractiveAutomationNameRuleTests.AnEmptyAutomationName_IsNotAName`, `InteractiveAutomationNameRuleTests.AnEmptyPropertyElement_IsNotAName`, `ExpanderAutomationNameRuleTests.AnExpanderNamedByAnEmptyPropertyElement_IsReported` |
| A scan never reads `bin` or `obj` | Every violation is reported twice, and one keeps being reported after the source is fixed | `XamlScanContext.Load` (`excludedDirectories`); test `ExpanderAutomationNameRuleTests.MarkupUnderObj_IsNotScanned` |
| Unparseable markup is surfaced, never skipped | A broken file full of violations reports zero | `XamlFile.ParseError`, `XamlScanContext.ParsedFiles` |
| A scan context is immutable; `WithAssemblies` returns a new one | A context handed to two rules changes under the second | `XamlScanContext.WithAssemblies` |
| `XQ1002` leaves `Expander` to `XQ1001` | One unnamed Expander is reported twice | `InteractiveAutomationNameRule.FrameworkInteractiveElements`; test `InteractiveAutomationNameRuleTests.Expander_IsLeftToItsOwnRule` |
| Adding a name to `FrameworkInteractiveElements` is a behaviour change for every consumer | Markup that passed turns red on a consumer's next upgrade, unannounced | `InteractiveAutomationNameRule.FrameworkInteractiveElements` remarks; test `InteractiveAutomationNameRuleTests.TheFrameworkSet_IsNotSilentlyEmpty` |
| `XQ1003` counts a `PART_` literal only when the next call is a lookup whose name starts with `Find` or `Get`, and never the bare prefix | Compiled XAML's element-name registrations read as parts, and compiled theme classes show up as controls | `TemplatePartRule` (its private `IsLookup` and `IsPartName`), `CompiledStrings.LoadedBy`; tests `TemplatePartRuleTests.ALiteralThatNamesAnElement_IsNotALookup`, `TemplatePartRuleTests.ANameBuiltAtRuntime_IsNotReadAsAPart` |
| `XQ1003` reads every type an assembly loads, public or not, and credits a lambda's literals to the control that declares it | A part looked up from an internal control, or inside a lambda, goes unchecked | `TemplatePartRule` (its private `TypesOf` and `ReadControls`); tests `TemplatePartRuleTests.AnInternalControlWithAPrivateConstant_IsChecked`, `TemplatePartRuleTests.ALiteralInsideALambda_IsCreditedToItsControl` |
| `XQ1003` matches parts inside their own `ControlTheme`, and reports only a part the code looks up and the theme omits | One theme satisfies another's lookups, or a name that exists only for a style selector is reported as a defect | `TemplatePartRule` remarks; tests `TemplatePartRuleTests.OneThemeDoesNotSatisfyAnother_InTheSameFile`, `TemplatePartRuleTests.APartNamedOnlyForAStyleSelector_IsNotAViolation` |
| IL is decoded instruction by instruction, each operand stepped over by its declared size | An operand byte that happens to equal the `ldstr` opcode is misread as a string load | `CompiledStrings` remarks |
| `XQ1004` checks fixed slot sizes only | False positives on `*` and `Auto` rows, which markup cannot resolve | `GridSlotOverflowRule` remarks; tests `GridSlotOverflowRuleTests.AStarRow_IsNotDecidableAndIsNotReported`, `GridSlotOverflowRuleTests.AnAutoRow_IsNotDecidableAndIsNotReported` |
| `XQ1004` measures a control where the framework's `Grid` places it: an index past the last definition in the last one, and a span across every fixed slot it crosses plus the spacing between them, clamped at the edge | A control that fits its span is reported against its first slot, or one placed past the end escapes the check while counting as inspected | `GridSlotOverflowRule` remarks; tests `GridSlotOverflowRuleTests.AChildThatFitsTheColumnsItSpans_IsNotReported`, `GridSlotOverflowRuleTests.AnIndexPastTheLastColumn_IsMeasuredAgainstTheLastColumn`, `GridSlotOverflowRuleTests.ASpanPastTheLastColumn_IsClampedToTheColumnsThatRemain` |
| A rule id is permanent public API | Any suppression keyed on the old id silently stops applying | `IXamlRule.Id`; test `RulesCatalogTests.EveryRuleId_IsUnique` |
| Every rule type is public and has a parameterless constructor | The rule vanishes from the README table, or generating the table throws | tests `RulesCatalogTests.EveryRuleType_IsPublic`, `RulesCatalogTests.TheReadmeRulesTable_IsWhatTheRuleTypesSay` |
| The README's rules table, between the `BEGIN GENERATED RULES` and `END GENERATED RULES` markers, is generated; the prose outside them is hand-written | A hand edit to the table fails the build, and a generator that owned the whole section would delete the prose about how rules overlap | `RulesCatalogTests` |
| The rule API and the whole ThemeAudit audit surface are public contracts | Narrowing a member breaks a consumer the next time it moves off its pinned version | `src/XamlQuality/Properties/AssemblyInfo.cs`; the consumers, and the members they use, in `PROGRESS.md` |
| The content digest is rooted at the deepest directory the scanned files share, never at the configuration's directory | The same files hash differently on a developer machine and in CI, so a committed report can never pass in both | `AuditRunner.CommonRoot`, `AuditRunner.Digest`; tests `DigestLocationTests.TheSameContentInTwoLocations_HashesTheSame`, `DigestLocationTests.ARenameThatDoesNotReorder_StillMovesTheDigest` |
| The theme mappings reach a consumer's output as `Mappings/<name>.json` | `"mapping": "FluentToSemi"` is a file-not-found for every `PackageReference` consumer | The `None` item in `src/XamlQuality/XamlQuality.csproj`, where `Link`, `PackageCopyToOutput` and `PackagePath` are all load-bearing |
| The release names each package it publishes; no step globs `*.nupkg` | A future packable project is published permanently, or attached to a GitHub Release nobody chose it for | `.github/workflows/release.yml`; tests `ReleaseWorkflowTests.The_release_workflow_names_every_package_it_publishes`, `ReleaseWorkflowTests.The_release_workflow_still_has_both_publishing_steps` |
| `NUGET_USER` is a repository variable holding the nuget.org profile name, and a release refuses to run without it | A masked value hides why a login fails, and a tag with it unset creates a GitHub Release for a package that never shipped | `.github/workflows/release.yml`, step `Refuse to release without NUGET_USER`; `docs/publishing.md` |

### 2. Commands

```bash
dotnet build XamlQuality.slnx -c Release
dotnet test --solution XamlQuality.slnx
XQ_UPDATE_DOCS=1 dotnet test --solution XamlQuality.slnx
dotnet pack XamlQuality.slnx -c Release -p:Version=0.0.0-local --output ./packages/local
```

- The third regenerates the README's rules table (`RulesCatalogTests`). Commit the result; the build
  never writes a tracked file.
- Tests run on Microsoft.Testing.Platform (`global.json`), so `dotnet test` takes `--solution` and
  rejects VSTest-only switches such as `--nologo`.
- Write `-p:` rather than `/p:`. The workflows use `/p:` because they run on Linux; Git Bash on
  Windows rewrites a leading-slash argument into a path.
- Warnings are errors (`Directory.Build.props`), and CI builds and tests on Linux, Windows and macOS
  because the rules compare paths (`.github/workflows/ci.yml`).

### 3. Checklists

#### Adding a rule

1. A public sealed class in `src/XamlQuality/Rules/` implementing `IXamlRule`, with a parameterless
   constructor. Take the next free `XQ` id. Ids are permanent and assigned only when a rule is
   written: candidates in `PROGRESS.md`'s backlog have none.
2. `Summary` states what the rule requires, not the violation (`IXamlRule.Summary`).
3. Count what was examined in `Inspected`. Name in `Skipped` what the rule saw but could not check.
4. Tests in `tests/XamlQuality.Tests/Rules/`: the violation, the clean case, both spellings wherever
   a property is involved, the cases where the rule must stay silent, and one asserting `Inspected`
   is zero.
5. Prove each test can fail. Remove the behaviour it covers, watch exactly that test fail, then
   restore it by the inverse edit.
6. Regenerate the README table. Prose about how the rule overlaps another goes outside the generated
   markers.
7. Run it on real code before it lands: a consumer's markup and, for a rule that reads assemblies,
   its build output. Fixtures written from the report that motivated a rule reproduce the report,
   not the codebase.
8. If the rule began as an entry in `docs/avalonia-gotchas.md`, leave a line in that entry naming the
   id.
9. Add it to `PROGRESS.md`'s unreleased list, with its effect on a consumer.

#### Changing a rule or the audit

- A rule that becomes stricter turns a consumer's green markup red on their next upgrade. Record it
  in `PROGRESS.md`'s unreleased list, under "Effect on a consumer".
- A change to how the digest is computed rebases every committed report once, and each consumer must
  regenerate theirs. Record that too.
- Keep `src/XamlQuality/Properties/AssemblyInfo.cs` accurate when a type's visibility changes.

#### Editing the documents

- `docs/ai-drivable-ui.md` is a copy of TailBlazer's `Documents/AiDrivableUi.md` and stays
  byte-identical to it. Change it only by applying the same change to every copy.
- `docs/avalonia-gotchas.md` is cited by path from other repositories, listed in `PROGRESS.md`.
  Moving it, or retitling an entry they cite, means updating them.
- An entry that becomes mechanically checkable is promoted to a rule, as that document's header
  requires.

#### Releasing

- A tag publishes permanently. Tag only when the developer has decided to release.
- Follow `docs/publishing.md`: tag `vYYYY.Q.MMDD`, one release per calendar day, verify against the
  nuget.org flat-container API rather than the workflow, and never re-tag to fix a failed run.
- Afterwards, move the released rows out of `PROGRESS.md`'s unreleased list and update its
  published version.
