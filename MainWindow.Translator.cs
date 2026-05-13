using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VoiceTypingDesktop.Services;
using VoiceTypingDesktop.Services.Translation;
using VoiceTypingDesktop.ViewModels;

namespace VoiceTypingDesktop;

/// <summary>
/// Translator tab behaviour. Kept in a partial file so MainWindow.xaml.cs
/// stays focused on core window/chrome concerns.
/// </summary>
public partial class MainWindow
{
    // -----------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------
    private TranslationService? _translator;
    private CancellationTokenSource? _translationCts;
    private readonly ObservableCollection<TranslationHistoryItem> _translations = new();
    private DispatcherTimerLike? _debounceTimer; // implemented below
    private bool _translatorInitialised;
    private bool _suppressTextChange;
    private Storyboard? _loaderSpin;

    // -----------------------------------------------------------------
    // Init (called lazily when the Translator tab first becomes visible)
    // -----------------------------------------------------------------
    private void EnsureTranslatorInitialised()
    {
        if (_translatorInitialised) return;
        _translatorInitialised = true;

        _translator = new TranslationService();

        // Populate combo boxes. Source accepts "Auto" as first entry.
        var sourceOptions = new System.Collections.Generic.List<LanguageCatalog.LanguageOption> { LanguageCatalog.Auto };
        sourceOptions.AddRange(LanguageCatalog.All);
        SourceLangCombo.ItemsSource = sourceOptions;
        TargetLangCombo.ItemsSource = LanguageCatalog.All;

        SourceLangCombo.DisplayMemberPath = "Name";
        TargetLangCombo.DisplayMemberPath = "Name";

        // Default: auto → English. If user's config has a remembered target,
        // honour it. (We reuse theme config just to avoid schema churn —
        // dedicated fields can be added later.)
        SourceLangCombo.SelectedIndex = 0; // auto
        TargetLangCombo.SelectedItem  = LanguageCatalog.All.FirstOrDefault(l => l.Code == "en")
                                        ?? LanguageCatalog.All[0];

        TranslationHistoryList.ItemsSource = _translations;
        foreach (var it in TranslationHistoryService.Load())
            _translations.Add(it);

        // Loader spinner animation (360° in 1 s, repeats).
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(anim, LoaderRotate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));
        _loaderSpin = new Storyboard();
        _loaderSpin.Children.Add(anim);

        _debounceTimer = new DispatcherTimerLike(TimeSpan.FromMilliseconds(600), () => _ = TriggerAutoTranslateAsync());

        UpdateSourceCount();
        UpdateLangHeaders();
        // Provider chip removed from the redesigned Translator header,
        // so we no longer surface the active provider name here.
    }

    // -----------------------------------------------------------------
    // UI event handlers
    // -----------------------------------------------------------------
    private void Lang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_translatorInitialised) return;
        UpdateLangHeaders();
        // Auto-translate is always on after the redesign — just
        // (re)start the debounce so the new language pair triggers a
        // re-translate ~600 ms after the last change.
        _debounceTimer?.Restart();
    }

    private void SwapLangs_Click(object sender, RoutedEventArgs e)
    {
        if (SourceLangCombo.SelectedItem is not LanguageCatalog.LanguageOption src) return;
        if (TargetLangCombo.SelectedItem is not LanguageCatalog.LanguageOption tgt) return;
        if (src.Code == "auto")
        {
            // Cannot swap "auto" to target. Pick English as a reasonable source.
            SourceLangCombo.SelectedItem = LanguageCatalog.All.FirstOrDefault(l => l.Code == tgt.Code)
                                           ?? LanguageCatalog.All[0];
            TargetLangCombo.SelectedItem = LanguageCatalog.All.FirstOrDefault(l => l.Code == "en")
                                           ?? LanguageCatalog.All[0];
            return;
        }
        SourceLangCombo.SelectedItem = LanguageCatalog.All.FirstOrDefault(l => l.Code == tgt.Code);
        TargetLangCombo.SelectedItem = LanguageCatalog.All.FirstOrDefault(l => l.Code == src.Code);

        // Also swap the texts so the user can "continue translating backwards".
        var left  = SourceTextBox.Text;
        var right = TargetTextBox.Text;
        _suppressTextChange = true;
        SourceTextBox.Text = right;
        TargetTextBox.Text = left;
        _suppressTextChange = false;

        UpdateSourceCount();
        _debounceTimer?.Restart();
    }

    private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;
        UpdateSourceCount();
        _debounceTimer?.Restart();
    }

    private async void TranslateBtn_Click(object sender, RoutedEventArgs e)
        => await RunTranslationAsync(reason: "manual");

    private async Task TriggerAutoTranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceTextBox.Text)) return;
        await RunTranslationAsync(reason: "auto");
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
    {
        var t = TargetTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(t)) { StatusBarText.Text = "Nothing to copy."; return; }
        Clipboard.SetText(t);
        StatusBarText.Text = "Translation copied.";
    }

    private void ClearSource_Click(object sender, RoutedEventArgs e)
    {
        SourceTextBox.Clear();
        TargetTextBox.Clear();
        TranslationStatusText.Text = string.Empty;
        UpdateSourceCount();
    }

    private void PasteFromMobile_Click(object sender, RoutedEventArgs e)
    {
        var latest = _receivedTexts.LastOrDefault();
        if (latest == null)
        {
            StatusBarText.Text = "No mobile message yet.";
            return;
        }
        SourceTextBox.Text = latest.Text;
        SourceTextBox.CaretIndex = SourceTextBox.Text.Length;
        StatusBarText.Text = "Loaded latest mobile message.";
        _debounceTimer?.Restart();
    }

    private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is TranslationHistoryItem item)
        {
            _suppressTextChange = true;
            SourceTextBox.Text = item.SourceText;
            TargetTextBox.Text = item.TranslatedText;
            SourceLangCombo.SelectedItem =
                (item.SourceLang == "auto" ? (object)LanguageCatalog.Auto
                 : LanguageCatalog.All.FirstOrDefault(l => l.Code == item.SourceLang))
                ?? LanguageCatalog.Auto;
            TargetLangCombo.SelectedItem =
                LanguageCatalog.All.FirstOrDefault(l => l.Code == item.TargetLang);
            _suppressTextChange = false;
            UpdateSourceCount();
            UpdateLangHeaders();
        }
    }

    private void ClearTranslationHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_translations.Count == 0) return;
        var ok = MessageBox.Show(this,
            "Clear all translation history?",
            "Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;
        _translations.Clear();
        TranslationHistoryService.Save(_translations);
    }

    // -----------------------------------------------------------------
    // Core translation
    // -----------------------------------------------------------------
    private async Task RunTranslationAsync(string reason)
    {
        EnsureTranslatorInitialised();
        if (_translator == null) return;

        var text = (SourceTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) { TargetTextBox.Text = string.Empty; return; }

        var src = (SourceLangCombo.SelectedItem as LanguageCatalog.LanguageOption)?.Code ?? "auto";
        var tgt = (TargetLangCombo.SelectedItem as LanguageCatalog.LanguageOption)?.Code ?? "en";

        if (src == tgt)
        {
            TargetTextBox.Text = text;
            TranslationStatusText.Text = "same language";
            return;
        }

        // Cancel previous request to avoid out-of-order results.
        _translationCts?.Cancel();
        _translationCts = new CancellationTokenSource();
        var ct = _translationCts.Token;

        ShowLoader(true);
        TranslationStatusText.Text = reason == "auto" ? "translating..." : "translating...";

        try
        {
            var result = await _translator.TranslateAsync(text, src, tgt, ct);
            if (ct.IsCancellationRequested) return;

            TargetTextBox.Text = result.TranslatedText;
            TranslationStatusText.Text = result.DetectedSourceLang is { Length: > 0 }
                ? $"detected: {result.DetectedSourceLang}"
                : "done";

            // Save to history. De-dupe exact match with the previous top item.
            var item = new TranslationHistoryItem
            {
                SourceText = text,
                TranslatedText = result.TranslatedText,
                SourceLang = src,
                TargetLang = tgt,
                CreatedAt = DateTime.Now
            };

            if (_translations.Count == 0 ||
                _translations[0].SourceText     != item.SourceText ||
                _translations[0].TranslatedText != item.TranslatedText ||
                _translations[0].TargetLang     != item.TargetLang)
            {
                _translations.Insert(0, item);
                while (_translations.Count > 200) _translations.RemoveAt(_translations.Count - 1);
                TranslationHistoryService.Save(_translations);
            }
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            TargetTextBox.Text = string.Empty;
            TranslationStatusText.Text = "error";
            StatusBarText.Text = "Translate failed: " + ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested) ShowLoader(false);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------
    private void UpdateSourceCount()
    {
        var len = SourceTextBox?.Text?.Length ?? 0;
        if (SourceCharCount != null) SourceCharCount.Text = $"{len} chars";
    }

    private void UpdateLangHeaders()
    {
        if (SourceLangCombo.SelectedItem is LanguageCatalog.LanguageOption src &&
            SourceHeaderText != null)
            SourceHeaderText.Text = $"SOURCE  •  {src.Flag} {src.Name}".ToUpper();
        if (TargetLangCombo.SelectedItem is LanguageCatalog.LanguageOption tgt &&
            TargetHeaderText != null)
            TargetHeaderText.Text = $"TRANSLATION  •  {tgt.Flag} {tgt.Name}".ToUpper();
    }

    private void ShowLoader(bool visible)
    {
        if (TranslateLoader == null) return;
        TranslateLoader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) _loaderSpin?.Begin();
        else         _loaderSpin?.Stop();
    }

    // -----------------------------------------------------------------
    // Tiny debounce timer using WPF Dispatcher — avoids extra deps.
    // -----------------------------------------------------------------
    private sealed class DispatcherTimerLike
    {
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private readonly Action _onTick;

        public DispatcherTimerLike(TimeSpan delay, Action onTick)
        {
            _onTick = onTick;
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                _onTick();
            };
        }

        public void Restart()
        {
            _timer.Stop();
            _timer.Start();
        }
    }
}
