// A worked example for docs/ai-drivable-ui.md, section 5: TailBlazer's helper that keeps harness
// windows out of the person's way. It is not compiled here: it needs Avalonia 12 and runs inside the
// application whose windows it places. Written for TailBlazer's Avalonia port, and moved here on
// 2026-09-24 under this repository's MIT licence, with its code exactly as it ran there.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace TailBlazer.Avalonia.Infrastructure;

/// <summary>Which edge a harness window is parked against.</summary>
internal enum ParkSide
{
    /// <summary>Hard against the right-hand edge of the rightmost screen. The default.</summary>
    Right,

    /// <summary>Hard against the left-hand edge of the leftmost screen.</summary>
    Left
}

/// <summary>
/// Parks windows out of the owner's way and behind their work while a harness is driving, and does
/// nothing at all otherwise.
/// </summary>
/// <remarks>
/// <b>THE HARNESSES TAKE OVER THE OWNER'S DESKTOP, AND ON 2026-09-21 THE OWNER SAID SO — TWICE.</b>
/// A sweep runs the whole set of Avalonia harnesses; each starts this application, and a newly
/// started window takes the foreground whenever the session is unlocked and nobody is giving input.
/// None of them asks for it: <c>SetForegroundWindow</c> appears in none of them, so the interruption
/// is the default behaviour of starting a process rather than anything the harnesses want.
/// <para>
/// <b>THE SECOND COMPLAINT IS WHY THIS IS A CLASS RATHER THAN A METHOD ON <c>MainWindow</c>.</b>
/// The first fix placed the shell window and stopped there, and the very next run put a window back
/// on the owner's screen — <c>ShowForResult</c>'s modal dialog, which is a different <c>Window</c>
/// created somewhere else and <b>activated by <c>ShowDialog</c> whatever it is asked for</b>. Every
/// window this application opens has to go through here.
/// </para>
///
/// <para>
/// <b>TWO MECHANISMS, AND BOTH EARN THEIR PLACE. Parking puts the window OUT OF THE WAY; the
/// Z-order call puts it BEHIND.</b> Neither alone is the whole answer, and the history of this
/// class is two rounds of believing one of them was.
/// </para>
/// <para>
/// <b>THE NAME USED TO SAY "OFF SCREEN" AND THAT WAS NEVER TRUE.</b> Windows clamps a top-level
/// window back onto the virtual screen, so asking for a coordinate past the last monitor cannot
/// work: on a 2880-wide display a 2026-wide window asked for 2944 landed at 854 — right edge flush
/// to the screen edge — and on a 5120-wide one asking for 5184 landed a 1016-wide window at 4104.
/// Both on the desktop, every time.
/// </para>
/// <para>
/// <b>AND THEN THE OPPOSITE MISTAKE: THIS CLASS RECORDED THE POSITION AS ACHIEVING NOTHING, WHICH IS
/// ALSO WRONG.</b> Parking hard against an edge is genuinely useful — a 1016-wide window in the far
/// right fifth of a 5120-wide monitor is out of the way. It read as useless because <b>every
/// measurement behind that conclusion was taken over Remote Desktop</b>, where the same clamp put a
/// 2026-wide window across 70% of a 2880-wide screen. The owner, sitting at the console, observed
/// it working and said so. <b>How much parking buys you scales with screen width against window
/// width, which is exactly why it is not the whole answer on its own.</b>
/// </para>
/// <para>
/// <b>SO THE POSITION IS COMPUTED EXACTLY RATHER THAN LEFT TO THE CLAMP</b>, in
/// <see cref="ParkAndSendToBack"/>, once the window is open and has a real size. Relying on the
/// clamp meant relying on undocumented behaviour to produce the one useful thing the code did.
/// </para>
/// <para>
/// <b>Never at a negative coordinate.</b> A window at -32000 is where Windows parks a minimised
/// one, and a window that has never been composited is exactly what captures blank. Parking LEFT
/// therefore goes to the leftmost screen's own edge, not to a negative offset that would be clamped
/// back to it.
/// </para>
///
/// <para>
/// <b>THE Z-ORDER HALF, AND IT IS THE ONE THAT ALWAYS WORKS.</b> <c>SetWindowPos</c> with
/// <c>HWND_BOTTOM</c> puts the window behind everything. <b>Measured on 33 visible top-level
/// windows: position 31 of 32 with the variable set, against 3 of 32 without.</b> Before this
/// existed, <c>ShowActivated = false</c> was doing all the work — and a window that merely lacks
/// focus is still in front of whatever it opened over, so on a clear desktop it would have been
/// seen.
/// </para>
/// <para>
/// <b>IT IS <c>SetWindowPos</c> AND NOT <c>SendMessage</c>.</b> Z-order is not a window message;
/// there is no <c>WM_</c> to post for it.
/// </para>
///
/// <para>
/// <b>IT IS SAFE TO CAPTURE A PARKED, BACKMOST WINDOW, AND THAT IS THE FACT THE WHOLE THING RESTS
/// ON.</b> Every harness photographs with <c>PrintWindow</c>, which asks the window to draw itself
/// into a bitmap rather than copying the screen — <c>SandboxHarnessJournal.md</c>, beside this file, says so
/// in as many words, arguing these harnesses "do not even need a clear desktop". <b>Proved rather
/// than assumed</b>: <c>verify-avalonia-scroll-marks</c> carries a blankness guard and reads pixel
/// positions for its assertion, and it passes both parked off the edge and at the bottom of the
/// Z-order — 105 marks counted and placed.
/// </para>
/// <para>
/// <b>Product code changed for a harness, gated on a variable never set in normal use.</b> It
/// follows the owner's standing directive of 2026-09-20 in spirit: where the shell has to help
/// automation, the help goes in <c>TailBlazer.Avalonia</c> rather than being worked around in the
/// <c>.ps1</c>. <c>Enter-HarnessSettings</c> sets the variable and <c>Exit-HarnessSettings</c> puts
/// it back, the same save-and-restore the settings folder already gets.
/// </para>
/// <para>
/// The full account is <c>SharedDesktopHarnessJournal.md</c>, beside this file.
/// </para>
/// </remarks>
internal static class HarnessWindowPlacement
{
    /// <summary>
    /// Set to <c>right</c> or <c>left</c> to park that way; any other non-empty value parks right.
    /// </summary>
    /// <remarks>
    /// <b>The owner's preference, 2026-09-22, and right is the default because it is what the owner uses.</b>
    /// A side is worth having rather than assuming: which edge is out of the way depends on where
    /// somebody keeps their work, and on a very wide monitor both edges are genuinely spare.
    /// </remarks>
    private const string Variable = "TAILBLAZER_WINDOW_PARK";

    /// <summary>Whether a harness has asked for windows to be kept out of the way.</summary>
    internal static bool Requested => Side is not null;

    /// <summary>Which edge to park against, or null when no harness asked.</summary>
    private static ParkSide? Side
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrWhiteSpace(value)) return null;

            //Anything that is not "left" parks right, deliberately. A typo in a harness variable
            //should leave the windows out of the way rather than back over the owner's work.
            return value.Trim().Equals("left", StringComparison.OrdinalIgnoreCase)
                ? ParkSide.Left
                : ParkSide.Right;
        }
    }

    /// <summary>
    /// Arranges for <paramref name="window"/> to be parked and sent to the back, if a harness
    /// asked. Call it before showing.
    /// </summary>
    /// <param name="owner">
    /// The window this one opens over, when it has one. <b>A dialog follows its owner rather than
    /// working the edge out again</b>: the owner is already parked, and
    /// <c>WindowStartupLocation.CenterOwner</c> cannot be relied on to keep a dialog there — which
    /// is the bug this parameter exists because of.
    /// </param>
    internal static void ApplyTo(Window window, Window? owner = null)
    {
        if (!Requested) return;

        //ShowDialog activates its window whatever this says, which is why it is the Z-order call
        //below that does the work and this is only a courtesy for the windows that are not modal.
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        //A window has no platform handle and no settled size until it is open, and BOTH are needed:
        //the handle for SetWindowPos, the size to work out where its left edge goes.
        window.Opened += (_, _) => ParkAndSendToBack(window, owner);
    }

    /// <summary>
    /// Parks the window against the configured edge and puts it at the bottom of the Z-order.
    /// </summary>
    /// <remarks>
    /// <b>It unsubscribes nothing and is hooked per window</b>, because the closure carries the
    /// owner. <c>Opened</c> is raised once per <c>Show</c>; a window shown a second time is being
    /// shown by a harness that wants it parked again.
    /// </remarks>
    private static void ParkAndSendToBack(Window window, Window? owner)
    {
        window.Position = owner is not null
            ? owner.Position + new PixelPoint(40, 40)
            : ParkedPosition(window);

        // AN OWNED WINDOW IS NEVER SENT TO THE BACK, AND THIS COST A REGRESSION TO LEARN.
        //
        // A modal dialog belongs ABOVE its owner - that is what modal means - and pushing one to
        // HWND_BOTTOM breaks the ShowDialog handshake it is in the middle of.
        // verify-avalonia-icon-selector caught it exactly: "a dialog still on screen means the
        // result never reached the awaiting caller", with three of its four arms passing first, so
        // it read as flakiness rather than as a break.
        //
        // The dialog is already out of the way by following its owner, which is parked.
        if (owner is not null) return;

        if (!OperatingSystem.IsWindows()) return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        // AND NEVER ACTIVATED, BY ANYBODY - the owner's report of 2026-09-24. ShowActivated = false
        // only stops the window activating ITSELF as it opens. When the owner's own window closes or
        // minimises with nothing of theirs above the harness window, Windows hands activation to the
        // next window down the Z-order, and that was sometimes this one: keyboard focus taken with no
        // harness asking for it. WS_EX_NOACTIVATE is the style Windows skips when it chooses, and it
        // also keeps a click from activating the window. Nothing here needs activation - the
        // harnesses drive through automation patterns, and the two that need the foreground ask for
        // -OnScreen, which never reaches this method.
        var extended = GetWindowLongPtr(handle, GwlExStyle);
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extended.ToInt64() | WsExNoActivate));

        //A failed call is not worth failing a harness over - the harness's own assertions are what
        //report trouble, and a window in the wrong place is visible rather than silent.
        _ = SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>
    /// Hard against the configured edge, computed from the screens and the window's own width.
    /// </summary>
    /// <remarks>
    /// A harness must not fail because of a helper, so an unavailable <c>Screens</c> falls back to
    /// leaving the window where it is rather than throwing or guessing a coordinate.
    /// </remarks>
    private static PixelPoint ParkedPosition(Window window)
    {
        try
        {
            var screens = window.Screens?.All;
            if (screens is not { Count: > 0 }) return window.Position;

            var width = window.FrameSize is { } frame
                ? (int)(frame.Width * (window.Screens?.ScreenFromWindow(window)?.Scaling ?? 1))
                : window.Bounds.Width > 0 ? (int)window.Bounds.Width : 0;

            return Side == ParkSide.Left
                ? new PixelPoint(screens.Min(screen => screen.Bounds.X), 0)
                : new PixelPoint(screens.Max(screen => screen.Bounds.Right) - width, 0);
        }
        catch
        {
            return window.Position;
        }
    }

    private static readonly IntPtr HwndBottom = new(1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;

    //The Ptr forms exist only in the 64-bit user32; a 32-bit process has the plain ones, which take
    //and return a 32-bit LONG. Chosen per call rather than assumed.
    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, value)
            : new IntPtr(SetWindowLong32(hWnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);
}
