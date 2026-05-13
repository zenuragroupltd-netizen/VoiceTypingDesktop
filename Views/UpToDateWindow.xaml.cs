using System.Windows;

namespace VoiceTypingDesktop.Views;

/// <summary>
/// Friendly confirmation shown by the About → "Check for Updates" button
/// when the Supabase version check reports the app is current.
/// </summary>
public partial class UpToDateWindow : Window
{
    public UpToDateWindow(string currentVersion)
    {
        InitializeComponent();
        DetailText.Text = $"You already have the latest version (v{currentVersion}).";
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
