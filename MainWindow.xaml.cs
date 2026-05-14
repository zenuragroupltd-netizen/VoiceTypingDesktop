using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoiceTypingDesktop.Services;
using VoiceTypingDesktop.ViewModels;

namespace VoiceTypingDesktop;

public partial class MainWindow : Window
{
    private SupabaseClient? _supabase;
    private DeviceService? _deviceService;
    private MobileSyncService? _syncService;

    private readonly ObservableCollection<ReceivedTextItem> _receivedTexts = new();

    // State
    private bool _isPinned;
    private bool _pairingExpanded = true;
    private bool _reallyClose;      // true = Exit from tray, false = hide to tray
    private TrayIconService? _tray;

    // Refreshes the live diagnostic text every second.
    private DispatcherTimer? _diagTimer;

    // Glyphs (Segoe MDL2 Assets)
    private const string GlyphChevronDown = "\uE70D"; // collapsed → expand
    private const string GlyphChevronUp   = "\uE70E"; // expanded  → collapse
    private const string GlyphSystem      = "\uE770";
    private const string GlyphSun         = "\uE706";
    private const string GlyphMoon        = "\uE708";

    public MainWindow()
    {
        InitializeComponent();

        ReceivedTextsList.ItemsSource = _receivedTexts;
        _receivedTexts.CollectionChanged += ReceivedTexts_CollectionChanged;

        // Load persisted history before startup so user sees old items immediately.
        foreach (var it in HistoryService.Load())
            _receivedTexts.Add(it);
        UpdateEmptyState();

        // Sync settings UI with persisted config.
        var cfg = App.Config;
        SyncThemeRadios(ThemeService.FromString(cfg.Theme));
        UpdateThemeGlyph();

        // The pairing card ALWAYS starts expanded on launch so the user can
        // immediately see the QR code and pair code. They can collapse it
        // with the chevron to save space; that collapse is intentionally
        // session-only (we do not persist it).
        _pairingExpanded = true;
        ApplyPairingExpansion(animate: false);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        Deactivated += MainWindow_Deactivated;
        ThemeService.ThemeChanged += (_, _) => UpdateThemeGlyph();
    }

    // ============================================================
    // Title bar
    // ============================================================

    private void MinBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Called by the tray "Exit" item to bypass the hide-to-tray behavior.</summary>
    public void RequestExit() => _reallyClose = true;

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // ============================================================
    // Theme
    // ============================================================

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        // Cycle: System -> Light -> Dark -> System
        var next = ThemeService.Current switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light  => AppTheme.Dark,
            _               => AppTheme.System
        };
        ApplyTheme(next);
    }

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        ApplyTheme(ThemeService.FromString(tag));
    }

    private void ApplyTheme(AppTheme theme)
    {
        ThemeService.Apply(theme);
        App.Config.Theme = ThemeService.ToConfigString(theme);
        App.Config.Save();
        SyncThemeRadios(theme);
        UpdateThemeGlyph();
    }

    private void SyncThemeRadios(AppTheme theme)
    {
        if (ThemeSystemBtn == null) return;
        ThemeSystemBtn.IsChecked = theme == AppTheme.System;
        ThemeLightBtn.IsChecked  = theme == AppTheme.Light;
        ThemeDarkBtn.IsChecked   = theme == AppTheme.Dark;
    }

    private void UpdateThemeGlyph()
    {
        if (ThemeGlyph == null) return;
        ThemeGlyph.Text = ThemeService.Current switch
        {
            AppTheme.Light  => GlyphSun,
            AppTheme.Dark   => GlyphMoon,
            _               => GlyphSystem
        };
        ThemeBtn.ToolTip = "Theme: " + ThemeService.Current;
    }

    // ============================================================
    // Pin (always on top)
    // ============================================================

    private void PinBtn_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void PinSettingCheck_Click(object sender, RoutedEventArgs e)
    {
        var want = PinSettingCheck.IsChecked == true;
        if (want != _isPinned) TogglePin();
    }

    private void TogglePin()
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;

        PinGlyph.Text = _isPinned ? "\uE840" : "\uE718";
        PinGlyph.Foreground = _isPinned
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("TextSecondary");

        PinStatusLabel.Text = _isPinned ? "Pinned on top" : string.Empty;
        StatusBarText.Text  = _isPinned
            ? "Pinned: staying above all windows."
            : "Unpinned: will minimize when you switch apps.";

        if (PinSettingCheck != null) PinSettingCheck.IsChecked = _isPinned;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        // Auto-minimize when the user switches to another app, UNLESS:
        //   - the window is pinned on top, or
        //   - the focus actually went to a dialog (MessageBox, file picker,
        //     etc.) owned by OUR process - minimizing in that case would
        //     orphan the modal dialog and the app would appear to hang.
        if (_isPinned || WindowState == WindowState.Minimized) return;

        // Defer the check: when Deactivated fires, the new foreground HWND
        // may not be fully set yet. Background priority lets the message
        // pump assign focus first, then we make the decision.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isPinned || WindowState == WindowState.Minimized) return;
            if (IsForegroundWindowOwnedByThisProcess()) return;
            WindowState = WindowState.Minimized;
        }), DispatcherPriority.Background);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static bool IsForegroundWindowOwnedByThisProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    // ============================================================
    // Sidebar is now fixed icon-only rail — no hover expand needed.

    // ============================================================
    // Nav
    // ============================================================

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageMobileSync == null) return;
        var idx = NavList.SelectedIndex;
        // Nav order: 0=Mobile Sync, 1=Voice Box, 2=Translator, 3=Settings, 4=About
        PageMobileSync.Visibility   = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageVoiceBox.Visibility     = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageTranslator.Visibility   = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility     = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility        = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

        if (idx == 2) EnsureTranslatorInitialised();
        if (idx == 1) EnsureVoiceBoxInitialised();
        if (idx == 4) EnsureAboutInitialised();
    }

    // ============================================================
    // Responsive layout
    // ============================================================
    //
    // The Window has MinWidth=430 / MinHeight=720 to guarantee a usable
    // compact (mobile-like) frame. Below the 650px width breakpoint we
    // restack a few elements so nothing clips or overlaps:
    //
    //   • Voice Box heading: language picker drops below the title and
    //     stretches full width.
    //
    // Add more compact tweaks here as the UI grows.

    private const double CompactBreakpoint = 650;

    private bool? _lastCompact;

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        ApplyResponsiveLayout(e.NewSize.Width < CompactBreakpoint);
    }

    private void ApplyResponsiveLayout(bool compact)
    {
        // Skip if we've already laid out for this mode — avoids the
        // (tiny) cost of touching layout properties on every drag.
        if (_lastCompact == compact) return;
        _lastCompact = compact;

        // ---- Voice Box: language picker repositioning ----
        if (VbLangSection != null)
        {
            if (compact)
            {
                // Stack: language picker on its own row, full width.
                Grid.SetRow(VbLangSection, 1);
                Grid.SetColumn(VbLangSection, 0);
                Grid.SetColumnSpan(VbLangSection, 2);
                VbLangSection.Margin = new Thickness(0, 12, 0, 0);
                VbLangSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            }
            else
            {
                // Side by side: language picker top-right of the heading.
                Grid.SetRow(VbLangSection, 0);
                Grid.SetColumn(VbLangSection, 1);
                Grid.SetColumnSpan(VbLangSection, 1);
                VbLangSection.Margin = new Thickness(0);
                VbLangSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            }
        }

        // ---- Translator: source / swap / target stacking ----
        ApplyTranslatorLangLayout(compact);

        // ---- About: feature card 2-col ⇄ 1-col + profile stacking ----
        ApplyAboutLayout(compact);

        // ---- Voice Box: bottom panel (API key | Smart Tip) stacks
        //      vertically at narrow widths so the cards keep comfortable
        //      minimum widths instead of shrinking into a cramped row.
        ApplyVoiceBoxBottomPanelLayout(compact);
    }

    // Flip VbBottomPanelGrid between a 2-col × 1-row band (wide) and a
    // 1-col × 2-row stack (compact). At compact widths the Smart-Tip
    // card also gets a top margin so it breathes away from the API key
    // card instead of sitting flush.
    private void ApplyVoiceBoxBottomPanelLayout(bool compact)
    {
        if (VbBottomPanelGrid == null
            || VbApiKeyCardBorder == null
            || VbSmartTipCardBorder == null)
            return;

        VbBottomPanelGrid.ColumnDefinitions.Clear();
        VbBottomPanelGrid.RowDefinitions.Clear();

        if (compact)
        {
            // Stack: single column, two rows.
            VbBottomPanelGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            VbBottomPanelGrid.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
            VbBottomPanelGrid.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(VbApiKeyCardBorder, 0);
            Grid.SetColumn(VbApiKeyCardBorder, 0);
            VbApiKeyCardBorder.Margin = new Thickness(0);

            Grid.SetRow(VbSmartTipCardBorder, 1);
            Grid.SetColumn(VbSmartTipCardBorder, 0);
            VbSmartTipCardBorder.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            // Side-by-side: two columns (3*/2*), single row.
            VbBottomPanelGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            VbBottomPanelGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            VbBottomPanelGrid.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(VbApiKeyCardBorder, 0);
            Grid.SetColumn(VbApiKeyCardBorder, 0);
            VbApiKeyCardBorder.Margin = new Thickness(0);

            Grid.SetRow(VbSmartTipCardBorder, 0);
            Grid.SetColumn(VbSmartTipCardBorder, 1);
            VbSmartTipCardBorder.Margin = new Thickness(12, 0, 0, 0);
        }
    }

    // Flip the About-page feature grid between 2 columns and 1 column,
    // and stack the profile avatar above its text block in compact mode.
    private void ApplyAboutLayout(bool compact)
    {
        // Feature grid -----------------------------------------------------
        if (AbtFeatureGrid != null
            && AbtFeatureCard1 != null && AbtFeatureCard2 != null
            && AbtFeatureCard3 != null && AbtFeatureCard4 != null)
        {
            AbtFeatureGrid.RowDefinitions.Clear();
            AbtFeatureGrid.ColumnDefinitions.Clear();

            if (compact)
            {
                // 1 column × 4 rows
                AbtFeatureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 0; i < 4; i++)
                    AbtFeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(AbtFeatureCard1, 0); Grid.SetColumn(AbtFeatureCard1, 0);
                Grid.SetRow(AbtFeatureCard2, 1); Grid.SetColumn(AbtFeatureCard2, 0);
                Grid.SetRow(AbtFeatureCard3, 2); Grid.SetColumn(AbtFeatureCard3, 0);
                Grid.SetRow(AbtFeatureCard4, 3); Grid.SetColumn(AbtFeatureCard4, 0);

                AbtFeatureCard1.Margin = new Thickness(0, 0, 0, 8);
                AbtFeatureCard2.Margin = new Thickness(0, 0, 0, 8);
                AbtFeatureCard3.Margin = new Thickness(0, 0, 0, 8);
                AbtFeatureCard4.Margin = new Thickness(0, 0, 0, 0);
            }
            else
            {
                // 2 columns × 2 rows (default)
                AbtFeatureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AbtFeatureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AbtFeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AbtFeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(AbtFeatureCard1, 0); Grid.SetColumn(AbtFeatureCard1, 0);
                Grid.SetRow(AbtFeatureCard2, 0); Grid.SetColumn(AbtFeatureCard2, 1);
                Grid.SetRow(AbtFeatureCard3, 1); Grid.SetColumn(AbtFeatureCard3, 0);
                Grid.SetRow(AbtFeatureCard4, 1); Grid.SetColumn(AbtFeatureCard4, 1);

                AbtFeatureCard1.Margin = new Thickness(0, 0, 8, 8);
                AbtFeatureCard2.Margin = new Thickness(8, 0, 0, 8);
                AbtFeatureCard3.Margin = new Thickness(0, 8, 8, 0);
                AbtFeatureCard4.Margin = new Thickness(8, 8, 0, 0);
            }
        }

        // Profile card: stack avatar above the text panel when compact ----
        if (AbtProfileGrid != null && AbtAvatarBox != null && AbtProfileTextPanel != null)
        {
            AbtProfileGrid.RowDefinitions.Clear();
            AbtProfileGrid.ColumnDefinitions.Clear();

            if (compact)
            {
                AbtProfileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AbtProfileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AbtProfileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(AbtAvatarBox, 0);
                Grid.SetColumn(AbtAvatarBox, 0);
                AbtAvatarBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;

                Grid.SetRow(AbtProfileTextPanel, 1);
                Grid.SetColumn(AbtProfileTextPanel, 0);
                AbtProfileTextPanel.Margin = new Thickness(0, 14, 0, 0);
            }
            else
            {
                AbtProfileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                AbtProfileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AbtProfileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(AbtAvatarBox, 0);
                Grid.SetColumn(AbtAvatarBox, 0);

                Grid.SetRow(AbtProfileTextPanel, 0);
                Grid.SetColumn(AbtProfileTextPanel, 1);
                AbtProfileTextPanel.Margin = new Thickness(18, 0, 0, 0);
            }
        }
    }

    // Flip TranslatorLangGrid between a 3-col horizontal layout and a
    // 3-row vertical stack. Done in code so the same XAML grid can host
    // both arrangements without duplicating elements.
    private void ApplyTranslatorLangLayout(bool compact)
    {
        if (TranslatorLangGrid == null) return;

        TranslatorLangGrid.RowDefinitions.Clear();
        TranslatorLangGrid.ColumnDefinitions.Clear();

        if (compact)
        {
            // Vertical stack: [source] / [swap] / [target]
            TranslatorLangGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TranslatorLangGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TranslatorLangGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TranslatorLangGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(TranslatorSourceCard, 0);
            Grid.SetColumn(TranslatorSourceCard, 0);

            Grid.SetRow(SwapLangsBtn, 1);
            Grid.SetColumn(SwapLangsBtn, 0);
            SwapLangsBtn.Margin = new Thickness(0, 8, 0, 8);
            SwapLangsBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            Grid.SetRow(TranslatorTargetCard, 2);
            Grid.SetColumn(TranslatorTargetCard, 0);
        }
        else
        {
            // Horizontal: [source] [swap] [target]
            TranslatorLangGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TranslatorLangGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TranslatorLangGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(TranslatorSourceCard, 0);
            Grid.SetColumn(TranslatorSourceCard, 0);

            Grid.SetRow(SwapLangsBtn, 0);
            Grid.SetColumn(SwapLangsBtn, 1);
            SwapLangsBtn.Margin = new Thickness(14, 0, 14, 0);
            SwapLangsBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            Grid.SetRow(TranslatorTargetCard, 0);
            Grid.SetColumn(TranslatorTargetCard, 2);
        }
    }

    // ============================================================
    // Pairing card collapse / expand
    // ============================================================

    private void PairingHeaderBtn_Click(object sender, RoutedEventArgs e)
    {
        _pairingExpanded = !_pairingExpanded;
        ApplyPairingExpansion(animate: true);
    }

    private void ApplyPairingExpansion(bool animate)
    {
        if (PairingDetail == null || PairChevron == null) return;

        PairingDetail.Visibility = _pairingExpanded ? Visibility.Visible : Visibility.Collapsed;
        PairChevron.Text = _pairingExpanded ? GlyphChevronUp : GlyphChevronDown;

        // Lightweight fade-in for the detail panel when expanding.
        if (animate && _pairingExpanded)
        {
            PairingDetail.Opacity = 0;
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PairingDetail.BeginAnimation(OpacityProperty, fade);
        }
    }

    // ============================================================
    // Startup / shutdown
    // ============================================================

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Start tracking which external window the user last had focus on,
        // so transcribed text can be pasted into THAT window and not into
        // our own app.
        ForegroundWindowTracker.Start();

        // Show the real assembly version in the Phase 3 status bar pill
        // instead of the hard-coded "v1.0". Uses GetAppVersion() from
        // MainWindow.About.cs (same partial class).
        if (StatusBarVersion != null)
            StatusBarVersion.Text = "v" + GetAppVersion();

        // System tray icon: keeps the app alive after the user closes the
        // window, and gives them quick mic toggle access.
        _tray = new TrayIconService(this);
        _tray.ToggleMicRequested += (_, _) =>
            Dispatcher.Invoke(() =>
            {
                EnsureVoiceBoxInitialised();
                ToggleMicFromHotkey();
            });
        _tray.SetTooltip("Voice Typing Desktop — click mic or press your hotkey");

        try
        {
            var cfg = App.Config;
            if (!cfg.HasSupabaseCredentials)
            {
                SetSyncStatus("Supabase credentials missing. Edit appsettings.json.", warning: true);
                StatusBarText.Text = "Config incomplete.";
                return;
            }

            _supabase = new SupabaseClient(cfg.SupabaseUrl, cfg.SupabaseAnonKey);
            _deviceService = new DeviceService(_supabase, cfg);

            SetSyncStatus("Registering...", warning: true);
            await _deviceService.RegisterAsync();

            DeviceIdText.Text = _deviceService.DeviceId;
            PairCodeText.Text = _deviceService.PairCode;

            try
            {
                QrImage.Source = QrCodeHelper.CreateQrBitmap(_deviceService.PairCode);
                QrPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception qex)
            {
                QrPlaceholder.Text = "QR error: " + qex.Message;
            }

            SetConnection(connected: true);

            _syncService = new MobileSyncService(_supabase, cfg, _deviceService.DeviceId);
            _syncService.SeedFromHistory(_receivedTexts);
            _syncService.StatusChanged += (_, msg) =>
                Dispatcher.Invoke(() => SetSyncStatus(msg,
                    warning: msg.StartsWith("Waiting") || msg.StartsWith("Sync error")));
            _syncService.SessionStarted += (_, sid) =>
                Dispatcher.Invoke(() =>
                {
                    StatusBarText.Text = $"Session: {Shorten(sid, 24)}";
                    SetSyncStatus($"Paired. Session: {Shorten(sid, 24)}", warning: false);
                });
            _syncService.TextReceived += (_, row) =>
                Dispatcher.Invoke(() =>
                {
                    var item = new ReceivedTextItem
                    {
                        Id = row.Id,
                        Text = row.Text,
                        CreatedAt = row.CreatedAt ?? DateTime.UtcNow
                    };
                    _receivedTexts.Add(item);
                    HistoryService.Save(_receivedTexts);
                    // Make sure the latest item is visible in the list.
                    try { ReceivedTextsList.ScrollIntoView(item); }
                    catch { /* benign during layout */ }
                });
            _syncService.Start();

            // Live diagnostic: refresh "Polled X times • last text Ys ago" once a second.
            _diagTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _diagTimer.Tick += (_, _) => RefreshSyncDiagnostics();
            _diagTimer.Start();
            RefreshSyncDiagnostics();

            StatusBarText.Text = "Ready.";
        }
        catch (Exception ex)
        {
            SetSyncStatus($"Startup error: {ex.Message}", warning: true);
            StatusBarText.Text = "Error during startup.";
            SetConnection(connected: false);
        }
    }

    private void RefreshSyncDiagnostics()
    {
        if (SyncDiagText == null || _syncService == null) return;

        var session = _syncService.CurrentSessionId;
        var polls   = _syncService.PollCount;
        var total   = _syncService.TextsReceivedTotal;
        var last    = _syncService.LastTextAtUtc;
        var error   = _syncService.LastError;

        var parts = new System.Collections.Generic.List<string>();
        var sessions = _syncService.ActiveSessionIds.Count;
        parts.Add(sessions switch
        {
            0 => "Not paired",
            1 => "Paired (1 mobile)",
            _ => $"Paired ({sessions} mobiles)"
        });
        parts.Add($"{polls} poll{(polls == 1 ? "" : "s")}");
        parts.Add($"{total} text{(total == 1 ? "" : "s")} received");
        if (last.HasValue)
        {
            var ago = DateTime.UtcNow - last.Value;
            parts.Add(ago.TotalSeconds < 60
                ? $"last {Math.Max(1, (int)ago.TotalSeconds)}s ago"
                : ago.TotalMinutes < 60
                    ? $"last {(int)ago.TotalMinutes}m ago"
                    : $"last {(int)ago.TotalHours}h ago");
        }

        // Show how long the current pairing is valid for — gives the user
        // confidence that they won't need to re-scan for the next 24h.
        var expiry = App.Config.PairedUntilUtcParsed;
        if (expiry.HasValue)
        {
            var left = expiry.Value - DateTime.UtcNow;
            if (left > TimeSpan.Zero)
            {
                var human = left.TotalHours >= 1
                    ? $"{(int)left.TotalHours}h {left.Minutes}m"
                    : $"{Math.Max(1, (int)left.TotalMinutes)}m";
                parts.Add($"expires in {human}");
            }
            else
            {
                parts.Add("expired");
            }
        }

        var line = string.Join("  •  ", parts);
        if (!string.IsNullOrEmpty(error)) line += $"\nLast error: {error}";
        SyncDiagText.Text = line;
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceService == null || _syncService == null) return;

        try
        {
            RepairButton.IsEnabled = false;

            // 1) Reset the in-memory + persisted session so we stop polling
            //    a stale session id.
            _syncService.ResetSession();

            // 2) Mint a fresh pair code, write it to Supabase, update UI.
            await _deviceService.ForceNewPairCodeAsync();

            PairCodeText.Text = _deviceService.PairCode;
            try
            {
                QrImage.Source = QrCodeHelper.CreateQrBitmap(_deviceService.PairCode);
                QrPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception qex)
            {
                QrPlaceholder.Text = "QR error: " + qex.Message;
            }

            SetSyncStatus("New pair code ready — scan it from your phone.", warning: true);
            StatusBarText.Text = "Re-pair requested. Scan the new code.";
            RefreshSyncDiagnostics();
        }
        catch (Exception ex)
        {
            SetSyncStatus($"Re-pair failed: {ex.Message}", warning: true);
        }
        finally
        {
            RepairButton.IsEnabled = true;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // First close press → hide to tray instead of exiting, so the user
        // can still trigger the mic via global hotkey / tray menu.
        if (!_reallyClose && _tray != null)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Actual exit — tidy up all services.
        try { _diagTimer?.Stop(); }                  catch { /* ignore */ }
        try { HistoryService.Save(_receivedTexts); } catch { /* ignore */ }
        try { CleanupVoiceBox(); }                   catch { /* ignore */ }
        try { _ = _syncService?.StopAsync(); }       catch { /* ignore */ }
        try { _supabase?.Dispose(); }                catch { /* ignore */ }
        try { _tray?.Dispose(); }                    catch { /* ignore */ }
        try { ForegroundWindowTracker.Stop(); }      catch { /* ignore */ }
    }

    // ============================================================
    // Mobile Sync actions
    // ============================================================

    private void ItemCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string t && !string.IsNullOrEmpty(t))
        {
            Clipboard.SetText(t);
            StatusBarText.Text = "Copied item to clipboard.";
        }
    }

    /// <summary>
    /// Sends the received mobile text straight into whichever external
    /// window the user had focused last. Uses the same clipboard + Ctrl+V
    /// flow as the Voice Box dictation so the behaviour is identical:
    /// the message lands in Notepad, Word, the browser, WhatsApp — wherever
    /// the cursor was before the user tapped "Paste".
    /// </summary>
    private void ItemPasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string t || string.IsNullOrWhiteSpace(t))
            return;

        try
        {
            ClipboardPasteHelper.CopyAndPaste(t);
            StatusBarText.Text = $"Pasted to active app: \"{Shorten(t, 40)}\"";
        }
        catch (Exception ex)
        {
            StatusBarText.Text = "Paste failed: " + ex.Message;
        }
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_receivedTexts.Count == 0)
        {
            StatusBarText.Text = "List is empty.";
            return;
        }
        var sb = new StringBuilder();
        foreach (var it in _receivedTexts) sb.AppendLine(it.Text);
        Clipboard.SetText(sb.ToString().TrimEnd());
        StatusBarText.Text = $"Copied {_receivedTexts.Count} items.";
    }

    private void ClearListButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Clear all local history?\nThis does not delete data from Supabase.",
            "Clear History",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        _receivedTexts.Clear();
        HistoryService.Save(_receivedTexts);
        StatusBarText.Text = "History cleared.";
    }

    // ============================================================
    // Helpers
    // ============================================================

    private void ReceivedTexts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateEmptyState();

    private void UpdateEmptyState()
    {
        if (EmptyState == null) return;
        var empty = _receivedTexts.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ReceivedCountText.Text = $"{_receivedTexts.Count} item{(_receivedTexts.Count == 1 ? "" : "s")}";
    }

    private void SetSyncStatus(string text, bool warning)
    {
        MobileSyncStatusText.Text = text;
        SyncDot.Fill = warning
            ? (Brush)FindResource("Warning")
            : (Brush)FindResource("Success");
    }

    private void SetConnection(bool connected)
    {
        var brush = connected
            ? (Brush)FindResource("Success")
            : (Brush)FindResource("Danger");

        if (ConnDotCollapsed != null) ConnDotCollapsed.Fill = brush;
    }

    // ============================================================
    // Social links
    // ============================================================

    private void TelegramBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://t.me/kinetimart");
    }

    /// <summary>
    /// Store URL for the companion Android app. Wired to the
    /// "Get it on Google Play" button in Mobile Sync. Points at the
    /// KinetiMart developer page, which lists every published app —
    /// once the VoiceTyping mobile app has a public package id we can
    /// swap this for the deep link <c>?id=com.kinetimart.voicetyping</c>.
    /// </summary>
    private const string PlayStoreUrl =
        "https://play.google.com/store/apps/developer?id=KinetiMart";

    private void OpenPlayStore_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(PlayStoreUrl);
    }

    /// <summary>
    /// Direct APK download link. Users who can't access the Play Store
    /// (Huawei, China, sideload preference) can grab the APK from here.
    /// Replace with your actual hosted APK URL when ready.
    /// </summary>
    private const string ApkDownloadUrl =
        "https://github.com/zenuragroupltd-netizen/VoiceTypingDesktop/releases";

    private void DownloadApk_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(ApkDownloadUrl);
    }

    private void WhatsAppBtn_Click(object sender, RoutedEventArgs e)
    {
        // wa.me link works with both desktop app and browser.
        // Bangladesh number: +880 1602366157
        OpenUrl("https://wa.me/8801602366157");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* ignore if no browser/app available */ }
    }

    private static string Shorten(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
}
