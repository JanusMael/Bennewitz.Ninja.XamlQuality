# AGENTS.md — invariants and checklists for XamlQuality

> Audience: an agent (Claude or otherwise) returning to this repository cold. Purpose: the contracts
> a single-file read does not show, so a change does not break one by accident. The narrative, what
> the repository is and why it is shaped this way, is in [`CLAUDE.md`](./CLAUDE.md). Work state is
> [`PROGRESS.md`](./PROGRESS.md).

This file is **fact-shaped**: every claim cites a file, a type, a member or a test, so drift surfaces
as a missing symbol under `grep` rather than as stale prose. No source-line numbers, no dates, no
counts.

## 1. Hard invariants

| Invariant | Failure signature if broken | Canonical source |
|---|---|---|
| The rules library takes no package reference and no framework dependency | A consumer's test project inherits dependencies it did not choose, and the rules stop working against WPF or MAUI markup | `src/XamlQuality/XamlQuality.csproj`; `Directory.Packages.props`, whose `Tool` group is for the CLI only |
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
| A rule id is permanent public API | Any suppression keyed on the old id silently stops applying | `IXamlRule.Id`; test `RulesCatalogTests.EveryRuleId_IsUnique` |
| Every rule type is public and has a parameterless constructor | The rule vanishes from the README table, or generating the table throws | tests `RulesCatalogTests.EveryRuleType_IsPublic`, `RulesCatalogTests.TheReadmeRulesTable_IsWhatTheRuleTypesSay` |
| The README's rules table, between the `BEGIN GENERATED RULES` and `END GENERATED RULES` markers, is generated; the prose outside them is hand-written | A hand edit to the table fails the build, and a generator that owned the whole section would delete the prose about how rules overlap | `RulesCatalogTests` |
| The rule API and the whole ThemeAudit audit surface are public contracts | Narrowing a member breaks a consumer the next time it moves off its pinned version | `src/XamlQuality/Properties/AssemblyInfo.cs`; the consumers, and the members they use, in `PROGRESS.md` |
| The content digest is rooted at the deepest directory the scanned files share, never at the configuration's directory | The same files hash differently on a developer machine and in CI, so a committed report can never pass in both | `AuditRunner.CommonRoot`, `AuditRunner.Digest`; tests `DigestLocationTests.TheSameContentInTwoLocations_HashesTheSame`, `DigestLocationTests.ARenameThatDoesNotReorder_StillMovesTheDigest` |
| The theme mappings reach a consumer's output as `Mappings/<name>.json` | `"mapping": "FluentToSemi"` is a file-not-found for every `PackageReference` consumer | The `None` item in `src/XamlQuality/XamlQuality.csproj`, where `Link`, `PackageCopyToOutput` and `PackagePath` are all load-bearing |
| The release names each package it publishes; no step globs `*.nupkg` | A future packable project is published permanently, or attached to a GitHub Release nobody chose it for | `.github/workflows/release.yml`; tests `ReleaseWorkflowTests.The_release_workflow_names_every_package_it_publishes`, `ReleaseWorkflowTests.The_release_workflow_still_has_both_publishing_steps` |
| `NUGET_USER` is a repository variable holding the nuget.org profile name, and a release refuses to run without it | A masked value hides why a login fails, and a tag with it unset creates a GitHub Release for a package that never shipped | `.github/workflows/release.yml`, step `Refuse to release without NUGET_USER`; `docs/publishing.md` |

## 2. Commands

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

## 3. Checklists

### Adding a rule

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

### Changing a rule or the audit

- A rule that becomes stricter turns a consumer's green markup red on their next upgrade. Record it
  in `PROGRESS.md`'s unreleased list, under "Effect on a consumer".
- A change to how the digest is computed rebases every committed report once, and each consumer must
  regenerate theirs. Record that too.
- Keep `src/XamlQuality/Properties/AssemblyInfo.cs` accurate when a type's visibility changes.

### Editing the documents

- `docs/ai-drivable-ui.md` is a copy of TailBlazer's `Documents/AiDrivableUi.md` and stays
  byte-identical to it. Change it only by applying the same change to every copy.
- `docs/avalonia-gotchas.md` is cited by path from other repositories, listed in `PROGRESS.md`.
  Moving it, or retitling an entry they cite, means updating them.
- An entry that becomes mechanically checkable is promoted to a rule, as that document's header
  requires.

### Releasing

- A tag publishes permanently. Tag only when the developer has decided to release.
- Follow `docs/publishing.md`: tag `vYYYY.Q.MMDD`, one release per calendar day, verify against the
  nuget.org flat-container API rather than the workflow, and never re-tag to fix a failed run.
- Afterwards, move the released rows out of `PROGRESS.md`'s unreleased list and update its
  published version.
