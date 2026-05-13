using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// System tray (notification area) icon + right-click menu.
/// Provides quick access to Show/Hide the window, toggle mic, and Exit,
/// even when the app is minimized / hidden.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Window _window;
    private readonly WinForms.ToolStripMenuItem _micItem;

    public event EventHandler? ToggleMicRequested;

    public TrayIconService(Window window, string tooltip = "Voice Typing Desktop")
    {
        _window = window;
        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = tooltip,
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();

        var showItem = new WinForms.ToolStripMenuItem("Show / Hide");
        showItem.Click += (_, _) => ToggleWindow();
        menu.Items.Add(showItem);

        _micItem = new WinForms.ToolStripMenuItem("Toggle Mic");
        _micItem.Click += (_, _) => ToggleMicRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_micItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _window.Dispatcher.Invoke(() =>
            {
                // Set a flag the MainWindow inspects so Closing actually exits.
                if (_window is MainWindow mw) mw.RequestExit();
                Application.Current.Shutdown();
            });
        };
        menu.Items.Add(exitItem);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ToggleWindow();
    }

    /// <summary>Update tooltip text (e.g. "Mic: ON").</summary>
    public void SetTooltip(string text)
    {
        // Windows tooltips are capped at 127 chars.
        _icon.Text = text.Length > 127 ? text.Substring(0, 127) : text;
    }

    /// <summary>Update the mic menu item label.</summary>
    public void SetMicState(bool on)
    {
        _micItem.Text = on ? "Mic Off" : "Mic On";
    }

    private void ToggleWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
            {
                _window.Hide();
            }
            else
            {
                _window.Show();
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
        });
    }

    private static Icon LoadAppIcon()
    {
        // Try loading from the embedded resource pack first, fall back to the
        // default app icon, then to a system icon if all else fails.
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info != null) return new Icon(info.Stream);
        }
        catch { /* fall through */ }

        try
        {
            var path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(path)) return new Icon(path);
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    private static void Suppressor(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // placeholder so we can remove a hypothetical close handler if needed
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
