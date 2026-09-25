# Progress

The work state of `Bennewitz.Ninja.XamlQuality`: what is published, what is on `main` and not yet
released, who depends on what, and the rule backlog with any questions open for the developer.

## Status

| | |
|---|---|
| Published | `2026.3.924`: `Bennewitz.Ninja.XamlQuality` and `Bennewitz.Ninja.XamlQuality.ThemeAudit`, released 2026-09-24 beside AppServices and ScopedEditors. What it changed for a consumer is in its [release notes](https://github.com/JanusMael/Bennewitz.Ninja.XamlQuality/releases/tag/v2026.3.924) |
| `main` | carries the unreleased changes below |
| Next release | Not scheduled; the developer decides. One release per calendar day |

### On `main`, not yet released

Every change after the `v2026.3.924` tag (`808346b`) that a consumer can see; this repository's own
working documents are left out. A change that makes a rule stricter or adds API goes here with its
effect on a consumer, so the release that carries it can say so. `main` takes squash and rebase
merges only, and both give a branch's commits new hashes, so a row cites its pull request.

| Commit or PR | Change | Effect on a consumer |
|---|---|---|
| `964469f` | `docs/avalonia-gotchas.md` gains two UI Automation entries from the TailBlazor port: before Avalonia 12.1.3 a list's selection reaches a Windows UIA client empty, and as of 12.1.3 a `ListBox`'s `ScrollPattern` is inert | Docs only |
| `9bb7285` | XQ1004 measures a control where the framework's `Grid` places it: an index past the last definition in the last one, and a span across every fixed slot it crosses plus the spacing between them. A span across an `Auto` or `*` slot, or a bound index, span or spacing, is not decidable | A control that fits the slots it spans is no longer reported, which was a false positive in `2026.3.924`. A control with an out-of-range index is now measured against the last slot and may be reported. The family's markup reads the same before and after: 459 controls, 0 findings |
| PR #9 | `docs/avalonia-gotchas.md` stops claiming that `XQ1001` and `XQ1002` began as its entries, and `CompatMapping`'s doc comments say the reviewed mappings ship with the library | Docs only |
| PR #10 | `XQ1005`, new: a key binding on an items control where nothing in its focus path can take focus, meaning the control, its item containers and what its item template holds. It reads compiled types, so it needs `WithAssemblies`, and names in `Skipped` what markup cannot decide. Measured by a real key press on Avalonia 12.1.3 under Fluent, Simple and Semi, it decides 22 of 28 arrangements, each as the key press did, and names the other 6 in `Skipped`: in every theme, 3 of those fire and 3 do not | New rule: nothing changes until a consumer constructs it. On TailBlazer, ClaudeForge, ScopedEditors, AppServices and DiffView, with their build output: 28 controls with key bindings, one on an items control (TailBlazer's log list, read as live), 0 findings, 0 skipped |
| PR #11, PR #12 | `docs/ai-drivable-ui.md` and `docs/avalonia-gotchas.md` become the only copies of themselves, and other repositories point here. The guide gains what TailBlazer relayed: from Avalonia 12 the same peers are published on Linux over AT-SPI, a harness driving them there unverified; a parked harness window takes `WS_EX_NOACTIVATE`; and two costs a harness pays, a search from the desktop root over its descendants, and list rows read without a `CacheRequest`. It stops presenting the method as accessibility work: nothing in it tests screen-reader use, and it asks only that a control's `Name` is filled in beside its `AutomationId` | Docs only |
| PR #13 | Both documents say in their headers that they are the one living copy, owned here, and that other repositories send what they learn to the session working here by message, or by an issue when none is running | Docs only |
| PR #14, PR #15, PR #16 | `docs/ai-drivable-ui/` arrives with TailBlazer's harness artefacts, each adapted and approved by the owner: `Probe-UiTestEnvironment.ps1`, the shared-desktop and Windows Sandbox journals, and `HarnessWindowPlacement.cs` as a worked example. The guide links to each, gains two points on sweeps, and names that folder rather than TailBlazer's unpushed branch as the home of its worked examples. It no longer says an agent-started window never holds the foreground: it does while the session is unlocked and nobody is typing or clicking | Docs only |
| PR #17 | `docs/avalonia-gotchas.md` gains three `ListBox` entries from TailBlazer's port, each re-measured on Avalonia 12.1.3: the selection changes inside the press, between its tunnel and its bubble; Shift-click ranges from an anchor index that a selection made in code moves; and there is no drag selection, because the pressed row keeps the pointer | Docs only |
| PR #18 | `docs/avalonia-gotchas.md` gains a Linux entry on driving an Avalonia application with `xdotool` under XWayland: a menu popup is its own X window, synthetic motion raises no tooltip, and keyboard accelerators do not reach the menu | Docs only |
| PR #19 | The guidance documents stop naming the repositories that sent their entries. Attributions become neutral, and the example code's names and paths become `App`, `HarnessSandbox` and `HARNESS_WINDOW_PARK`. Links into another repository's own documents go. Facts about the family's packages stay | Docs only. A repository citing an entry by heading is unaffected, because no heading changed |
| PR #20 | XQ1004 counts in `Inspected` only a control it measured against fixed slots. One whose fit depends on a value markup cannot evaluate, such as a bound or resource-based definition, index, span, spacing or size, is named in `Skipped` with that value, unless no fixed slot could depend on it. One in an `Auto` or `*` slot, or declaring no size, counts as neither. A `Width` of `NaN` declares no size, where it was reported as one | `Inspected` falls sharply, so a floor on it can fail. On the family's markup, 512 direct Grid children read 461 inspected before and 13 after, with 0 findings either way and 1 skip. Of the 512, 446 declare no size, 9 declare one only where the grid defines no slots, and 43 sit in `Auto` or `*` slots. DiffView's markup reads 0, where it read 22. A finding changes only on a `Width` or `Height` of `NaN`, which the framework reads as unset and is no longer reported, or on a value the framework rejects or a spacing that is not a finite number, which is no longer measured |
| PR #21 | `docs/avalonia-gotchas.md` gains a performance entry from TailBlazer's port: Avalonia has nothing to port WPF's `Timeline.DesiredFrameRate` to, and no public frame-rate cap. It was read in Avalonia's source at 12.1.3, and every line it cites was checked at that tag | Docs only |
| PR #22 | ThemeAudit's digest reads each CRLF pair as LF, and a consumer's files are read in the order of their path below the scanned directory, written with `/`. DiffView found it on its Windows runner: every row cloned there hashed differently from Linux and macOS | A report generated on Windows now matches one generated on Linux or macOS. A digest made from LF checkouts does not move, which a test pins, so a report generated on Linux or macOS keeps its digests. DiffView's one-file row, re-computed from the file, gives its committed `60346377b215` from either line ending. A digest over files that have CRLF where the report was generated moves once, to the value every platform now gives |
| PR #23 | XQ1003 no longer throws on build output whose dependencies do not load. A control the scanned assemblies define but could not load is named in `Skipped` with what stopped it, and one whose code could be read only in part is checked on that part and named for the rest. XQ1005's skip for such a control says it did not load, where it told the consumer to pass the assembly they had passed | A scan of a library's build output without its framework beside it now returns: XQ1003 threw `FileNotFoundException` on ScopedEditors' and DiffView's, and on DiffView's it now names the six themed controls. A theme for a control that did not load, which passed in silence when nothing threw first, is now a skip. A build whose dependencies load reads as before: TailBlazer's gives XQ1003 2 inspected and XQ1005 1, either way. Skip reasons for such controls change wording, and `XamlSkip.Subject` does not |
| PR #24 | `docs/avalonia-gotchas.md` gains a second performance entry from TailBlazer's port: Avalonia has nothing to port WPF's `RenderCapability.Tier` to, and no public member reports whether rendering is accelerated. It was read in Avalonia's source at 12.1.3, and every claim was checked at that tag | Docs only |
| PR #25 | `docs/avalonia-gotchas.md` gains four entries from ClaudeForge's UI style guide, each measured on Avalonia 12.1.3 or read in its source at that tag: two `TextBlock`s in different fonts do not share a baseline, where the `Run`s of one do; Semi's own strings stay Chinese until `SemiTheme.Locale` is set; a `Button` or `CheckBox` names itself with its content's raw text, the access-key underscore and any emoji included; and Avalonia 12 moved drop data to `DataTransfer`. Two entries are corrected: a parent's tooltip covers its children, as it has since 11.1.0, and an `ItemFilter` applies only in `FilterMode.Custom`, so setting `FilterMode = None` before opening does show the whole list | Docs only. Both corrected entries are retitled, so a repository citing "Tooltips don't propagate from child to parent" or "`AutoCompleteBox.ItemFilter` SUPERSEDES `FilterMode`" by heading must update it. ClaudeForge's `AGENTS.md` cites the first, and jmui rewrites that line in the change that repoints its style guide's section 14 here, held until this lands |

## Consumers inside the family

- **ScopedEditors' tests** pin `2026.3.922`, test-only. They use
  `InteractiveAutomationNameRule(additionalElements)`, `ExpanderAutomationNameRule`,
  `XamlScanContext.Load`, `XamlFile.RelativePath` / `.Text` / `.ParseError`, and
  `XamlRuleResult.Inspected` / `.Findings`. Narrowing any of them breaks ScopedEditors once it moves
  off `2026.3.922`, and moving to `2026.3.924` brings it the stricter XQ1001 and XQ1002.
- **ScopedEditors cites `docs/avalonia-gotchas.md` by path**, in `AppSeverityToGlyphConverter.cs` and
  its tests, `PropertyEditorWrapper.axaml` twice, and `DangerSurfaceMarkupTests.cs`. They rely on the
  entries about emoji-font fallback and about `AutomationProperties.Name` being ignored on a
  `TextBlock`. Moving or renaming the file or those entries means updating them.
- **The documents in `docs/` are the only copies of themselves**, by the owner's decision of
  2026-09-24. TailBlazer retired its copy of the guide (`8e46a8f`) and points its `CLAUDE.md` at both
  documents here. Templates' `PROGRESS.md` links the gotchas. ClaudeForge is retiring its pre-move
  `docs/AVALONIA-GOTCHAS.md`, 13 entries behind this one, and repointing its links here, in
  JanusMael/ClaudeForge#80 (open on 2026-09-24).
  bb-skills' drivable-ui skill still carries a copy of the guide, on an unmerged branch. Moving either
  file, or retitling a section or entry someone cites, means telling them.
- **OpenForge2k's tests**, jmui's `ClaudeForge.Tests`, pin `2026.3.924` on `main`, test-only, since
  JanusMael/ClaudeForge#77 merged on 2026-09-24, and so have the stricter XQ1001. They use
  `XamlScanContext.Load`, `ExpanderAutomationNameRule`, and `XamlRuleResult.Inspected` /
  `.Findings`.
- **DiffView**'s tests adopt XQ1003 and XQ1004 on `2026.3.924`, by its own report on 2026-09-24 of
  an upgrade not yet on GitHub. XQ1003 reads 34 against a floor of 20, deliberately below its
  population: the floor guards the silent `Clean(0)` of a scan handed no assemblies, not a part
  count. It asserts XQ1003's `Skipped` subjects are exactly `DiffPaneHeader` and `DiffStatusStrip`,
  keyed on `XamlSkip.Subject` rather than the prose `Reason`, so narrowing either breaks it. XQ1004
  inspects 22 controls with no findings. Its regenerated audit report's digest came out at the
  predicted `b7ea0ec438c5`, with nothing but digests moving, so `4f7bd62`'s diagnosis was complete.
  It proposed the peer check, which is next in the backlog's order.

## Rule backlog

Ids are permanent, and the README's rules table is generated from them, so an id is assigned only
when a rule is written.

The rules are written in the table's order, decided on 2026-09-23. `XQ1005`, first in that order, is
written. The peer check is next: it is the one candidate measured failing on a consumer, and an id
on a control with no peer is as unreachable as a name.

| Candidate | Source | Fit | What it checks | What it needs |
|---|---|---|---|---|
| A custom control that carries an automation id or name but has no peer | `ai-drivable-ui.md`, rule 3; proposed by DiffView | Strong, and measured on a consumer | A themed control whose type overrides no `OnCreateAutomationPeer` below its framework base. DiffView measured all eight of its themed controls resolving to `NoneAutomationPeer`: names set, unreachable by a control-view search, behind a name gate that stayed green | Types, as XQ1003 does, over the same enumeration of themed controls XQ1003 already builds for `Skipped`. Also the framework's peer-less base types named, and an opt-out for a control that really is decorative, which custom drawing alone does not make it (`ai-drivable-ui.md`) |
| An explicit `AutomationId` on every interactive control | `ai-drivable-ui.md`, rule 1 | Best fit. ScopedEditors and OpenForge2k set none in their markup today, so adopting it means a sweep | XQ1002's element set, both spellings, and an empty value is not an id. For MAUI, the plain `AutomationId` attribute too | Markup only. It can check that an id is present, not that it is unique inside an item template |
| A zero-size Grid slot whose child sets no `IsVisible` | `ai-drivable-ui.md`, rule 6 | Partial | A child of a row or column with a literal `Height="0"` or `Width="0"` | Markup only. XQ1004 already covers the min-size half; bound sizes and clipping cannot be decided from markup |
| Containers generated from `ItemsSource` and named by `ToString()` | `ai-drivable-ui.md`, rule 2 | Partial | The `ItemTemplate`'s `x:DataType` overrides `ToString()`, or a container `Style` sets the name | Types. `TreeViewItem` ignores `ToString()` (measured, in `avalonia-gotchas.md`), so a tree needs the `Style` |

**Not a fit:** the guide's rule 10, keeping popups in the window's tree. Avalonia's `OverlayPopups` is
set in C# at startup, where no markup scan can see it. That check belongs in the application, as a
startup assertion or a headless test.

## Questions for the developer

None open.

## Follow-ups

- **XQ1004 reads four things more narrowly than the framework.** Found while building PR #20, and
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
