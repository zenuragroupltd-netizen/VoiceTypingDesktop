using System.Windows;

namespace VoiceTypingDesktop.Views;

public partial class UpdateCheckErrorWindow : Window
{
    public UpdateCheckErrorWindow(string message)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(message))
            DetailText.Text = message;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
