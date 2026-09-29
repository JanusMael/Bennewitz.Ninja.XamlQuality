---
name: drivable-ui
description: >-
  Build and verify a desktop UI so an agent can drive and test it through its automation tree —
  automation peers, AutomationIds, UI Automation harnesses that send no input, headless fixtures and
  captures. Use this WHENEVER the work touches a desktop UI's testability: "write a harness for this
  view", "test this in the running app", "drive the app through UI Automation", "add an AutomationId",
  "this control is invisible to automation", "the harness can't
  find the control", "verify the UI change actually works" — and whenever you are about to work around
  what the automation tree reports instead of fixing it. Sets the rules for the application, picks the
  instrument and the layer for each claim, and refuses to believe any check until it has been seen to fail.
---

The application owns its automation surface. When the automation tree cannot answer a question
honestly, the fix goes in the application — a peer, an id, an honest name — not in a clever read in
the test script; a workaround helps one harness, a peer helps every later harness. And no check is
believed until it has been seen to fail: the tests that cost the most were
the ones that passed for the wrong reason. The full method, with the evidence and the traps, is
XamlQuality's `docs/ai-drivable-ui.md`, the one living copy; read it before building the first
harness in a project, from a local clone of `JanusMael/Bennewitz.Ninja.XamlQuality` when there is
one and otherwise from
`https://raw.githubusercontent.com/JanusMael/Bennewitz.Ninja.XamlQuality/main/docs/ai-drivable-ui.md`.

## 1. Make the application honest to automation

- **An explicit `AutomationId` on every control a test or a user must reach**, and never a search by
  visible text.
- **An explicit `Name` a person would read aloud** — never a data object's `ToString()` fallback.
- **A peer for every custom control.** A bare `Control` subclass is invisible to a control-view
  search, so its id appears nowhere; override `OnCreateAutomationPeer`, `internal` is fine.
- **Never advertise a pattern the peer does not honour** — withdraw it or make it work.
- **What is not on screen is not in the tree** — hide it, do not clip or zero-size it.
- **Publish desktop state** (session, foreground) on the main window's `HelpText`; **make state
  seedable from files**; **gate test-mode behaviour** (isolated settings, window parking) on
  environment variables a harness sets and restores.

## 2. Pick the instrument for the claim

| Claim | Instrument |
|---|---|
| Where layout put it | Layout bounds, in a headless fixture — relative to the **parent**; translate for the window |
| What automation sees | The peer's rectangle and properties — **blind to clipping** |
| What was drawn | A capture (`PrintWindow` with `PW_RENDERFULLCONTENT`, or headless rendering) |

## 3. Pick the layer

Headless fixtures over the real views first. Then runtime harnesses through UI Automation patterns
with **no synthetic input**, which run beside a person working. Harnesses needing the **foreground**
go to the person as a command. Real-input harnesses need an isolated session — keep that set small,
and count each harness by what it does, not by which helper it calls.

## 4. Write the check, then break it

Isolate settings; seed state; find the window by **process id** and scope every search to it and to
the owning container; poll with a deadline; PASS / FAIL / INCONCLUSIVE as 0 / 1 / 2; guard every
negative; give every claim a control arm; assert after the **second** value. Then **break the code
under it, see it go red, restore, rebuild both legs** — and prove a new runtime harness against a
worktree build of the base commit.

## 5. Run them as a set, with the person's consent

A sweep runner with per-harness timeouts, stray-process cleanup, one output file per harness, and an
exit code equal to the number not PASS. **Ask before taking the machine, in one bucket.** Never pipe
a harness through `tail`.

## Critical details (easy to get wrong)

- **A search rooted at the desktop reaches other applications.** One found and invoked another app's
  `Close` button. Scope by process id, always.
- **Two instruments disagreeing means doubt the harness first.** Every false finding here came from
  asserting on one instrument while reasoning about another.
- **An automation property must not be sourced from what it is used to check**, or the assertion
  compares a value with itself and stays green for ever.
- **A detector's frame of reference is part of its claim.** A parent-relative overflow check passed
  while a whole column sat off the window.
- **A mechanism no fixture can drive is a mechanism nobody is watching.** A headless
  `DispatcherTimer` never fires; the branch built on it was broken in the running app.
- **A fix that chooses what a user loses is the owner's decision**, even when it satisfies the check.
