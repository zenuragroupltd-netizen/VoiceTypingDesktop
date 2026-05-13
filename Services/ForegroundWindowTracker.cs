using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Polls the OS foreground window and remembers the most recent one that
/// does NOT belong to our own process. Used so we can restore the user's
/// previous "target" window (Notepad, browser, Word etc.) right before
/// pasting transcribed text.
/// </summary>
public static class ForegroundWindowTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    private static DispatcherTimer? _timer;
    private static uint _myPid;

    /// <summary>
    /// The last foreground window HWND that did NOT belong to this process.
    /// <c>IntPtr.Zero</c> until the tracker has seen one.
    /// </summary>
    public static IntPtr LastExternalHwnd { get; private set; }

    public static void Start()
    {
        if (_timer != null) return;
        _myPid = (uint)Environment.ProcessId;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != _myPid)
            {
                LastExternalHwnd = hwnd;
            }
        };
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>True if the stored HWND still points at a live window.</summary>
    public static bool HasValidTarget() =>
        LastExternalHwnd != IntPtr.Zero && IsWindow(LastExternalHwnd);
}
