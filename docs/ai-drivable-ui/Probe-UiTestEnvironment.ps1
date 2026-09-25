# Which UI-testing approaches does this machine support? It prints the answer and changes nothing.
#
#   pwsh -NoProfile -File docs/ai-drivable-ui/Probe-UiTestEnvironment.ps1
#
# CHOOSE THE APPROACH FROM THE ENVIRONMENT, AND ASK RATHER THAN REMEMBER. On the machine this was
# written on, the same host was a console session at one hour and a Remote Desktop session the next,
# at a different resolution and a different scaling. That changed which approaches were available,
# AND what every measured figure meant. docs/ai-drivable-ui.md, section 4, says which layer of
# verification to reach for; this reports what the machine offers.
#
# It asserts nothing: the output is the point, and the exit code is 0 unless the probe itself fails.
# Only the Windows verdicts come from measurement. Every other verdict is where to start measuring.
#
# Dot-sourcing it, as an editor's F5 does, runs it too; set UITESTPROBE_NOEXEC to load it without
# running it. It leaves the caller's variables and the thread's DPI awareness as it found them.
#
# Written for a WPF-to-Avalonia port, and moved here on 2026-09-24 under this repository's
# MIT licence.
[CmdletBinding()]
param()

function Invoke-UiTestProbe {
    $ErrorActionPreference = 'Stop'

    if (-not ('UiTestProbe.Win32' -as [type])) {
        Add-Type -TypeDefinition @'
namespace UiTestProbe {
    using System;
    using System.Runtime.InteropServices;

    public static class Win32 {
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        // SM_REMOTESESSION: the one API that answers "is this Remote Desktop" without parsing a
        // localised table. $env:SESSIONNAME comes back EMPTY in processes started by a test runner.
        public static bool IsRemote { get { return GetSystemMetrics(0x1000) != 0; } }

        // How this process sees the screen: 0 unaware, 1 system aware, 2 per-monitor aware,
        // -1 unreadable. A harness running unaware reads figures that scaling has shrunk.
        public static int Awareness {
            get {
                try { return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()); }
                catch (EntryPointNotFoundException) { return -1; }
            }
        }

        // Screen, virtual screen and DPI, read per-monitor aware so that scaling cannot shrink
        // them, then the thread's awareness is put back. The last element is 1 when that switch
        // was made, and 0 where it does not exist (before Windows 10 1607) and the read was plain.
        public static int[] Metrics() {
            IntPtr previous = IntPtr.Zero;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }

            try {
                int dpi;
                try { dpi = (int)GetDpiForWindow(GetDesktopWindow()); }
                catch (EntryPointNotFoundException) { dpi = 0; }

                return new[] {
                    GetSystemMetrics(0), GetSystemMetrics(1), GetSystemMetrics(78), GetSystemMetrics(79),
                    dpi, previous != IntPtr.Zero ? 1 : 0,
                };
            }
            finally {
                if (previous != IntPtr.Zero) { SetThreadDpiAwarenessContext(previous); }
            }
        }
    }
}
'@
    }

    function Say([string] $label, [string] $value) {
        Write-Host ('  {0,-22} {1}' -f $label, $value)
    }

    function Verdict([string] $letter, [string] $name, $ok, [string] $why) {
        $mark = if ($ok -eq $true) { 'YES  ' } elseif ($ok -eq $false) { 'NO   ' } else { '?    ' }
        Write-Host ('  {0}  {1}{2,-46} {3}' -f $letter, $mark, $name, $why)
    }

    Write-Host ''
    Write-Host 'UI TEST ENVIRONMENT'
    Write-Host ''

    # ------------------------------------------------------------ 1. which platform
    $onWindows = $IsWindows -or ($PSVersionTable.PSEdition -eq 'Desktop')
    Say 'platform' $(if ($onWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } elseif ($IsMacOS) { 'macOS' } else { 'unknown' })

    # ------------------------------------------------------------ 2. local or remote
    if ($onWindows) {
        $remote = [UiTestProbe.Win32]::IsRemote
        Say 'session' $(if ($remote) { 'REMOTE (rdp)' } else { 'console' })
    }
    else {
        $remote = [bool] $env:SSH_CONNECTION
        Say 'session' $(if ($remote) { 'REMOTE (ssh)' } else { 'local (or unknown)' })
    }

    # ------------------------------------------------------------ 3. what is drawing
    $server = 'Win32'
    if (-not $onWindows) {
        $server = if ($env:WAYLAND_DISPLAY) { 'Wayland' } elseif ($env:DISPLAY) { 'X11' } elseif ($IsMacOS) { 'Quartz' } else { 'NONE (headless)' }
        if ($env:XDG_SESSION_TYPE) { $server += " (XDG_SESSION_TYPE=$env:XDG_SESSION_TYPE)" }
    }
    Say 'display server' $server

    # ------------------------------------------------------------ 4. scaling
    if ($onWindows) {
        $metrics = [UiTestProbe.Win32]::Metrics()
        $awareness = switch ([UiTestProbe.Win32]::Awareness) {
            0 { 'UNAWARE: it reads figures that scaling has shrunk' }
            1 { 'system aware' }
            2 { 'per-monitor aware' }
            default { 'unknown' }
        }

        Say 'display' ('{0} x {1} (virtual {2} x {3})' -f $metrics[0], $metrics[1], $metrics[2], $metrics[3])
        Say 'dpi / scale' ('{0} / {1}x' -f $metrics[4], $(if ($metrics[4] -gt 0) { [Math]::Round($metrics[4] / 96.0, 3) } else { '?' }))
        Say 'this process' $awareness
        if ($metrics[5] -eq 0) {
            Say '' 'read without the per-monitor switch, so a scaled display may read smaller'
        }
    }

    Write-Host ''
    Write-Host 'WHAT THIS MACHINE SUPPORTS'
    Write-Host ''

    if ($onWindows) {
        Verdict 'A' 'automation APIs, no synthetic input' $true 'UI Automation; capture by asking the window to draw itself'
        Verdict 'B' 'synthetic input to the real desktop' $true 'works, but it lands wherever the foreground is - never beside a person'

        $sandbox = Test-Path -LiteralPath (Join-Path $env:windir 'System32\WindowsSandbox.exe')
        if (-not $sandbox) {
            Verdict 'C' 'isolated desktop (Windows Sandbox)' $false 'not installed: the optional feature is off, or this edition lacks it'
        }
        elseif ($remote) {
            Verdict 'C' 'isolated desktop (Windows Sandbox)' $null 'reported not to work over Remote Desktop (not measured); start it from the console'
        }
        else {
            Verdict 'C' 'isolated desktop (Windows Sandbox)' $true 'installed, and this is the console'
        }
    }
    elseif ($IsMacOS) {
        Verdict 'A' 'automation APIs, no synthetic input' $null 'the macOS accessibility API - UNVERIFIED here'
        Verdict 'B' 'synthetic input to the real desktop' $null 'needs the Accessibility permission for the host process - UNVERIFIED here'
        Verdict 'C' 'isolated desktop' $null 'none has been tried on macOS'
    }
    elseif ($server -like 'NONE*') {
        Verdict 'A' 'automation APIs, no synthetic input' $false 'no display server, so no window to drive: start one (Xvfb) first'
        Verdict 'B' 'synthetic input to the real desktop' $false 'no display server to inject into'
        Verdict 'C' 'isolated desktop (Xvfb)' $null 'start Xvfb and set DISPLAY - UNVERIFIED here'
    }
    elseif ($server -like 'Wayland*') {
        Verdict 'A' 'automation APIs, no synthetic input' $null 'AT-SPI carries Avalonia 12''s peers; a harness driving them is UNVERIFIED'
        Verdict 'B' 'synthetic input to the real desktop' $false 'Wayland has no XTEST; injection is compositor-specific and may not exist'
        Verdict 'C' 'isolated desktop' $null 'Xvfb is X11 only; a nested or headless compositor is the Wayland answer - UNVERIFIED here'
    }
    else {
        Verdict 'A' 'automation APIs, no synthetic input' $null 'AT-SPI carries Avalonia 12''s peers; a harness driving them is UNVERIFIED'
        Verdict 'B' 'synthetic input to the real desktop' $null 'X11 has XTEST, usually yes - UNVERIFIED here'
        Verdict 'C' 'isolated desktop (Xvfb / Xephyr)' $null 'usually yes on X11 - UNVERIFIED here'
    }

    Verdict 'D' 'fully headless toolkit rendering' $true 'always available; proves nothing about chrome, focus or Z-order'

    Write-Host ''
    Write-Host '  Only the Windows verdicts are measured, by the harnesses this was written for. Treat any'
    Write-Host '  other verdict as where to start measuring. docs/ai-drivable-ui.md, section 4, says which'
    Write-Host '  layer of verification to reach for.'
    Write-Host ''
}

if (-not $env:UITESTPROBE_NOEXEC) { Invoke-UiTestProbe }
