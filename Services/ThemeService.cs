using System;
using System.Windows;
using Microsoft.Win32;

namespace VoiceTypingDesktop.Services;

public enum AppTheme
{
    System = 0,
    Dark   = 1,
    Light  = 2
}

/// <summary>
/// Swaps the active color dictionary at runtime.
/// All control templates use DynamicResource so the change is live.
/// </summary>
public static class ThemeService
{
    private const string DarkPath  = "Themes/Colors.Dark.xaml";
    private const string LightPath = "Themes/Colors.Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>True if the resolved theme (after System) is dark.</summary>
    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var effective = Resolve(theme);
        IsDark = effective == AppTheme.Dark;

        var app = Application.Current;
        if (app == null) return;

        var newDict = new ResourceDictionary
        {
            Source = new Uri(effective == AppTheme.Dark ? DarkPath : LightPath, UriKind.Relative)
        };

        var merged = app.Resources.MergedDictionaries;

        // Find and replace the existing Colors.* dictionary in place to avoid
        // a flash where no palette is loaded.
        int replaceIndex = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Colors.Dark", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Colors.Light", StringComparison.OrdinalIgnoreCase))
            {
                replaceIndex = i;
                break;
            }
        }

        if (replaceIndex >= 0)
            merged[replaceIndex] = newDict;
        else
            merged.Insert(0, newDict);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static AppTheme Resolve(AppTheme requested) =>
        requested == AppTheme.System
            ? (ReadWindowsAppsUseLightTheme() ? AppTheme.Light : AppTheme.Dark)
            : requested;

    /// <summary>Reads the Windows personalization setting for apps.</summary>
    private static bool ReadWindowsAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 1;
        }
        catch { /* default to dark if registry isn't reachable */ }
        return false;
    }

    public static AppTheme FromString(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "light"  => AppTheme.Light,
        "dark"   => AppTheme.Dark,
        _        => AppTheme.System
    };

    public static string ToConfigString(AppTheme t) => t switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark  => "dark",
        _              => "system"
    };
}
