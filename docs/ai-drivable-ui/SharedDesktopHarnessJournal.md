# Running UI-automation harnesses on a desktop somebody is using

*An account of making a shared machine a working environment for UI Automation tests, written while
doing it in TailBlazer's Avalonia port from 2026-09-21. "Here" means that port, on that machine.*

*The companion to [SandboxHarnessJournal.md](SandboxHarnessJournal.md), which covers the other
half of the problem. That one is about **input isolation**; this one is about **everything else**.*

---

## Choose the approach from the environment, and detect the environment first

**This is the step both of these journals originally skipped.** Each describes an approach that
works; neither said how to pick one, and the choice is not a preference — **the environment decides
which approaches are available at all**, and it can change under you between one run and the next.

**Four questions, in this order.** Answer them by asking the machine, not by remembering.

1. **Is there a display at all?** CI usually has none.
2. **Is the session local or remote?** RDP, VNC, xrdp, Remote Desktop on macOS.
3. **What is drawing?** Win32 / X11 / **Wayland** / Quartz. This is the question most likely to
   invalidate an approach outright.
4. **What is the scaling, and does it match what the process was told at startup?** See the DPI
   section below — this one silently halves numbers rather than failing.

### The four approaches, and what each one actually needs

| | Approach | Needs | Fails when |
|---|---|---|---|
| **A** | **Accessibility/automation APIs, no synthetic input; capture by asking the window to draw itself** | A running app and an a11y bus. Nothing else | Almost never — this is why it is the default |
| **B** | **Synthetic input into the real desktop** | A session you own and are **not** using | Blocked outright under Wayland; unreliable on a disconnected remote session |
| **C** | **An isolated desktop** (Windows Sandbox, Xvfb, Xephyr, a nested compositor) | Host support for that mechanism | Windows Sandbox is **reported not to work from an RDP session**. Nobody has measured it; the sandbox journal says so |
| **D** | **Fully headless toolkit rendering** (Avalonia.Headless and similar) | Nothing | Anything about real windows: chrome, focus, Z-order, the window manager |

**Prefer A, and count how many of your harnesses genuinely need B.** Counted by what each one does,
on 2026-09-24, TailBlazer's Avalonia set is 29 harnesses: none sends input, and two need the
foreground. That ratio is the whole reason a shared desktop is workable here — and it was
miscounted for weeks, because the count was taken by grepping for one tool's name rather than by
asking what each harness *does*.

### THE SECOND GATE, AND IT IS NOT "DOES IT SEND INPUT"

**A harness can need the FOREGROUND without sending a single keystroke**, and that is a separate
question from approach B. It cost a whole afternoon here to notice.

**A `ComboBox` will not open its dropdown for a window that is not in the foreground.** Nor will a
menu, and probably nor will anything else that light-dismisses. A harness that drives such a control
through UI Automation sends no input at all — it looks like a pure approach-A harness — and yet it
cannot work on a desktop somebody else is using, because **every click elsewhere takes the
foreground away.**

Measured, twelve runs, sampling the foreground window every 250ms:

| | runs | another application in front |
|---|---|---|
| dropdown opened | 2 | **64%** of the run |
| dropdown did not | 10 | **94%** of the run |

The two that worked are the two where the shell held the foreground for part of the run. **Before
this was instrumented the transcript said only "after Expand() the combo reads Collapsed"**, which
read as a flaky harness and cost three wrong conclusions about an unrelated window-placement change.

**So classify by what a harness NEEDS, in three buckets rather than two:**

| Needs | Can it run beside a working person? |
|---|---|
| The automation tree, and a capture | **Yes** — this is the large majority |
| The **foreground** | **No.** It needs the machine to itself, though it takes it only briefly |
| Synthetic **input** | **No.** It needs a session of its own — that is what the sandbox is for |

**Measured 2026-09-24, on Windows 11 26200 with Avalonia 12.1.3: the gate is an unlocked session
with no input during the run.** An agent-started window held the foreground through three runs of
one harness while the owner sat at the desk, unlocked, hands off, 13 seconds after the last
keystroke. The same harness read no foreground twice earlier that day: once while the owner was
using the machine, and once with the session locked, when `GetForegroundWindow` belonged to LockApp.
`PrintWindow` captures are unaffected by the lock. **Before a run that needs the foreground, check
that `GetForegroundWindow`'s process is not LockApp, then ask the person to keep their hands off for
about a minute.** The person does not have to be away, and who starts the run does not matter.

**And make the application say which state it was in.** A window that publishes *"session=console;
foreground=False; parked=True"* on its automation `HelpText` turns an opaque failure into a
precondition that names itself — INCONCLUSIVE rather than FAIL, per the rule below. That is three
API calls and one property, and it is the single cheapest thing in this document.

### The gate that will surprise a Windows developer: Wayland

**Wayland has no XTEST.** There is no global synthetic-input injection by design, and screen capture
goes through a portal that asks the user. So **approach B is not merely awkward under Wayland, it is
absent** — and approach C's usual Linux answer, `Xvfb`, is an **X11** server and so only helps an
X11 (or XWayland) client.

What exists instead is compositor-specific: virtual-pointer and virtual-keyboard protocols that some
compositors implement and others do not, and headless compositors (`cage`, `weston --backend=headless`,
`labwc`) for the isolated-desktop role. **None of this is verified in TailBlazer** — its harnesses
are Windows-only — so treat the previous sentence as a starting point for your own
measurement rather than as a finding. **The point that does carry: a suite built on synthetic input
is a suite that may not port to the platform you are porting to**, and finding that out after
writing thirty of them is expensive.

### What this means in practice

- **Probe, then choose.** [`Probe-UiTestEnvironment.ps1`](Probe-UiTestEnvironment.ps1) reports the
  four answers: on Windows by asking the system, elsewhere from `$WAYLAND_DISPLAY`, `$DISPLAY`,
  `$XDG_SESSION_TYPE` and `$SSH_CONNECTION`.
- **Record the answers with every result.** See *Record the session type* below — TailBlazer has
  an incident record it cannot split by session type because nobody wrote it down.
- **Re-probe rather than remember.** The same machine here was console at one hour and RDP the next,
  at different resolutions and different scaling, which changed what a harness could do *and* what
  its numbers meant.

## The problem this solves

A UI-automation harness starts the real application and asserts on what it actually does. That is
the whole point of it — a green unit suite is not evidence a desktop application works, and
TailBlazer shipped a feature with thirty-six passing tests that did not work at all.

But starting an application gives its window the foreground, whenever the session is unlocked and
nobody is giving it input (measured, above). **Not because any harness asks for it**:
`SetForegroundWindow` appears in none of TailBlazer's Avalonia harnesses. It is simply what starting
a process does. A sweep of two dozen harnesses is two dozen windows
appearing over whatever the owner is doing, and on 2026-09-21 the owner stopped the work twice over
exactly that.

**Windows Sandbox is the obvious answer and it is usually the wrong one.** The sandbox exists for
input isolation — for harnesses that send real keystrokes and mouse events, which would otherwise
land in whatever the owner is typing into. Harnesses that drive the application through UI
Automation send none. TailBlazer's never needed the isolation, and reaching for a sandbox to solve a
*foreground* problem costs a virtual machine, thirty minutes, and — on this host — three occasions
when it took the whole machine down.

There is also a plain availability problem. **A sandbox is reported not to work from a Remote
Desktop session** (the owner's report; no launch from one was attempted to measure it), and the owner
works over RDP, so on any given day the sandbox may simply not be available. A technique that only works when you are sitting at the console is not a technique.

So: how do you run a UI harness on the machine somebody is using, without taking their desktop?

## What does not work, each of them measured

### Moving the window off the screen — it cannot be done, and the consolation prize is real

The first fix computed a coordinate past the right-hand edge of every screen and put the window
there. **It does not get off the screen.** Windows clamps a top-level window back onto the virtual
screen, measured as an A/B on one build in one hour:

| placement asked for | where the window landed | off the desktop? |
|---|---|---|
| default | `427,102 .. 2453,1573` | no |
| `screens.Max(Bounds.Right) + 64` = 2944 | **`854,0 .. 2880,1471`** | **no** |

The display is 2880 wide and the window is 2026. `2880 - 2026 = 854`. The right edge is flush
against the screen edge and the whole window is visible. **No coordinate this approach can choose
beats the clamp**, so anything named "off screen" is named after a mechanism that does not exist.

**BUT DO NOT CONCLUDE, AS THIS DOCUMENT FIRST DID, THAT IT ACHIEVES NOTHING.** What it reliably does
is **park the window hard against an edge**, and how much that buys depends entirely on the ratio of
screen width to window width:

| | screen | window | result |
|---|---|---|---|
| over Remote Desktop | 2880 wide | 2026 wide | covers **70%** of the screen — useless |
| at the console | 5120 wide | 1016 wide | far right **20%** — genuinely out of the way |

**Every measurement behind the original "it does nothing" conclusion was taken over Remote
Desktop.** The machine's owner, sitting at the console, said plainly that it had been working — and
was right. A conclusion drawn entirely from one display is a conclusion about that
display, which is the same lesson the DPI section below arrives at from a different direction.

So park deliberately rather than incidentally: **compute the position once the window is open and
has a real size**, from the screen bounds and the window width, rather than asking for a coordinate
outside the desktop and relying on the clamp to produce something useful. Relying on the clamp means
relying on undocumented behaviour for the one thing the code actually achieves.

**Offer both edges.** Which edge is spare depends on where the person keeps their work, and on a
very wide monitor both are.

### Negative coordinates

The obvious next thought, and it is worse. **`-32000` is where Windows parks a minimised window**,
and a window that has never been composited is exactly what captures blank — so you trade a visible
window for a harness that silently asserts on an empty bitmap. That failure mode is much more
expensive than the one you started with.

### `ShowActivated = false` on its own

This is the one that matters, because **it works well enough to be mistaken for a solution.**

Setting `ShowActivated = false` stops the new window taking the foreground. On a busy desktop the
window then appears *behind* everything and is genuinely invisible, so the owner reports that the
problem is fixed — as this owner did. But the window is still there, in front of whatever it opened
over in Z-order terms, and **on a clear desktop it will be seen.**

For six days this arrangement was credited to the off-screen position, which had never got a window
off any screen. **A claim nobody measured is the thing this codebase keeps paying for** — and the
correction above is the same lesson arriving from the other side, because the next claim, that the
position achieved *nothing*, was also made without measuring it where it works.

## What does work

**Three things together, and the mistake this document made twice was believing any one of them was
the whole answer.** Park the window against an edge; do not activate it; put it at the bottom of the
Z-order once it has a handle.

**Added 2026-09-24: a fourth, `WS_EX_NOACTIVATE`.** Opening a window without activation does not
stop Windows handing it activation later, when the person's own window closes or minimises and the
parked window is next in the Z-order. The extended style stops that, and stops a click activating it
too. The guide's section 5 has the rule, and why a modal dialog is the one case it cannot cover.

Parking is covered above. The Z-order call is the half that always works:

```csharp
// Z-order is NOT a window message - there is no WM_ to post for it.
[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool SetWindowPos(
    IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

private static readonly IntPtr HwndBottom = new(1);
private const uint SwpNoSize     = 0x0001;
private const uint SwpNoMove     = 0x0002;
private const uint SwpNoActivate = 0x0010;

SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
```

Measured, on 33 visible top-level windows:

| harness variable | Z-order position | windows in front of it |
|---|---|---|
| unset | **3** of 32 | 3 |
| set | **31** of 32 | 31 |

### Five things about doing it that are not obvious

- **It cannot be done when the window is constructed.** A window has no platform handle until it is
  open. In Avalonia that means hooking `Opened`; the equivalent exists in every toolkit and the
  constructor is always too early.
- **Hook it per window, and leave it hooked.** `Opened` is raised once per `Show` — Avalonia 12.1.3
  raises it from `Show` and `ShowDialog`, never from activation — so a window shown again is parked
  again, which is what a harness showing it again wants, and a person who brings it forward to look
  at it is never fought.
- **Every window the application opens has to go through one helper, not just the main one.** The
  first fix here placed the shell window and the very next run put a window back on the owner's
  screen: a modal dialog is a *different* window created somewhere else, and **`ShowDialog`
  activates its window whatever `ShowActivated` says.** A fix for "windows appear" has to enumerate
  the windows rather than fix the one in front of you.
- **Gate it on an environment variable the harness sets and restores**, so the behaviour exists only
  while a harness is driving and the application a person runs is untouched. In TailBlazer
  `TAILBLAZER_WINDOW_PARK` — taking `right` or `left` — is set by the harness helper's
  `Enter-HarnessSettings` and put back by `Exit-HarnessSettings`, the same save-and-restore the
  isolated settings folder already gets. **No harness was edited**, because they all already pass
  through that helper.
- **Leave an opt-out.** Harnesses that send real keystrokes need a window that can receive them.
  TailBlazer's is `-OnScreen`, and two harnesses use it.

### Capture is unaffected, and that is the fact the whole thing rests on

`PrintWindow` with `PW_RENDERFULLCONTENT` **asks the window to draw itself into a bitmap** rather
than copying the screen. So it does not need a clear desktop, it does not need the window in front,
and it does not need the window on screen at all.

**Proved rather than argued**: the harness used to check this carries a blankness guard and reads
pixel *positions* for its assertion — it counts 105 scroll marks and checks where they are. It
passes with the window at the bottom of the Z-order, and it passed off the edge of the screen
before that. A harness that merely asserted "something was captured" would not have been evidence.

## The DPI trap, and there are two of them

**A harness figure that looks doubled or halved is a DPI story, not a product story.** This cost a
false conclusion twice in two days, and the two causes are different.

### The display changes between sessions

| | 2026-09-21 evening | 2026-09-22 morning |
|---|---|---|
| display | 2880 x 1771 at **192 DPI** | 5120 x 1440 at **96 DPI** |
| a scroll harness's pitch / viewport | 40 / 1123 | **20 / 561** |
| that harness's failures | **four** | **one** |

Nothing about the application changed. **An RDP session reconnected at a different resolution and
scaling.** Three of that harness's four assertions turned out to be scaling-dependent, and only the
survivor was the real defect — which had been on record for days described by figures that belonged
to one display.

### A DPI-unaware process sees half of everything

Separately, and on the *same* display: two runs of one harness within an hour reported the window as
`1013 x 736 at 427,0` and `2026 x 1471 at 854,0`. Exactly half.

At 192 DPI a **DPI-unaware** process is handed virtualized coordinates. `GetWindowRect` then sizes
the capture bitmap at half, `PrintWindow` draws at physical scale into it, and **only the window's
top-left quadrant is captured** — so the saved image is cut off mid-sentence and a sample taken at a
fixed offset lands somewhere else entirely. In TailBlazer's case it read the title bar where the toolbar is,
and the run was very nearly written up as a regression in code that was provably correct.

### The mechanism is "system aware", and you probably cannot fix it at the process

**A DPI-*unaware* process is the obvious suspect and it is usually the wrong one.** The likelier
culprit is **system aware**, which is what a manifest-pinned host like PowerShell already is: such a
process is handed the DPI the system had **at logon**, and when the session's current DPI differs —
exactly what happens when a remote client connects at different scaling — Windows virtualizes every
coordinate by the ratio. At 192 against a 96 baseline that is half.

**And you cannot simply force it.** Windows lets a process set its DPI awareness **once**, and a
manifest counts; `SetProcessDpiAwarenessContext` returns false from inside a script the shell has
already fixed. This was tried here, in the belief that it would settle the matter, and it did not.

### What to do about it

- **Record the display metrics, the DPI, the awareness level and the SESSION TYPE with every harness
  result.** Five API calls, four printed lines, and they turn an unreadable intermittent into
  something obvious at the top of any transcript. The awareness line earns its place precisely
  because you cannot change it — knowing a run was system aware at 192 DPI explains every halved
  figure in it.
- **Assert in device-independent pixels.** Divide the measurement by `dpi / 96`. A row pitch defined
  as 20 DIPs reads 20 on every display, and DIPs are the unit the application itself is written in —
  so the assertion finally speaks the same language as the code under test. **This is the real fix;
  everything above is diagnosis.**
- **Or make the claim arithmetic.** A ratio, a difference between two measurements in one run, or an
  identity that cancels the constant terms survives a scaling change without needing the DPI at all.
- **Compare the verdict and the cause, never the figures.**

## A window's size is not its client's size (2026-09-24)

**`SetWindowPos` sets the OUTER size.** A harness that sizes the window to 640 gets a client area
about 16 px narrower than a headless fixture's 640, which sizes the content itself, and that gap hid
a real overflow in the fixtures. Compare client with client.

## Record the session type. Every time.

This machine's harness record contains three occasions when a sandbox session took the whole host
down, one desktop-application hang, and a UI freeze that happens about one run in nineteen. **Not
one of those entries records whether the session was the physical console or Remote Desktop**, so
the record cannot now be split by it — and RDP is the single most obvious variable in the room.

That is a gap in the evidence rather than evidence of anything, and it is exactly the shape of thing
that makes an intermittent hazard unreadable a month later. **One line per entry. Write it down.**

## Run them as a set, because nothing else will

Harnesses that need the real application are usually run one at a time, by whoever is working on the
thing that harness covers. Nothing runs them as a set unless somebody writes the runner —
TailBlazer's sandbox runner deliberately refuses every harness that drives the Avalonia build, so for that set
there was no runner at all.

The cost of not having one, over six steps of a UI port: **two harnesses reddened by a peer change,
one pre-existing red nobody knew about, three reddened by deleting a single automation id, and one
intermittent** — every one of them red and green again without a build, a fixture or a step-level
instrument noticing.

A sweep of twenty-two harnesses took about fifteen minutes. Points worth building into the runner,
each of which came from getting it wrong:

- **A per-harness timeout**, so one hung harness cannot eat the session.
- **Kill stray application processes before and after each run**, not only after a timeout. A harness
  that finds the application already running reports INCONCLUSIVE, so one hung harness otherwise
  turns into a whole red sweep.
- **Exit with the NUMBER that were not PASS.** A clean sweep is then exit 0 and the caller needs no
  parsing.
- **Name the known non-green in the runner itself.** A reader who has to go and ask which failures
  are expected will eventually stop asking.
- **Redirect each harness's output to its own file and grep it.** Piping through `tail` nearly lost
  the name of a failure here, twice.

## What this does not solve

**Harnesses that send real keystrokes or mouse events still need their own session.** Sending a
window to the back does nothing about input that goes wherever the foreground is. That is what
Windows Sandbox is for, and [SandboxHarnessJournal.md](SandboxHarnessJournal.md) is the account of
making it work.

The division that has held here: **drive through UI Automation and you can run on a shared desktop;
send real input and you need isolation.** Counting which harnesses do which by what they *do* rather
than by which helper they call is worth the five minutes — TailBlazer's own count was wrong for
weeks because it was taken by grepping for one tool's name.
