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
| PR #11 | `docs/ai-drivable-ui.md` and `docs/avalonia-gotchas.md` become the only copies of themselves, and other repositories point here. The guide gains what TailBlazer relayed: harness and screen reader are one requirement; from Avalonia 12 the same peers reach Linux over AT-SPI, a harness driving them there unverified; a parked harness window takes `WS_EX_NOACTIVATE`; and two costs a harness pays, a search from the desktop root over its descendants, and list rows read without a `CacheRequest` | Docs only |

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

None open. The last one, the backlog naming a candidate `XQ1005` before it was written, closed when
`XQ1005` was.
