using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Lands transcribed text in whichever app currently has the keyboard
/// caret (Notepad, browser, Word, Discord, Facebook compose box, etc.).
///
/// Strategy (in order of preference):
///   1. If our own window is foreground, swap focus back to the last
///      external window tracked by <see cref="ForegroundWindowTracker"/>.
///      We poll the OS until the foreground actually changed instead of
///      relying on a blind <see cref="Thread.Sleep"/>.
///   2. Type the text via SendInput Unicode (KEYEVENTF_UNICODE). This
///      delivers each character directly to the focused control without
///      relying on the clipboard or simulated Ctrl+V, so it works in
///      every text input on Windows — including apps that ignore
///      clipboard-based pastes (some Electron apps, certain overlays).
///   3. Also mirror the text onto the system clipboard so the user can
///      paste it manually elsewhere if they wish (and so the mobile
///      "copy-after-send" workflow stays consistent).
///
/// <see cref="LastDiagnostic"/> surfaces a one-line trace of what
/// happened during the most recent call ("typed 23 chars into hwnd=…")
/// so the UI can show it on the status bar when something goes wrong.
/// </summary>
public static class ClipboardPasteHelper
{
    /// <summary>One-line trace of the most recent paste attempt — set
    /// after every <see cref="CopyAndPaste"/> call so the UI can surface
    /// it on the status bar.</summary>
    public static string LastDiagnostic { get; private set; } = "";

    /// <summary>Sends [text] to whichever app currently has the caret.</summary>
    public static void CopyAndPaste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            LastDiagnostic = "skipped (empty text)";
            return;
        }

        var trace = new StringBuilder();

        // Mirror to clipboard first so the user can also paste manually.
        var clipboardOk = TrySetClipboard(text);
        trace.Append(clipboardOk ? "clip✓ " : "clip✗ ");

        // If we're the foreground window, swap to the last external target.
        if (IsForegroundOurs())
        {
            var target = ForegroundWindowTracker.LastExternalHwnd;
            if (target == IntPtr.Zero || !IsWindow(target))
            {
                LastDiagnostic = trace.Append(
                    "no target hwnd — focus a Notepad/browser/text field first, then dictate.").ToString();
                return;
            }

            if (!RestoreFocusTo(target))
            {
                LastDiagnostic = trace.Append(
                    $"focus restore failed (hwnd=0x{target.ToInt64():X}).").ToString();
                return;
            }
            trace.Append("fg✓ ");
        }

        // Type the text directly into the focused control.
        var typed = SendUnicodeText(text);
        trace.Append($"typed {typed}/{text.Length}");
        LastDiagnostic = trace.ToString();
    }

    // -----------------------------------------------------------------
    // Clipboard
    // -----------------------------------------------------------------
    private static bool TrySetClipboard(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                // SetDataObject(copy: true) flushes the data onto the OS
                // clipboard so it survives our process going idle and is
                // visible to clipboard managers / Ctrl+V from any app.
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    // -----------------------------------------------------------------
    // Focus restoration
    // -----------------------------------------------------------------
    private static bool IsForegroundOurs()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Restores focus to <paramref name="hwnd"/> and waits up to ~300ms
    /// for the OS to actually grant the foreground change. Returns true
    /// when the target is foreground at the time of return.
    ///
    /// Correct phantom-Alt ordering: Alt-down → SetForegroundWindow →
    /// Alt-up. The earlier implementation released Alt before calling
    /// SetForegroundWindow, which on some Windows builds causes our own
    /// WPF window to interpret the Alt-up as a menu mnemonic and steal
    /// focus back before the paste fires.
    /// </summary>
    private static bool RestoreFocusTo(IntPtr hwnd)
    {
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_KEYUP = 0x0002;

        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);          // Alt down
        try
        {
            ShowWindow(hwnd, SW_SHOW);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Alt up
        }

        // Poll until the target IS the foreground (or we time out).
        const int totalMs = 300;
        const int stepMs  = 15;
        for (int waited = 0; waited < totalMs; waited += stepMs)
        {
            if (GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(stepMs);
        }
        return GetForegroundWindow() == hwnd;
    }

    // -----------------------------------------------------------------
    // SendInput Unicode typing (no clipboard, no Ctrl+V)
    // -----------------------------------------------------------------
    /// <summary>
    /// Types [text] into the currently-focused control via SendInput
    /// with KEYEVENTF_UNICODE. Returns the number of characters that
    /// were successfully queued.
    ///
    /// Newlines are sent as Enter keystrokes (VK_RETURN) so they trigger
    /// the target app's "new line" / "send message" behaviour the way
    /// the user expects.
    /// </summary>
    private static int SendUnicodeText(string text)
    {
        var inputs = new List<INPUT>(capacity: text.Length * 2 + 4);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // Swallow \r in \r\n pairs — the \n that follows will do the work.
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue;
            if (c == '\n' || c == '\r')
            {
                inputs.Add(NewVkKey(VK_RETURN, keyUp: false));
                inputs.Add(NewVkKey(VK_RETURN, keyUp: true));
                continue;
            }

            inputs.Add(NewUnicodeKey(c, keyUp: false));
            inputs.Add(NewUnicodeKey(c, keyUp: true));
        }

        if (inputs.Count == 0) return 0;

        var arr = inputs.ToArray();
        // SendInput returns the number of input events successfully queued.
        var queued = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        // Two events per character → divide to report char count.
        return (int)(queued / 2);
    }

    private static INPUT NewUnicodeKey(char c, bool keyUp) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0)
            }
        }
    };

    private static INPUT NewVkKey(ushort vk, bool keyUp) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };

    // -----------------------------------------------------------------
    // Win32 interop
    // -----------------------------------------------------------------
    private const int    INPUT_KEYBOARD    = 1;
    private const uint   KEYEVENTF_KEYUP   = 0x0002;
    private const uint   KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN         = 0x0D;
    private const int    SW_SHOW           = 5;

    // Windows INPUT struct is 40 bytes on x64 (28 on x86). The union must
    // be sized for its LARGEST variant (MOUSEINPUT), not just KEYBDINPUT,
    // otherwise Marshal.SizeOf<INPUT>() returns the wrong stride and
    // SendInput silently rejects every call with cbSize mismatch — exactly
    // the failure mode that made the older Ctrl+V helper appear to
    // succeed (no exception) while typing nothing.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT    mi;
        [FieldOffset(0)] public KEYBDINPUT    ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint   uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
