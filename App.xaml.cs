using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoiceTypingDesktop.Config;
using VoiceTypingDesktop.Services;
using VoiceTypingDesktop.Views;

namespace VoiceTypingDesktop;

public partial class App : Application
{
    public static AppConfig Config { get; private set; } = new AppConfig();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load config first so we have Supabase credentials.
        Config = AppConfig.LoadOrCreate();

        // Apply persisted theme as early as possible so the first paint
        // (including the update dialog) uses the right colors.
        ThemeService.Apply(ThemeService.FromString(Config.Theme));

        // Run the version gate BEFORE showing the main window.
        var allowed = await RunVersionGateAsync();
        if (!allowed)
        {
            Shutdown();
            return;
        }

        // All good — show the real app.
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    /// <summary>
    /// Contacts Supabase, decides if this build is allowed to run, and
    /// shows the update dialog if not. Returns true if the app should
    /// continue, false if it should exit.
    /// </summary>
    private static async Task<bool> RunVersionGateAsync()
    {
        // If Supabase isn't configured at all, don't gate — fail open so
        // first-launch / misconfigured installs still work.
        if (!Config.HasSupabaseCredentials) return true;

        UpdateCheckResult result;
        try
        {
            using var sb = new SupabaseClient(Config.SupabaseUrl, Config.SupabaseAnonKey);
            var checker = new VersionCheckService(sb);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            result = await checker.CheckAsync(cts.Token);
        }
        catch
        {
            // Network or other error — fail open.
            return true;
        }

        switch (result.Decision)
        {
            case UpdateDecision.Ok:
            case UpdateDecision.Unknown:
                return true;

            case UpdateDecision.OptionalUpdate:
            {
                var dlg = new UpdateRequiredWindow(result);
                var res = dlg.ShowDialog();
                // true  = user clicked "Remind me later" → continue to app
                // false = user clicked "Exit" / closed dialog → exit
                return res == true;
            }

            case UpdateDecision.ForceUpdate:
            {
                var dlg = new UpdateRequiredWindow(result);
                dlg.ShowDialog();
                // Force-update always blocks regardless of which button
                // was clicked. The "Download" button opens the link but
                // does not let them in.
                return false;
            }

            default:
                return true;
        }
    }
}
