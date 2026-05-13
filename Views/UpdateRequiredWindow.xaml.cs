using System;
using System.Diagnostics;
using System.Windows;
using VoiceTypingDesktop.Services;

namespace VoiceTypingDesktop.Views;

/// <summary>
/// Shown at startup when the Supabase version check reports the client is
/// behind. For a force-update, only Download + Exit are shown. For an
/// optional update, "Remind me later" is also visible and lets the app
/// continue.
/// </summary>
public partial class UpdateRequiredWindow : Window
{
    public bool UserChoseLater { get; private set; }

    private readonly UpdateCheckResult _result;

    public UpdateRequiredWindow(UpdateCheckResult result)
    {
        InitializeComponent();
        _result = result;

        CurrentVersionText.Text = "v" + result.CurrentVersion;
        LatestVersionText.Text  = "v" + result.LatestVersion;

        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
            ? "A newer version of Voice Typing Desktop is available with bug fixes and improvements."
            : result.ReleaseNotes;

        if (result.Decision == UpdateDecision.ForceUpdate)
        {
            HeaderTitle.Text    = "Update Required";
            HeaderSubtitle.Text = "You must update to keep using Voice Typing Desktop.";
            LaterBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderTitle.Text    = "Update Available";
            HeaderSubtitle.Text = "A new version is ready to download.";
            LaterBtn.Visibility = Visibility.Visible;
        }
    }

    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = _result.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this,
                "Download link is not set yet. Please contact support.",
                "No download link",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Couldn't open the download link:\n" + ex.Message,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExitBtn_Click(object sender, RoutedEventArgs e)
    {
        UserChoseLater = false;
        DialogResult = false;
        Close();
    }

    private void LaterBtn_Click(object sender, RoutedEventArgs e)
    {
        // Only reachable for optional updates.
        UserChoseLater = true;
        DialogResult = true;
        Close();
    }
}
