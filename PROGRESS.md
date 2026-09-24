# Progress

The work state of `Bennewitz.Ninja.XamlQuality`: what is published, what is on `main` and not yet
released, who depends on what, and the rule backlog with the questions still open for the developer.

## Status

| | |
|---|---|
| Published | `2026.3.922`: `Bennewitz.Ninja.XamlQuality` and `Bennewitz.Ninja.XamlQuality.ThemeAudit` |
| `main` | carries the unreleased changes below |
| Next release | Not scheduled; the developer decides. One release per calendar day |

### On `main`, not yet released

Every change after the `v2026.3.922` tag (`5d49915`) that a consumer can see; this repository's own
working documents are left out. Most of it makes a rule stricter or adds API, so the release that
carries it has to say so.

| Commit | Change | Effect on a consumer |
|---|---|---|
| `4f7bd62`, merged as `f7eaf8a` | ThemeAudit's content digest hashes each file's path relative to the scanned set's common ancestor, not the configuration's directory | **DiffView's CI is red on `main` until a release carries it.** A dependency resolved to a sibling checkout locally and to a fetched copy on CI produced two digests for the same files. DiffView predicts `b7ea0ec438c5` in both layouts afterwards, measured on a source build |
| `1e84319` | **XQ1004**, a new rule: a control whose declared minimum or fixed size exceeds the fixed Grid row or column it sits in | New findings wherever a consumer adopts it |
| `99299ec` | XQ1001 and XQ1002 reject an empty `<AutomationProperties.Name>` element, whether open-and-closed, self-closing or whitespace only | Markup that passed may now fail |
| `1d9ccac` | XQ1003 reads part names from compiled code: constants whether public or not, and `PART_` literals passed to a `Find*` or `Get*` lookup, across non-public types and lambdas | More parts are inspected. DiffView measures 33 → 34, with no findings |
| `1d9ccac` | `XamlSkip` and `XamlRuleResult.Skipped` name what a rule saw but could not check; XQ1003 fills it | New public API, additive |
| `52e481c` | XQ1003 given no assemblies names every themed control as skipped, instead of reporting a silent 0 | A scan that forgot `WithAssemblies` now says so |
| `e92fb92`, `8154f78` | `docs/ai-drivable-ui.md`, TailBlazer's guide to a UI an agent can drive and verify, kept byte-identical to TailBlazer's copy | Docs only |
| `621dab8` … `d80e53b` | `docs/avalonia-gotchas.md` arrives from ClaudeForge and grows: layout clip, focus and key bindings, context menus, pressing keys headlessly, the property-metadata trap, trimming, and the four ways an `avares://` URI fails | Docs only |

## Consumers inside the family

- **ScopedEditors' tests** pin `2026.3.922`, test-only. They use
  `InteractiveAutomationNameRule(additionalElements)`, `ExpanderAutomationNameRule`,
  `XamlScanContext.Load`, `XamlFile.RelativePath` / `.Text` / `.ParseError`, and
  `XamlRuleResult.Inspected` / `.Findings`. Narrowing any of them breaks ScopedEditors once it moves
  off `2026.3.922`.
- **ScopedEditors cites `docs/avalonia-gotchas.md` by path**, in `AppSeverityToGlyphConverter.cs` and
  its tests, `PropertyEditorWrapper.axaml` twice, and `DangerSurfaceMarkupTests.cs`. They rely on the
  entries about emoji-font fallback and about `AutomationProperties.Name` being ignored on a
  `TextBlock`. Moving or renaming the file or those entries means updating them.
- **DiffView** intends to adopt XQ1003 at 33 inspected against `2026.3.922`, raising its floor to 34
  once a release carries `1d9ccac`. It declined to promote its inline part name to a public constant,
  which `1d9ccac` made unnecessary. Its adoption plan is still a draft.

## Rule backlog

Ids are permanent, and the README's rules table is generated from them, so an id is assigned only
when a rule is written.

| Candidate | Source | Fit | What it checks | What it needs |
|---|---|---|---|---|
| `XQ1005`: a key binding on a control where nothing in its focus path can take focus | `avalonia-gotchas.md` | Scope decided 2026-09-23: all three cases | The host is unfocusable (`ListBox`, `ItemsControl`, `TreeView`); an `ItemContainerTheme` makes the container unfocusable; a keyless `ControlTheme` does the same | Types for the first case, the element alone for the second, theme resolution for the third. The third may stall, and must not hold back the first two |
| An explicit `AutomationId` on every interactive control | `ai-drivable-ui.md`, rule 1 | Best fit | XQ1002's element set, both spellings, and an empty value is not an id. For MAUI, the plain `AutomationId` attribute too | Markup only. It can check that an id is present, not that it is unique inside an item template |
| A zero-size Grid slot whose child sets no `IsVisible` | `ai-drivable-ui.md`, rule 6 | Partial | A child of a row or column with a literal `Height="0"` or `Width="0"` | Markup only. XQ1004 already covers the min-size half; bound sizes and clipping cannot be decided from markup |
| A custom control that carries an automation id or name but has no peer | `ai-drivable-ui.md`, rule 3; proposed by DiffView | Strong, and measured on a consumer | A themed control whose type overrides no `OnCreateAutomationPeer` below its framework base. DiffView measured all eight of its themed controls resolving to `NoneAutomationPeer`: names set, unreachable by a control-view search, behind a name gate that stayed green | Types, as XQ1003 does, over the same enumeration of themed controls XQ1003 already builds for `Skipped`. Also the framework's peer-less base types named, and an opt-out for a control that really is decorative |
| Containers generated from `ItemsSource` and named by `ToString()` | `ai-drivable-ui.md`, rule 2 | Partial | The `ItemTemplate`'s `x:DataType` overrides `ToString()`, or a container `Style` sets the name | Types. `TreeViewItem` ignores `ToString()` (measured, in `avalonia-gotchas.md`), so a tree needs the `Style` |

**Not a fit:** the guide's rule 10, keeping popups in the window's tree. Avalonia's `OverlayPopups` is
set in C# at startup, where no markup scan can see it. That check belongs in the application, as a
startup assertion or a headless test.

## Questions for the developer

Asked on 2026-09-23 and deferred; ask again.

1. **Which of the candidates to write, and in what order.** `XQ1005` is already decided. The order
   suggested when this was first asked was the explicit `AutomationId`, then the zero-size slot,
   then the peer check, then the container half of the naming rule. **New since then:** DiffView
   measured the peer gap on its own controls and proposes the peer check first, with the explicit
   `AutomationId` after it. An id on a control with no peer is just as unreachable as a name.
2. **When to release.** `main` carries a new rule, stricter rules and new public API. The strongest
   reason to cut one: **DiffView's CI is red on `main` until a release carries `4f7bd62`**, although
   DiffView reports nothing waiting on a deadline. Its XQ1003 floor rising to 34, and any move by
   ScopedEditors off `2026.3.922`, wait on it as well.
