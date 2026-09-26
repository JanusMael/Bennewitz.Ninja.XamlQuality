# Progress

The work state of `Bennewitz.Ninja.XamlQuality`: what is published, what is on `main` and not yet
released, who depends on what, and the rule backlog with any questions open for the developer.

## Status

| | |
|---|---|
| Published | `2026.3.925`: `Bennewitz.Ninja.XamlQuality` and `Bennewitz.Ninja.XamlQuality.ThemeAudit`, released 2026-09-25. What it changed for a consumer, with the old and new rule ids, is in its [release notes](https://github.com/JanusMael/Bennewitz.Ninja.XamlQuality/releases/tag/v2026.3.925) |
| `main` | carries what the next section lists, not yet released |
| Next release | Not scheduled; the developer decides. One release per calendar day |

### On `main`, not yet released

Every change after the `v2026.3.925` tag (`bbd7e8c`) that a consumer can see; this repository's own
working documents are left out. A change that makes a rule stricter or adds API goes here with its
effect on a consumer, so the release that carries it can say so. `main` takes squash and rebase
merges only, and both give a branch's commits new hashes, so a row cites its pull request.

| Commit or PR | Change | Effect on a consumer |
|---|---|---|
| PR #34 | `BNXQ1007`, new: every interactive control declares an explicit `AutomationId`, the id a test or an agent searches by, as `docs/ai-drivable-ui.md`'s rule 1 asks. It covers `BNXQ1002`'s framework set and `Expander`, and takes the consumer's own control names. It reads both spellings of `AutomationProperties.AutomationId` and MAUI's plain `AutomationId`. An `x:Name` does not count, and an empty or blank id is reported as empty. Measured on Avalonia 12.1.3 through its runtime XAML loader, `x:Name` alone gives a derived id, an empty attribute gives an empty id that hides it, and an empty property element sets nothing. Rule 1 of the guide now names `BNXQ1007` as its check | New rule: nothing changes until a consumer constructs it. Adopting it means a sweep. Over each repository's GitHub `main`, it reports every control it inspects in ScopedEditors (13, at `4fbca95`), ClaudeForge (309, at `70dc713`) and DiffView (64, at `ec61a10`), all for a missing id and none for an empty one. Templates' `bbavalonia` sets its ids and reads clean, 3 inspected at `061d3ea` |
| PR #35 | `BNXQ1008`, new: every control in a zero-size `Grid` slot is hidden by `IsVisible`, not by the slot alone, as `docs/ai-drivable-ui.md`'s rule 6 asks. It covers a control that asks for no size in that direction, in fixed slots that add up to none, and leaves one that asks for a size to `BNXQ1004`. A slot's size is read as the framework arranges it: its own `MinHeight` first, then a `MaxHeight` of 0, then a star of weight 0, and a span has the spacing between its slots. Any `IsVisible` but a literal `True` answers it, and so does WPF's `Visibility` but `Visible`. A slot whose size markup cannot evaluate is named in `Skipped`. Measured on Avalonia 12.1.3 in a headless window: a `Button` in a row of `Height="0"` is arranged 0 tall and stays in the automation tree, reporting `IsOffscreen` false, a Fluent `TextBox` there is arranged 32 tall over its neighbours, and `IsVisible="False"` takes the `Button` out of the tree. `BNXQ1004`'s reading of a grid moves into `GridLayout`, which both rules share, unchanged: its output over the family's markup and Avalonia's repository is byte for byte the same. Rule 6 of the guide, and the layout-clip entry in `docs/avalonia-gotchas.md`, now name `BNXQ1008` | New rule: nothing changes until a consumer constructs it. Over each repository's GitHub `main` it finds nothing: ClaudeForge inspects 32 (`70dc713`), DiffView 3 (`ec61a10`), and ScopedEditors (`c31b8cb`) and Templates (`061d3ea`) none. Over Avalonia's own repository it inspects 134 and names 4 in `Skipped`: the `SplitView` templates, Fluent and Simple, size the pane's column or row by a binding |
| PR #37 | A theme's setter whose value is written as its content, as in `<Setter Property="Template"><ControlTemplate>`, is read. `Value` is `Setter`'s content property, and Avalonia's own themes write their templates that way, but the reading `BNXQ1005` shares with `BNXQ1009`, `ThemeReader`, took such a value for empty | `BNXQ1005`: a theme that sets `Focusable`, `ItemContainerTheme`, an item template, a template or a content template as a setter's content was named in `Skipped`, as a value markup cannot evaluate. It is now followed, so such a skip can become a verdict, a finding among them. No consumer in the family runs `BNXQ1005` yet, and over ClaudeForge's markup and build output (`70dc713`) it inspects and skips nothing, before and after |
| PR #37 | `BNXQ1009`, new: every item container generated from `ItemsSource` is named by what it shows, not by its item's type, as `docs/ai-drivable-ui.md`'s rule 2 asks. A `ListBoxItem`, `ComboBoxItem` or `TabItem` that nothing names falls back to `ToString()` on its item, and a `TreeViewItem` to nothing. In the framework's order, the rule reads a style reaching the host whose selector is the container type alone, the `ItemContainerTheme` or implicit container theme, `DisplayMemberBinding`, and the item template's root. Last it reads the `ToString()` that the template's `x:DataType` resolves to, and reports `object`'s, `ValueType`'s or a record's. A row is recognised by the peer its container creates, read from IL, so another framework's rows are left alone. Measured on Avalonia 12.1.3 in a headless window, for every case the tests take. `ResourceScope` gains the styles that reach an element, and `ElementTypes` a type by namespace and name. `BNXQ1005`'s theme reading moves, unchanged, into the `ThemeReader` both rules share. The three gotchas entries on generated containers, and rule 2 of the guide, now name `BNXQ1009` | New rule: nothing changes until a consumer constructs it. Over ClaudeForge's GitHub `main` (`70dc713`) and its build output it inspects 14, finds nothing, and names in `Skipped` 6 `ComboBox`es with no item template. At `7c110d4`, before ClaudeForge's two fixes, it reports exactly the six controls those fixes went on to change: two navigation trees whose rows UI Automation read as empty, and four lists whose rows a view-model type named, one of them read through UI Automation |

## Consumers inside the family

`2026.3.925` renamed the rule ids: what `2026.3.924` shipped as `XQ1001` to `XQ1004` is `BNXQ1001`
to `BNXQ1004` from it on. Each entry below keeps the ids of the release it pins, and a consumer that
moves its pin past `2026.3.924` renames them in its test names, messages and comments.

- **ScopedEditors' tests** pin `2026.3.925`, test-only, since JanusMael/Bennewitz.Ninja.ScopedEditors#7.
  They run BNXQ1001 and BNXQ1002, through `InteractiveAutomationNameRule(additionalElements)`,
  `ExpanderAutomationNameRule`, `XamlScanContext.Load`, `XamlFile.RelativePath` / `.Text` /
  `.ParseError`, and `XamlRuleResult.Inspected` / `.Findings`; narrowing any of them breaks
  ScopedEditors. By its own report the move was clean but for the renames: its markup writes no
  empty `<AutomationProperties.Name>`, so `2026.3.924`'s stricter names found nothing there.
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
- **Templates' `bbavalonia` template** pins `2026.3.925` for the apps it generates, test-only, since
  JanusMael/Bennewitz.Ninja.Templates#13. Their `AutomationNameTests` use `XamlScanContext.Load` and
  `.Files`, `InteractiveAutomationNameRule()`, `ExpanderAutomationNameRule`, and
  `XamlRuleResult.Inspected` / `.Findings`, and name BNXQ1001 and BNXQ1002 in their test names and
  messages. Its `AGENTS.md` sends Avalonia and drivable-UI lessons here.
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

The rules were written in the order decided on 2026-09-23, and all of them are now: `BNXQ1005`,
`BNXQ1006`, the peer check, `BNXQ1007`, the explicit `AutomationId`, `BNXQ1008`, the zero-size slot,
and `BNXQ1009`, the `ToString()` containers. No candidate is open.

**Not a fit:** the guide's rule 10, keeping popups in the window's tree. Avalonia's `OverlayPopups` is
set in C# at startup, where no markup scan can see it. That check belongs in the application, as a
startup assertion or a headless test.

## Questions for the developer

None open.

## Follow-ups

- **BNXQ1004 reads five things more narrowly than the framework, and `BNXQ1008` shares three of
  them through `GridLayout`.** The first four were found while building PR #20, and each is left
  for a change of its own, because each can add findings or skips on a consumer's markup:
  - The shorthand is split at commas only, in both rules. Avalonia's parser also splits at
    whitespace (`GridLength.ParseLengths`, read from source), so `ColumnDefinitions="Auto 16 *"`
    reads as one star column.
  - `Grid.Row`, `Grid.Column` and the spans are read only as attributes, in both rules, so one
    written as a property element is placed in row or column 0.
  - A control that sets a literal `MinWidth` and a larger literal `Width` is measured by its
    `MinWidth`. The framework arranges it at the larger, unless a `MaxWidth` caps it.
  - WPF's unit suffixes (`px`, `in`, `cm`, `pt`) are not read, in both rules, so a size written with
    one is named in `Skipped`.
  - A definition's own `MinHeight` and `MaxHeight`, and a star of weight 0, are read by `BNXQ1008`
    and not by `BNXQ1004`. Measured on Avalonia 12.1.3 in PR #35, a row of `Height="0"
    MinHeight="20"` is arranged 20 tall, a `MaxHeight` of 0 empties any row, and `0*` gets nothing.
    `GridLayout.SlotBounds` already carries them, so the change is `BNXQ1004` measuring against
    them too.
- **`BNXQ1009` reads an item type only from an item template's `x:DataType`.** A list with no item
  template, or a template that declares none, is named in `Skipped`, as all 6 of ClaudeForge's skips
  are. Reading the type from the `ItemsSource` binding's path, through the view's own `x:DataType`,
  would decide most of them. It can add findings, so it is a change of its own.
- **`BNXQ1009` checks Avalonia's rows only.** WPF's `ListBoxItem` is named through a different peer,
  whose fallback was not measured. Measuring it is what would let the rule check WPF markup.
