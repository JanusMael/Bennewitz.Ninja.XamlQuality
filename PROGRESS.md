# Progress

The work state of `Bennewitz.Ninja.XamlQuality`: what is published, what is on `main` and not yet
released, who depends on what, and the rule backlog with any questions open for the developer.

## Status

| | |
|---|---|
| Published | `2026.3.925`: `Bennewitz.Ninja.XamlQuality` and `Bennewitz.Ninja.XamlQuality.ThemeAudit`, released 2026-09-25. What it changed for a consumer, with the old and new rule ids, is in its [release notes](https://github.com/JanusMael/Bennewitz.Ninja.XamlQuality/releases/tag/v2026.3.925) |
| `main` | carries nothing unreleased |
| Next release | Not scheduled; the developer decides. One release per calendar day |

### On `main`, not yet released

Every change after the `v2026.3.925` tag (`bbd7e8c`) that a consumer can see; this repository's own
working documents are left out. A change that makes a rule stricter or adds API goes here with its
effect on a consumer, so the release that carries it can say so. `main` takes squash and rebase
merges only, and both give a branch's commits new hashes, so a row cites its pull request.

| Commit or PR | Change | Effect on a consumer |
|---|---|---|
| PR #34 | `BNXQ1007`, new: every interactive control declares an explicit `AutomationId`, the id a test or an agent searches by, as `docs/ai-drivable-ui.md`'s rule 1 asks. It covers `BNXQ1002`'s framework set and `Expander`, and takes the consumer's own control names. It reads both spellings of `AutomationProperties.AutomationId` and MAUI's plain `AutomationId`. An `x:Name` does not count, and an empty or blank id is reported as empty. Measured on Avalonia 12.1.3 through its runtime XAML loader, `x:Name` alone gives a derived id, an empty attribute gives an empty id that hides it, and an empty property element sets nothing. Rule 1 of the guide now names `BNXQ1007` as its check | New rule: nothing changes until a consumer constructs it. Adopting it means a sweep. Over each repository's GitHub `main`, it reports every control it inspects in ScopedEditors (13, at `4fbca95`), ClaudeForge (309, at `70dc713`) and DiffView (64, at `ec61a10`), all for a missing id and none for an empty one. Templates' `bbavalonia` sets its ids and reads clean, 3 inspected at `061d3ea` |

## Consumers inside the family

`2026.3.925` renamed the rule ids: what `2026.3.924` shipped as `XQ1001` to `XQ1004` is `BNXQ1001`
to `BNXQ1004` from it on. Each entry below keeps the ids of the release it pins, and a consumer that
moves its pin past `2026.3.924` renames them in its test names, messages and comments.

- **ScopedEditors' tests** pin `2026.3.922`, test-only. They use
  `InteractiveAutomationNameRule(additionalElements)`, `ExpanderAutomationNameRule`,
  `XamlScanContext.Load`, `XamlFile.RelativePath` / `.Text` / `.ParseError`, and
  `XamlRuleResult.Inspected` / `.Findings`. Narrowing any of them breaks ScopedEditors once it moves
  off `2026.3.922`, and moving to `2026.3.924` or later brings it the stricter XQ1001 and XQ1002.
- **ScopedEditors cites `docs/avalonia-gotchas.md` by path**, in `AppSeverityToGlyphConverter.cs` and
  its tests, `PropertyEditorWrapper.axaml` twice, and `DangerSurfaceMarkupTests.cs`. They rely on the
  entries about emoji-font fallback and about `AutomationProperties.Name` being ignored on a
  `TextBlock`. Moving or renaming the file or those entries means updating them.
- **The documents in `docs/` are the only copies of themselves**, by the owner's decision of
  2026-09-24. TailBlazer retired its copy of the guide (`8e46a8f`) and points its `CLAUDE.md` at both
  documents here. Templates' `PROGRESS.md` links the gotchas. ClaudeForge retired its pre-move
  `docs/AVALONIA-GOTCHAS.md` and repointed its links here in JanusMael/ClaudeForge#80, and its UI
  style guide's section 14 points here since JanusMael/ClaudeForge#87. bb-skills' drivable-ui skill
  points at the guide here and keeps no copy (`addad02`, on a local branch). Moving either file, or
  retitling a section or entry someone cites, means telling them.
- **OpenForge2k's tests**, jmui's `ClaudeForge.Tests`, pin `2026.3.924` on `main`, test-only, since
  JanusMael/ClaudeForge#77 merged on 2026-09-24, and so have the stricter XQ1001. They use
  `XamlScanContext.Load`, `ExpanderAutomationNameRule`, and `XamlRuleResult.Inspected` /
  `.Findings`.
- **Templates' `bbavalonia` template** pins `2026.3.924` for the apps it generates, test-only. Their
  `AutomationNameTests` use `XamlScanContext.Load` and `.Files`, `InteractiveAutomationNameRule()`,
  `ExpanderAutomationNameRule`, and `XamlRuleResult.Inspected` / `.Findings`, and name XQ1001 and
  XQ1002 in their test names and messages. Its `AGENTS.md` sends Avalonia and drivable-UI lessons
  here.
- **DiffView**'s tests pin `2026.3.924` on its `main` (`ec61a10`), test-only. They run XQ1002 with
  DiffView's own element names beside `InteractiveAutomationNameRule.FrameworkInteractiveElements`,
  and XQ1003 and XQ1004. DiffView also runs the audit and commits its report.
  - XQ1003 reads 34 by its own report, against a floor of 20, deliberately below its population: the
    floor guards the silent `Clean(0)` of a scan handed no assemblies, not a part count. It asserts
    XQ1003's `Skipped` subjects are exactly `DiffPaneHeader` and `DiffStatusStrip`, keyed on
    `XamlSkip.Subject` rather than the prose `Reason`, so narrowing either breaks it.
  - `GridSlotTests` floors XQ1004's `Inspected` at 12 and asserts its `Skipped` is empty. Measured on
    `ec61a10`'s markup, the published rule inspects 22 placements at `2026.3.924` and 0 at
    `2026.3.925`, with no findings or skips at either, because `2026.3.925` counts only a control it
    measured against fixed slots. Moving DiffView's pin fails that floor until it is re-set.
  - Its regenerated audit report's digest came out at the predicted `b7ea0ec438c5`, with nothing but
    digests moving, so `4f7bd62`'s diagnosis was complete. It proposed the peer check, written as
    `BNXQ1006` in PR #27, and the reading of a helper's lookups that `BNXQ1003` gained in PR #30.

## Rule backlog

Ids are permanent, and the README's rules table is generated from them, so an id is assigned only
when a rule is written.

The rules are written in the table's order, decided on 2026-09-23. `BNXQ1005`, `BNXQ1006`, the peer
check, and `BNXQ1007`, the explicit `AutomationId`, are written, and the zero-size slot is next.

| Candidate | Source | Fit | What it checks | What it needs |
|---|---|---|---|---|
| A zero-size Grid slot whose child sets no `IsVisible` | `ai-drivable-ui.md`, rule 6 | Partial | A child of a row or column with a literal `Height="0"` or `Width="0"` | Markup only. BNXQ1004 already covers the min-size half; bound sizes and clipping cannot be decided from markup |
| Containers generated from `ItemsSource` and named by `ToString()` | `ai-drivable-ui.md`, rule 2 | Partial | The `ItemTemplate`'s `x:DataType` overrides `ToString()`, or a container `Style` sets the name | Types. `TreeViewItem` ignores `ToString()` (measured, in `avalonia-gotchas.md`), so a tree needs the `Style` |

**Not a fit:** the guide's rule 10, keeping popups in the window's tree. Avalonia's `OverlayPopups` is
set in C# at startup, where no markup scan can see it. That check belongs in the application, as a
startup assertion or a headless test.

## Questions for the developer

None open.

## Follow-ups

- **BNXQ1004 reads four things more narrowly than the framework.** Found while building PR #20, and
  left for a change of their own, because each can add findings or skips on a consumer's markup:
  - The shorthand is split at commas only. Avalonia's parser also splits at whitespace
    (`GridLength.ParseLengths`, read from source), so `ColumnDefinitions="Auto 16 *"` reads as one
    star column.
  - `Grid.Row`, `Grid.Column` and the spans are read only as attributes, so one written as a
    property element is measured as row or column 0.
  - A control that sets a literal `MinWidth` and a larger literal `Width` is measured by its
    `MinWidth`. The framework arranges it at the larger, unless a `MaxWidth` caps it.
  - WPF's unit suffixes (`px`, `in`, `cm`, `pt`) are not read, so a size written with one is named
    in `Skipped`.
