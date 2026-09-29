# Progress

The work state of `Bennewitz.Ninja.XamlQuality`: what is published, what is on `main` and not yet
released, who depends on what, and the rule backlog with any questions open for the developer.

## Status

| | |
|---|---|
| Published | `2026.3.928`: `Bennewitz.Ninja.XamlQuality` and `Bennewitz.Ninja.XamlQuality.ThemeAudit`, released 2026-09-28. What it changed for a consumer is in its [release notes](https://github.com/JanusMael/Bennewitz.Ninja.XamlQuality/releases/tag/v2026.3.928) |
| `main` | carries nothing unreleased |
| Next release | Not scheduled; the developer decides. One release per calendar day |

### On `main`, not yet released

Every change after the `v2026.3.928` tag (`067a509`) that a consumer can see; this repository's own
working documents are left out. A change that makes a rule stricter or adds API goes here with its
effect on a consumer, so the release that carries it can say so. `main` takes squash and rebase
merges only, and both give a branch's commits new hashes, so a row cites its pull request.

| Commit or PR | Change | Effect on a consumer |
|---|---|---|
| PR #43 | `docs/avalonia-gotchas.md` gains an AvaloniaEdit entry from DiffView's session: a zero-length element that a host's generator places at a line start is lost where a built-in generator claims the line's first character, because `TextView`'s constructor appends the built-ins first and the first element with a length ends a round of construction. Checked in AvaloniaEdit's source at the `12.0.0` tag, and measured headless on Avalonia.AvaloniaEdit 12.0.0 with Avalonia 12.1.3. The cause held as sent, and the loss is wider: under the default options, lines beginning with a URL or an e-mail address lose the element too | Docs only |

## Consumers inside the family

`2026.3.925` renamed the rule ids: what `2026.3.924` shipped as `XQ1001` to `XQ1004` is `BNXQ1001`
to `BNXQ1004` from it on. Each entry below keeps the ids of the release it pins, and a consumer that
moves its pin past `2026.3.924` renames them in its test names, messages and comments.

- **ScopedEditors' tests** pin `2026.3.928`, test-only, since JanusMael/Bennewitz.Ninja.ScopedEditors#10,
  which records the rules new in it as the owner's call. They run BNXQ1001 and BNXQ1002, through
  `InteractiveAutomationNameRule(additionalElements)`, `ExpanderAutomationNameRule`,
  `XamlScanContext.Load`, `XamlFile.RelativePath` / `.Text` / `.ParseError`, and
  `XamlRuleResult.Inspected` / `.Findings`; narrowing any of them breaks ScopedEditors. By its own
  report its move to `2026.3.925` was clean but for the renames: its markup writes no empty
  `<AutomationProperties.Name>`, so `2026.3.924`'s stricter names found nothing there.
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
- **OpenForge2k's tests**, jmui's `ClaudeForge.Tests`, pin `2026.3.925` on `main`, test-only, since
  JanusMael/ClaudeForge#91 merged on 2026-09-25, and run BNXQ1001 alone. They use
  `XamlScanContext.Load`, `ExpanderAutomationNameRule`, and `XamlRuleResult.Inspected` /
  `.Findings`.
- **Templates' `bbavalonia` template** pins `2026.3.928` for the apps it generates, test-only, since
  JanusMael/Bennewitz.Ninja.Templates#17, published in Templates `2026.3.928`. Their
  `AutomationNameTests` use `XamlScanContext.Load` and `.Files`, `InteractiveAutomationNameRule()`,
  `InteractiveAutomationIdRule()`, `ExpanderAutomationNameRule`, and `XamlRuleResult.Inspected` /
  `.Findings`, and name BNXQ1001, BNXQ1002 and BNXQ1007 in their test names and messages. Its
  `AGENTS.md` sends Avalonia and drivable-UI lessons here.
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
and `BNXQ1009`, the `ToString()` containers. One candidate has come in since.

| Candidate | Source | Fit | What it checks | What it needs |
|---|---|---|---|---|
| A row that takes no click where it draws nothing | `avalonia-gotchas.md`, the entry on a row whose `Background` is null | Measured; its shape decided on 2026-09-28, but for `TreeViewItem` | A `ListBoxItem`, `ComboBoxItem` or `TabItem` that draws nothing across its width: a style or container theme setting `Background` to `{x:Null}`, a container theme not `BasedOn` another that sets no `Background`, a container template whose root draws no background, or, by the developer's decision, a bound `Background` with no non-null `TargetNullValue` | Measured on Avalonia 12.1.3 under Fluent, Simple and Semi 12.1.0.1, by a click where only a row's background can be hit. On a `ListBoxItem`, its markup loaded through the runtime XAML loader, each of the first three leaves the click unanswered, the third even when the row's `Background` is `Transparent`, and so does a binding to a null source or through a converter answering null. `TargetNullValue` rescued both of those, and `FallbackValue` neither. `ComboBoxItem` and `TabItem` are `Transparent` stock and miss the click with a null background, as `ListBoxItem` does, in all three themes. `TreeViewItem` does not follow them: Fluent's and Simple's templates give its header presenter a `Transparent` background of its own, by their source at 12.1.3, so a null row background still takes the click there, while under Semi it misses. The open question is below |

**Not a fit:** the guide's rule 10, keeping popups in the window's tree. Avalonia's `OverlayPopups` is
set in C# at startup, where no markup scan can see it. That check belongs in the application, as a
startup assertion or a headless test.

## Questions for the developer

- **Does the row-background rule leave `TreeViewItem` out?** Whether a null row background loses
  the click depends on the theme's template: it does under Semi, and not under Fluent or Simple.
  Recommended: leave it out, and say so in the rule's remarks.

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
- **Trimming is being made a family policy, with Templates.** The developer's model, 2026-09-28: a
  repository that trims sets `IsTrimmable` and `EnableTrimAnalyzer`; a family library it depends on
  must then be trimmable too; with neither property set, nothing is enforced. Today the shared
  conventions checker, copied here from Templates and never edited here, notes every library
  without them. XamlQuality is used only from test projects, so it owes nothing under the policy,
  and the note goes when the agreed change copies back. The two sides are being split between this
  repository's session and Templates'.
- **`BNXQ1009` reads an item type only from an item template's `x:DataType`.** A list with no item
  template, or a template that declares none, is named in `Skipped`, as all 6 of ClaudeForge's skips
  are. Reading the type from the `ItemsSource` binding's path, through the view's own `x:DataType`,
  would decide most of them. It can add findings, so it is a change of its own. It is next, by the
  developer's decision of 2026-09-28, after the row-background investigation.
- **`BNXQ1009` checks Avalonia's rows only.** WPF's `ListBoxItem` is named through a different peer,
  whose fallback was not measured. Measuring it is what would let the rule check WPF markup.
