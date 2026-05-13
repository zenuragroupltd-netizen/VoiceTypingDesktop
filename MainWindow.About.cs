using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using VoiceTypingDesktop.Services;
using VoiceTypingDesktop.Views;

namespace VoiceTypingDesktop;

/// <summary>
/// About-page logic: version display, "Check for Updates" stub, and the
/// social link buttons. Kept in a partial class so MainWindow.xaml.cs
/// stays focused on navigation/voice/translator concerns.
///
/// === HOW TO EDIT ===
///
/// • To replace the developer profile picture:
///     Drop a 256×256 PNG into Assets\profile.png. The build picks it
///     up automatically because the project file uses
///     &lt;Resource Include="Assets\profile.png" /&gt;.
///
/// • To edit the social links shown on the About page:
///     Update the constants in the LINKS region below. Each AbtXxxx_Click
///     handler simply opens the corresponding URL in the user's default
///     browser via OpenUrl().
///
/// • To wire real update checking:
///     Replace the body of CheckForUpdatesAsync() — fetch a JSON manifest
///     from your server (or GitHub Releases API), compare the version,
///     and update AbtUpdateStatus / AbtLastChecked accordingly. The
///     "Check for Updates" button already calls this method async.
/// </summary>
public partial class MainWindow
{
    private bool _aboutInitialised;

    // ===========================================================
    // LINKS — edit these to point at your real channels.
    // ===========================================================
    private const string WebsiteUrl  = "https://kinetimart.com/";
    private const string YouTubeUrl  = "https://www.youtube.com/@kinetimart";
    private const string EmailUrl    = "mailto:hello@kinetimart.com";
    private const string FacebookUrl = "https://www.facebook.com/kinetimart";
    private const string TelegramUrl = "https://t.me/kinetimart";

    /// <summary>
    /// Lazy initialiser called the first time the user opens the About
    /// tab. Reads the assembly version and renders it in both the App
    /// card and the Software Updates card.
    /// </summary>
    private void EnsureAboutInitialised()
    {
        if (_aboutInitialised) return;
        _aboutInitialised = true;

        var v = GetAppVersion();
        if (AbtVersionTextLarge != null)
            AbtVersionTextLarge.Text = $"Version {v}";
        if (AbtCurrentVersion != null)
            AbtCurrentVersion.Text = v;
    }

    private static string GetAppVersion()
    {
        try
        {
            // Prefer the InformationalVersion (set in the .csproj as
            // <Version>); fall back to the assembly version if missing.
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
            {
                // Strip a possible "+commit" suffix that some build tools add.
                var raw = info.InformationalVersion;
                var plus = raw.IndexOf('+');
                return plus >= 0 ? raw.Substring(0, plus) : raw;
            }
            var ver = asm.GetName().Version;
            return ver?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    // ===========================================================
    // Check for Updates — real Supabase check
    // ===========================================================

    /// <summary>
    /// Contacts the <c>app_versions</c> table in Supabase and shows one of
    /// three custom dialogs:
    ///   • UpToDateWindow          — already on the latest version
    ///   • UpdateRequiredWindow    — a newer version is available (force or optional)
    ///   • UpdateCheckErrorWindow  — network / config failure
    ///
    /// The button is disabled while the request is in flight so the user
    /// can't queue up duplicate checks.
    /// </summary>
    private async void AbtCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        // Guard against double-clicks.
        if (AbtCheckUpdatesBtn != null) AbtCheckUpdatesBtn.IsEnabled = false;
        if (AbtCheckUpdatesBtnText != null) AbtCheckUpdatesBtnText.Text = "Checking…";
        if (AbtUpdateStatus != null) AbtUpdateStatus.Text = "Checking…";

        try
        {
            var cfg = App.Config;
            if (!cfg.HasSupabaseCredentials)
            {
                ShowCheckError("The update server is not configured yet. " +
                               "Add your Supabase credentials in appsettings.json.");
                return;
            }

            UpdateCheckResult result;
            using (var sb = new SupabaseClient(cfg.SupabaseUrl, cfg.SupabaseAnonKey))
            {
                var checker = new VersionCheckService(sb);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                result = await checker.CheckAsync(cts.Token);
            }

            // Stamp the "last checked" row so the UI feels alive.
            if (AbtLastChecked != null)
                AbtLastChecked.Text = DateTime.Now.ToString("dd MMM yyyy, HH:mm");

            switch (result.Decision)
            {
                case UpdateDecision.Ok:
                    if (AbtUpdateStatus != null) AbtUpdateStatus.Text = "Up to date";
                    ShowUpToDate(result.CurrentVersion);
                    break;

                case UpdateDecision.OptionalUpdate:
                case UpdateDecision.ForceUpdate:
                    if (AbtUpdateStatus != null)
                        AbtUpdateStatus.Text = result.Decision == UpdateDecision.ForceUpdate
                            ? "Update required"
                            : $"Update available (v{result.LatestVersion})";
                    ShowUpdateAvailable(result);
                    break;

                case UpdateDecision.Unknown:
                default:
                    if (AbtUpdateStatus != null) AbtUpdateStatus.Text = "Couldn't reach server";
                    ShowCheckError("We couldn't reach the update server. " +
                                   "Check your internet connection and try again.");
                    break;
            }
        }
        catch (Exception ex)
        {
            if (AbtUpdateStatus != null) AbtUpdateStatus.Text = "Check failed";
            ShowCheckError(ex.Message);
        }
        finally
        {
            if (AbtCheckUpdatesBtn != null) AbtCheckUpdatesBtn.IsEnabled = true;
            if (AbtCheckUpdatesBtnText != null) AbtCheckUpdatesBtnText.Text = "Check for Updates";
        }
    }

    private void ShowUpToDate(string currentVersion)
    {
        var dlg = new UpToDateWindow(currentVersion) { Owner = this };
        dlg.ShowDialog();
    }

    private void ShowUpdateAvailable(UpdateCheckResult result)
    {
        var dlg = new UpdateRequiredWindow(result) { Owner = this };
        var res = dlg.ShowDialog();

        // When triggered from About (not the startup gate), a force-update
        // decision still shouldn't kill the app unless the user clicks Exit.
        // UpdateRequiredWindow's Exit button sets DialogResult = false and
        // its Download button leaves it unset. We only shut down if the user
        // explicitly chose Exit AND it was a force update at app launch —
        // here the dialog is informational, so we simply return.
        _ = res;
    }

    private void ShowCheckError(string message)
    {
        var dlg = new UpdateCheckErrorWindow(message) { Owner = this };
        dlg.ShowDialog();
    }

    // ===========================================================
    // Profile image fallback
    // ===========================================================

    /// <summary>
    /// Fires when WPF can't load Assets/profile.png (file missing or
    /// corrupt). We hide the Ellipse that paints the photo through an
    /// ImageBrush so the underlying "MI" initials badge keeps showing
    /// as a graceful fallback.
    /// </summary>
    private void AbtProfileImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (AbtProfileImageHost != null)
            AbtProfileImageHost.Visibility = Visibility.Collapsed;
        if (sender is System.Windows.Controls.Image img)
            img.Visibility = Visibility.Collapsed;
    }

    // ===========================================================
    // Social link buttons
    // ===========================================================

    // Each handler delegates to the existing static OpenUrl(...) helper
    // already defined in MainWindow.xaml.cs (used by the existing
    // Telegram/WhatsApp sidebar buttons), so there's only one URL-launch
    // implementation in the whole app.
    private void AbtWebsite_Click(object sender, RoutedEventArgs e)  => OpenUrl(WebsiteUrl);
    private void AbtYouTube_Click(object sender, RoutedEventArgs e)  => OpenUrl(YouTubeUrl);
    private void AbtEmail_Click(object sender, RoutedEventArgs e)    => OpenUrl(EmailUrl);
    private void AbtFacebook_Click(object sender, RoutedEventArgs e) => OpenUrl(FacebookUrl);
    private void AbtTelegram_Click(object sender, RoutedEventArgs e) => OpenUrl(TelegramUrl);
}
