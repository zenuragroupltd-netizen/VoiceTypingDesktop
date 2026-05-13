using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Registers a user-configurable system-wide hotkey. The key combo is
/// expressed as a modifier mask (Ctrl/Alt/Shift/Win) plus a Windows
/// virtual-key code. Call <see cref="Register"/> once with the window,
/// then <see cref="SetHotkey"/> whenever the combo changes.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HOTKEY_ID = 0x7001;

    // Windows modifier flags for RegisterHotKey
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;
    private const uint MOD_WIN     = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Attach to the window's message loop. Call once at window load.
    /// </summary>
    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle == IntPtr.Zero
            ? helper.EnsureHandle()
            : helper.Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Binds a new hotkey. Pass null/empty binding to clear.
    /// Returns true on success.
    /// </summary>
    public bool SetHotkey(HotkeyBinding? binding)
    {
        Unregister();

        if (binding == null || binding.VirtualKey == 0) return true; // cleared

        uint mods = 0;
        if (binding.Ctrl)  mods |= MOD_CONTROL;
        if (binding.Alt)   mods |= MOD_ALT;
        if (binding.Shift) mods |= MOD_SHIFT;
        if (binding.Win)   mods |= MOD_WIN;
        mods |= MOD_NOREPEAT;

        _registered = RegisterHotKey(_hwnd, HOTKEY_ID, mods, (uint)binding.VirtualKey);
        return _registered;
    }

    public bool IsRegistered => _registered;

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 /*WM_HOTKEY*/ && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// A user-chosen keyboard shortcut. Serialised in the config as a string
/// like "Ctrl+Alt+V" or "Ctrl+Shift+Space".
/// </summary>
public sealed class HotkeyBinding
{
    public bool Ctrl  { get; set; }
    public bool Alt   { get; set; }
    public bool Shift { get; set; }
    public bool Win   { get; set; }
    /// <summary>Windows virtual-key code (see VK_* constants).</summary>
    public int  VirtualKey { get; set; }
    /// <summary>Friendly key name for display, e.g. "V" or "F9".</summary>
    public string KeyName { get; set; } = string.Empty;

    public string DisplayText
    {
        get
        {
            if (VirtualKey == 0) return "Not set";
            var parts = new System.Collections.Generic.List<string>();
            if (Ctrl)  parts.Add("Ctrl");
            if (Alt)   parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win)   parts.Add("Win");
            parts.Add(string.IsNullOrEmpty(KeyName) ? "?" : KeyName);
            return string.Join(" + ", parts);
        }
    }

    public string Serialize() => VirtualKey == 0
        ? string.Empty
        : $"{(Ctrl?"1":"0")}{(Alt?"1":"0")}{(Shift?"1":"0")}{(Win?"1":"0")}|{VirtualKey}|{KeyName}";

    public static HotkeyBinding? Deserialize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split('|');
        if (parts.Length < 3) return null;
        if (parts[0].Length != 4) return null;
        if (!int.TryParse(parts[1], out var vk)) return null;
        return new HotkeyBinding
        {
            Ctrl  = parts[0][0] == '1',
            Alt   = parts[0][1] == '1',
            Shift = parts[0][2] == '1',
            Win   = parts[0][3] == '1',
            VirtualKey = vk,
            KeyName = parts[2]
        };
    }

    /// <summary>
    /// Build a binding from a WPF KeyEventArgs (used by the "press keys to set"
    /// input box).
    /// </summary>
    public static HotkeyBinding? FromKeyEvent(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses.
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt  || key == Key.RightAlt  ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin ||
            key == Key.None)
            return null;

        var mods = Keyboard.Modifiers;
        return new HotkeyBinding
        {
            Ctrl  = mods.HasFlag(ModifierKeys.Control),
            Alt   = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win   = mods.HasFlag(ModifierKeys.Windows),
            VirtualKey = KeyInterop.VirtualKeyFromKey(key),
            KeyName = key.ToString()
        };
    }
}
