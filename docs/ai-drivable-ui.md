# Making a desktop UI that an agent can drive and verify

**For an AI agent starting UI work in a desktop application.** This is the method that let agents
build, drive and verify a WPF-to-Avalonia port: 289 headless fixtures and 29 runtime harnesses
that drive the real application **with no synthetic input**, run beside a person working at the
same machine, and between them caught defects the compiler, the unit tests and code review all
missed. The rules come first; the reasons and the traps follow. The worked examples are in the
TailBlazer repository, branch `feature/UpgradeToNet10`, whose paths are given where they help.

**The one-line version: the application owns its automation surface, every claim is checked by the
instrument that can actually see it, and no check is believed until it has been seen to fail.**

⭐ **This is the one living copy, and what other repositories learn comes back here.** It belongs to
[Bennewitz.Ninja.XamlQuality](https://github.com/JanusMael/Bennewitz.Ninja.XamlQuality). Send a new
lesson, or a correction, to the session working in that repository by message (it answers as
`XamlQuality`), with the measurement or source that shows it. When no such session is running, open
an issue there instead. A claim is checked before it lands, and the sender hears the outcome. Keep no
copy elsewhere; cite this file by path or URL.

---

## 1. The stance: fix the application, not the harness

**When the automation tree cannot answer a question honestly, the fix goes in the application —
not in the test script.** A clever read in a harness (a raw-tree walk, a regex over a composed name,
a pattern that happens to answer) is invisible to everyone but the next reader of that harness. An
automation peer is part of the product: every later harness gets it free.

**Everything an agent needs to drive a UI is part of the application**: a stable identity per
control, an honest name, patterns that do what they advertise, nothing present in the tree that is
not on screen. Treat each gap as a defect in the application, with a test, not as test plumbing.
Fill in what a screen reader would read as well: a control's `Name`, not only the `AutomationId` a
harness finds it by (rule 2). Nothing in this method tests screen-reader use.

**Why it is not a preference**: in TailBlazer every one of these gaps produced a *false result*
before it was closed — a number that looked like a defect in the application and was an artefact
of how it was read.

| Gap | The false result it produced |
|---|---|
| A list row's `Name` fell back to its data object's `ToString()`, `"{Number}: {Text}"` | A wrapped line read as 5 characters short — "the port strands the end of every long line". It was the `"270: "` prefix |
| The drawn text of a custom control reachable only in the RAW view | A control-view search matched nothing, **which reads exactly like the control not existing** |
| `ListBox` advertised `SelectionPattern`; `GetSelection()` returned nothing | "No row selected" while the row was visibly painted selected |
| `ListBox` advertised `ScrollPattern`; every value was 0 or -1 and `Scroll()` did nothing | Every scroll harness failed for a reason unrelated to scrolling |
| A collapsed pane stayed in the tree at full size | A harness reported a pane present that no user could see |

**From Avalonia 12, the same peers are published on Linux.** `Avalonia.FreeDesktop.AtSpi` puts them
on the AT-SPI bus, one node per `AutomationPeer`, and an application built on `Avalonia.Desktop`
carries it through `Avalonia.X11`, which starts it with the application. nuget.org lists it from
12.0.0-preview2 and in no 11.x release. **That they are published is read from the source; that a
harness can drive them over AT-SPI has not been verified.** Every harness behind this method drives
Windows UI Automation.

---

## 2. Rules for the application

1. **Every control a test or a user needs to reach has an explicit `AutomationId`.** Do not rely on
   a framework deriving one from `x:Name`, and never find a control by its visible text.
2. **Every control's `Name` is the value a person would read aloud**, set explicitly. Never let a
   peer fall back to a data object's `ToString()` — that is a debugging string nobody chose.
3. **A custom control gets a peer.** A bare `Control`/`TemplatedControl` subclass has no peer a
   control-view search can reach, so an `AutomationId` on it appears nowhere. Override
   `OnCreateAutomationPeer`; name the peer with what the control *shows*; put it beside the control.
   `internal` is fine — grant `InternalsVisibleTo` to the test projects from one shared file.
4. **Never advertise a pattern the peer does not honour.** Answering "not supported" is the truth;
   advertising `Selection` or `Scroll` and answering nothing is the defect. Honour it or withdraw it.
5. **An automation property must not be sourced from the thing it will be used to check.** Naming a
   row with its *visible character window* would make every wrap assertion compare the drawn text
   with itself — green for ever. Name it with the raw data; let the drawn text be a second, separate
   property.
6. **What is not on screen is not in the tree.** A collapsed pane, a shut drawer, a zero-width
   column: hide it (`IsVisible=false` / `Visibility.Collapsed`), do not merely shrink or clip it.
   Clipping stops the paint and changes neither the layout bounds nor the automation tree.
   **Custom-drawn is not a proxy for decorative.** Whether something is a control element depends
   on whether a person can act on it. In a control library the custom-drawn surfaces are often the
   interactive ones — that is why they were custom-drawn — and hiding them as decoration removes
   exactly what most needs driving.
7. **The application says what state the desktop left it in.** Publish session type, whether the
   window holds the foreground, and any test-mode placement on the main window's automation
   `HelpText` (TailBlazer: `ShellEnvironment`), updated on activation changes. A harness then reports
   *why* it could not decide instead of failing opaquely.
8. **State can be seeded without input.** If settings, saved searches, layouts or selections persist
   to files, a harness writes those files before launch and gets the application into any state
   without driving a single control. This was the cheapest instrument the TailBlazer port had.
9. **Test-mode behaviour is gated on environment variables a harness sets and restores**, so the
   application a person runs is untouched: an isolated settings folder (never the user's real one),
   and window placement that keeps harness windows out of the way (below).
10. **Only a modal dialog is a real top-level window.** Popups, menus and flyouts living in the main
    window's tree are reachable from it; a popup that is its own top-level window is invisible to a
    search rooted at the main window. (Avalonia: `OverlayPopups = true`.)

---

## 3. Three instruments, three different questions

**Name which one you are reading before asserting.** Substituting one for another produced the only
wrong finding the TailBlazer port ever published.

| Instrument | Answers | Blind to |
|---|---|---|
| **Layout bounds** (`Visual.Bounds`, `ActualWidth`) — in a headless fixture | Where layout put a control, relative to its **parent** | Paint, clipping, and anything relative to the window unless you translate |
| **The automation peer's bounding rectangle** — in a runtime harness | What a harness perceives | Clipping: **a clipped control keeps its full rectangle** |
| **A capture** — `PrintWindow` with `PW_RENDERFULLCONTENT`, or headless rendering | What actually reached the pixels | Anything not drawn |

- A claim about **what is drawn** needs a capture. A property assertion is not a pixel: unwiring a
  border's brush leaves the property test green and only the pixel test goes red.
- A layout detector that compares each control with its **parent** cannot see a whole column pushed
  past the window's edge while every control sits neatly inside it. **A detector's frame of
  reference is part of its claim** — add a window-relative check for anything that must stay
  on screen.
- `PrintWindow` asks the window to draw itself, so capture works with the window behind others,
  parked at a screen edge, or partly off screen. That fact is what makes a shared desktop workable.

---

## 4. Four layers of verification, and which to reach for

| Layer | Needs | Use it for |
|---|---|---|
| **Headless fixtures** — the real views, real container, real rendering (Avalonia.Headless with Skia, `UseHeadlessDrawing = false`) | Nothing | Bindings, peers, layout at several sizes, pixels, key bindings (a headless keypress goes through the real input stack), view-model wiring. Fast; run on every change |
| **Runtime harnesses through UI Automation, no input** | A running app and a desktop | What only a real window says: real sizes, real OS, the real file system, the command line, multi-process behaviour. **Runs beside a working person** |
| **Runtime harnesses that need the foreground** | The window holding focus | Light-dismissing controls (combo boxes, menus, overlay drawers) and activation-dependent behaviour. **Needs an undisturbed machine** |
| **Runtime harnesses that send real input** | An isolated session (Windows Sandbox, Xvfb, a nested compositor) | Keyboard and mouse paths that genuinely need a keystroke. Keep this set small |

**Ask the machine which of these it can run.**
[`Probe-UiTestEnvironment.ps1`](ai-drivable-ui/Probe-UiTestEnvironment.ps1) prints the session, the
display server and the scaling, then which approaches the machine supports. The same host can be a
console session one hour and a Remote Desktop session the next, with different answers.

**Prefer the first two, and count the rest by what each harness DOES, not by which helper it calls.**
TailBlazer's Avalonia set is 29 harnesses; none sends input and two need the foreground. The count
was wrong for weeks when it was taken by grepping for a tool's name.

**Drive through patterns, not input**: `InvokePattern` (buttons), `TogglePattern`,
`SelectionItemPattern.Select()`, `ValuePattern`, `ExpandCollapsePattern`, `WindowPattern`
(minimise, restore, close), `RangeValuePattern.SetValue` on a scrollbar. None needs the foreground,
none touches the user's keyboard or mouse. **Check what a pattern actually returns before building
on it** — see rule 4.

**The foreground is a second gate, separate from input.** A combo box will not open its dropdown for
a background window even when driven purely through automation. A window an agent's shell starts
takes the foreground only on an unlocked, idle desktop: not while someone is working, and not while
the session is locked, when LockApp holds it. **Hand foreground-needing harnesses to the person as a
command to run** rather than folding them into an unattended sweep, and make them report
INCONCLUSIVE, naming the foreground state, when they cannot decide.

**To reach "the window was active and then was not" without input**, minimise it through
`WindowPattern` — that deactivates a window that really was active. A second, parked launch is not
the same state: it was never activated.

---

## 5. Writing a harness

**Skeleton, in order:**

1. **Isolate.** Point the application at a fresh settings folder through an environment variable;
   verify on exit that the user's real settings folder is unchanged (TailBlazer compares a manifest
   of it taken on entry), and fail the run over any other verdict if it is not. Refuse to start if the application is already running.
2. **Seed state from files** (rule 8), then start the application with its arguments.
3. **Find the window by process id**, and scope every search to it. `RootElement` is the whole
   desktop: an unscoped search for a control named `Close` found and invoked **a button in another
   application**. When a popup must be found from the root, AND in the process id. (PowerShell:
   never name the variable `$pid` — that is the harness's own process.)
   Never search `RootElement` with `TreeScope.Descendants`: it visits every application on the
   desktop, so a harness's time measures what else is open. One first search took 17 s, and one
   harness ran 94 s on a quiet desktop and 406 s on a busy one, with the same code.
4. **Scope to the container that owns the part.** Template part names (`PART_VerticalScrollBar`) are
   shared by every scroll viewer in the window; take the one inside the list you mean, never the
   first in the window.
5. **Poll with a deadline, never sleep once.** The predicate must still hold, and the last observed
   value is what gets asserted — this does not weaken the check, and a fixed sleep is the flake.
6. **Assert preconditions as INCONCLUSIVE, claims as FAIL.** "The file never opened" is not "the
   feature is broken". Three verdicts with three exit codes: PASS 0, FAIL 1, INCONCLUSIVE 2.
7. **Guard every negative.** "No bell appeared" is also what a view that never read the new line
   reports — prove the line arrived before "nothing happened" means anything.
8. **Give every claim a control arm.** A second launch that differs in exactly one thing (the option
   off, the file not empty) and must come out differently. A harness whose control also fails is
   measuring itself — or, occasionally, a shared path in the application; check which.
9. **Assert after the SECOND value.** A binding or an invalidation proved on first paint proves
   nothing about a change. Re-point, re-set, append — then assert.
10. **Re-find after anything that can remove an element.** A stale automation element answers from
    cache with its last rectangle instead of throwing; assert absence as `FindFirst` returning null.
11. **Assert in device-independent pixels, and print the display metrics, the DPI awareness and the
    session type at the top of every transcript.** A figure that looks doubled or halved is a DPI
    story. Or make the claim a ratio that cancels the scale.
12. **Count pixels by structure, not by colour alone.** ClearType fringing makes a raw coloured-pixel
    count meaningless; count scanlines at least 60% filled, or compare two regions of the same row,
    and make any blankness guard sample the same pixels the assertion reads.
13. **Read many elements through a `CacheRequest`.** Reading `.Current` on each list row makes two
    cross-process calls per row, each waiting on the application's UI thread; cache `Name` and
    `BoundingRectangle` and fetch them in one call. One harness went from 250 s to 166 s, with
    identical readings.

**Keep harness windows out of the person's way**: when a harness environment variable is set, the
application parks its window against a screen edge and sends it to the bottom of the Z-order once it
has a handle (`SetWindowPos(HWND_BOTTOM, NOMOVE|NOSIZE|NOACTIVATE)`), for **every** window it opens,
through one helper. Windows clamps a top-level window back on screen, so "off screen" is not
available; parking plus Z-order is. Leave an opt-out for harnesses that need the foreground.
TailBlazer's helper, [`HarnessWindowPlacement.cs`](ai-drivable-ui/HarnessWindowPlacement.cs), does all
of this and the paragraph below.

**And make the parked window impossible to activate: add `WS_EX_NOACTIVATE`.** Opening without
activation (`ShowActivated = false`) only stops the window activating itself. When the person's own
window closes or minimises and nothing of theirs sits above the harness window, Windows hands
activation to the next window down the Z-order, and in TailBlazer a harness window took keyboard
focus that way now and then, with nothing asking for it. `WS_EX_NOACTIVATE` in the extended style
(`SetWindowLongPtr(GWL_EXSTYLE, …)`) is the flag Windows skips when it chooses, and it also stops a
click from activating the window. Harnesses that drive through automation patterns never need
activation, so set it on every parked window the application owns, never on one opened under the
foreground opt-out. **A modal dialog is the one case it cannot cover**: `ShowDialog` activates its
window whatever it is asked, so a harness that opens one takes focus once per dialog.

**Resize without taking focus**: `SetWindowPos(..., SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE)`.

---

## 6. No check is believed until it has been seen to fail

**Break the code under a new test on purpose, see the test go red, then restore.** In TailBlazer this
refuted a claimed guard several times — a fixture that "would have caught" a bug it could not reach,
a colour assertion satisfied by a failed capture, a flag test that passed against the old code
because the old code happened to share its outcome.

- **Rebuild on both legs** of the break/restore cycle. An incremental build can leave the restore
  stale and green — or the break stale, which reads as "this assertion does not discriminate" and
  gets a real assertion deleted.
- **Restore by the inverse edit, never by `git checkout --`** while the file holds other uncommitted
  work — it reverts everything, silently.
- **A mutation harness is code: an edit that matches nothing must fail, or a no-op mutation reads as
  a surviving one.**
- **To prove a runtime harness discriminates**, build the base commit in a detached worktree and
  point the harness at that executable. A harness that has only ever passed has not been shown to
  test anything.
- **Know which branch of the check failed.** A harness that failed only through its "not inside the
  window" branch has still not shown that its ink branch can fail.
- **A test that passes either way says so in its own comment** — the whole-row viewport sizes that
  pass before and after a rounding fix are there as controls, and are labelled as such.
- **When a probe clears a suspect, start it from where the failing run started.** A rounding bug was
  once "ruled out" by evaluating the arithmetic from the top of the file; the failing harness began
  tailing at the bottom, where the two roundings differ.

**A mechanism no fixture can drive is a mechanism nobody is watching.** A headless `DispatcherTimer`
never fires — not under `RunJobs`, not under a real main loop — so the scheduler branch built on it
had no test, and it was broken in the running application. Replacing it with a thread-pool timer
that *posts* to the dispatcher made it testable (and needed the timer rooted until it fires, or it
can be collected first).

---

## 7. Running them, and the person at the machine

- **A sweep runner runs every harness as a set**, with a per-harness timeout, stray-process cleanup
  before and after each, output redirected to one file per harness, a summary CSV, and **an exit
  code equal to the number that were not PASS**. Nothing else will tell you which change broke which
  harness.
- **Serial by default.** Run harnesses in parallel only once a parallel sweep's verdicts have matched
  a serial sweep's. A harness that finds its window by its own process id is safe beside another by
  inspection, which is not the same as reliable under load.
- **A harness the sweep leaves out is a row, not a footnote.** When a background sweep skips the ones
  that need the foreground, list them in the summary as not run and not counted, so a sweep that
  skipped two does not read as complete.
- **Never pipe a harness or a sweep through `tail`/`head`.** The shell reports the last element of a
  pipeline, so a sweep with three failures reported exit 0.
- **Name the known non-green in the runner or the project's rules**, with the reason each is
  non-green by construction, so a new red is noticed rather than excused.
- **Host time belongs to the person.** Ask before running, and ask for **one bucket** — what each
  run answers, how long, the total — rather than a run at a time. One approval does not authorise
  the next run.
- **Measure the rate before attributing anything intermittent.** At one freeze in thirteen runs, six
  clean runs are what you would expect whether or not a change helped.
- **Re-probe the environment rather than remember it.** Console or remote session, display server,
  scaling — the same machine changed all three between one hour and the next.
- **The long account is
  [SharedDesktopHarnessJournal.md](ai-drivable-ui/SharedDesktopHarnessJournal.md)**: choosing an
  approach from the environment, the foreground gate, parking a window, and the DPI traps, each
  measured.

---

## 8. Traps, each of which cost a false result

| Trap | What to do |
|---|---|
| A search rooted at the desktop found another application's `Close` button | Scope by process id, always |
| A popup is its own top-level window; a search from the main window finds none of it | Keep popups in the window's tree, or search from the root AND the process id |
| A stale element returns its last rectangle | Re-find; assert absence as null |
| A clipped element keeps its full automation rectangle | Hide rather than clip; claims about drawing use a capture |
| A peer advertises a pattern and answers nothing | Check what it returns; withdraw or honour it in the application |
| A row's `Name` is its data object's `ToString()` | Set the name explicitly |
| A bare custom control is absent from the control view | Give it a peer |
| A virtualised list reads selection only on realised rows; opening a pane scrolls a tail-anchored list | Select after the viewport settles |
| A parent-relative layout detector cannot see a column pushed off the window | Add a window-relative assertion for anything that must stay on screen |
| A Grid never shrinks an `Auto` column | Give the must-stay controls an outer `*,Auto` of their own |
| A shut overlay pane closed by light-dismiss never reached a one-way binding | Two-way binding for anything the control can change itself |
| A fixed sleep before an assertion | Poll with a deadline |
| A figure halved or doubled between runs | DPI; assert in DIPs; print the metrics |
| Counting coloured pixels | Count filled scanlines, or compare regions of one row |
| A headless timer never fires | Build on something a fixture can drive |
| A harness left red right after your change | Rebuild the base, run both arms at the same commit, several times, before attributing it |

---

## 9. Adopting this in a new project

1. **Decide the automation surface is product.** Write the rules in section 2 into the project's
   agent instructions, and keep a record of every automation gap closed with what it cost.
2. **Add the three hooks**: an environment variable for an isolated settings folder, one for harness
   window placement, and the environment report on the main window's `HelpText`.
3. **Make state seedable from files** wherever the application already persists it.
4. **Stand up headless fixtures over the real views** — the real container, real rendering — and a
   parent-relative overflow detector plus window-relative checks, run at several window sizes.
5. **Write the harness helper** (isolation, process-scoped search, three verdicts, display metrics)
   and the sweep runner **before** the second harness.
6. **Classify every harness** into no-input, foreground, or real-input, and route each to the
   machine state it needs.
7. **For every new check: break it, see it fail, restore, rebuild.** For every new runtime harness:
   run it once against the base commit.
