using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using VoiceTypingDesktop.Services;
using VoiceTypingDesktop.Services.Recording;

namespace VoiceTypingDesktop;

/// <summary>
/// Desktop Voice Box — mic-centric dictation UI.
///
/// • One big microphone button in the middle.
/// • Click to start listening; click again to stop.
/// • Pulse rings animate while the mic is on.
/// • Below: language dropdown + the user's own OpenAI API key input.
/// • No buttons for copy/clear/translate — transcribed text goes directly
///   into whichever app has the cursor focus.
/// </summary>
public partial class MainWindow
{
    private ContinuousDictationService? _dictation;
    private bool _vbInitialised;
    private Storyboard? _pulseStoryboard;
    private GlobalHotkeyService? _hotkey;
    private HotkeyBinding? _pendingHotkey; // set while the hotkey box is focused

    // --- Language list shown in the dropdown ---
    //
    // The Code is a BCP-47-style "lang[-REGION]" string we persist to
    // settings; WhisperApiEngine strips the region suffix because the
    // OpenAI transcription API only cares about the language part.
    // Keeping the region lets us remember a user's specific regional
    // preference (e.g. en-IN vs en-US) across launches.
    //
    // CountryCode is a lowercase ISO-3166 alpha-2 used to look up the
    // matching PNG flag in Assets/Flags/. FlagSource is built lazily
    // once per option; if the asset is missing, FlagSource stays null
    // and the dropdown DataTemplate falls back to a 2-letter pill
    // badge instead of broken emoji glyphs.
    private sealed class LangOption
    {
        public string Code { get; }
        public string Label { get; }
        public string CountryCode { get; }
        public ImageSource? FlagSource { get; }
        public bool HasFlag => FlagSource != null;

        public LangOption(string code, string label, string countryCode)
        {
            Code = code;
            Label = label;
            CountryCode = countryCode.ToUpperInvariant();
            FlagSource = TryLoadFlag(countryCode);
        }
    }

    private static readonly IReadOnlyList<LangOption> Languages = new[]
    {
        new LangOption("auto",  "Auto-detect", ""),

        // South Asian — top of the list since this app primarily targets
        // Bangladeshi/Indian users.
        new LangOption("bn-BD", "Bengali (BD)",   "bd"),
        new LangOption("bn-IN", "Bengali (IN)",   "in"),
        new LangOption("hi-IN", "Hindi (IN)",     "in"),
        new LangOption("ur-PK", "Urdu (PK)",      "pk"),
        new LangOption("ur-IN", "Urdu (IN)",      "in"),
        new LangOption("pa-IN", "Punjabi (IN)",   "in"),
        new LangOption("gu-IN", "Gujarati (IN)",  "in"),
        new LangOption("mr-IN", "Marathi (IN)",   "in"),
        new LangOption("ta-IN", "Tamil (IN)",     "in"),
        new LangOption("te-IN", "Telugu (IN)",    "in"),
        new LangOption("kn-IN", "Kannada (IN)",   "in"),
        new LangOption("ml-IN", "Malayalam (IN)", "in"),
        new LangOption("ne-NP", "Nepali (NP)",    "np"),
        new LangOption("si-LK", "Sinhala (LK)",   "lk"),

        // English variants.
        new LangOption("en-US", "English (US)", "us"),
        new LangOption("en-GB", "English (UK)", "gb"),
        new LangOption("en-IN", "English (IN)", "in"),
        new LangOption("en-CA", "English (CA)", "ca"),
        new LangOption("en-AU", "English (AU)", "au"),

        // Western European.
        new LangOption("es-ES", "Español (ES)",     "es"),
        new LangOption("es-MX", "Español (MX)",     "mx"),
        new LangOption("pt-BR", "Português (BR)",   "br"),
        new LangOption("pt-PT", "Português (PT)",   "pt"),
        new LangOption("fr-FR", "Français (FR)",    "fr"),
        new LangOption("fr-CA", "Français (CA)",    "ca"),
        new LangOption("de-DE", "Deutsch (DE)",     "de"),
        new LangOption("it-IT", "Italiano (IT)",    "it"),
        new LangOption("nl-NL", "Nederlands (NL)",  "nl"),
        new LangOption("ca-ES", "Català (ES)",      "es"),

        // Nordic.
        new LangOption("sv-SE", "Svenska (SE)",   "se"),
        new LangOption("da-DK", "Dansk (DK)",     "dk"),
        new LangOption("nb-NO", "Norsk (NO)",     "no"),
        new LangOption("fi-FI", "Suomi (FI)",     "fi"),
        new LangOption("is-IS", "Íslenska (IS)",  "is"),

        // Central/Eastern European.
        new LangOption("ru-RU", "Русский (RU)",       "ru"),
        new LangOption("uk-UA", "Українська (UA)",    "ua"),
        new LangOption("pl-PL", "Polski (PL)",        "pl"),
        new LangOption("cs-CZ", "Čeština (CZ)",       "cz"),
        new LangOption("sk-SK", "Slovenčina (SK)",    "sk"),
        new LangOption("hu-HU", "Magyar (HU)",        "hu"),
        new LangOption("ro-RO", "Română (RO)",        "ro"),
        new LangOption("hr-HR", "Hrvatski (HR)",      "hr"),
        new LangOption("sr-RS", "Српски (RS)",        "rs"),
        new LangOption("bg-BG", "Български (BG)",     "bg"),
        new LangOption("sl-SI", "Slovenščina (SI)",   "si"),
        new LangOption("mk-MK", "Македонски (MK)",    "mk"),
        new LangOption("el-GR", "Ελληνικά (GR)",      "gr"),

        // Middle Eastern.
        new LangOption("ar-SA", "Arabic (SA)",  "sa"),
        new LangOption("ar-AE", "Arabic (AE)",  "ae"),
        new LangOption("ar-EG", "Arabic (EG)",  "eg"),
        new LangOption("he-IL", "עברית (IL)",   "il"),
        new LangOption("fa-IR", "فارسی (IR)",   "ir"),
        new LangOption("tr-TR", "Türkçe (TR)",  "tr"),

        // East / Southeast Asian.
        new LangOption("zh-CN", "Chinese (CN)",        "cn"),
        new LangOption("zh-TW", "Chinese (TW)",        "tw"),
        new LangOption("ja-JP", "Japanese (JP)",       "jp"),
        new LangOption("ko-KR", "Korean (KR)",         "kr"),
        new LangOption("th-TH", "Thai (TH)",           "th"),
        new LangOption("vi-VN", "Tiếng Việt (VN)",    "vn"),
        new LangOption("id-ID", "Bahasa Indonesia",    "id"),
        new LangOption("ms-MY", "Bahasa Melayu (MY)",  "my"),
        new LangOption("tl-PH", "Filipino (PH)",       "ph"),

        // African.
        new LangOption("sw-KE", "Kiswahili (KE)",  "ke"),
        new LangOption("af-ZA", "Afrikaans (ZA)",  "za"),

        // Other (no native ISO-3166 country, falls back to GB).
        new LangOption("cy-GB", "Cymraeg (GB)", "gb"),
    };

    /// <summary>
    /// Resolves a 2-letter country code to its embedded PNG flag.
    /// Returns <c>null</c> if the asset is missing — the dropdown
    /// template then renders a clean fallback badge instead of a
    /// broken-image icon. We freeze the BitmapImage so it's safe to
    /// share across UI threads and reuse for every row.
    /// </summary>
    private static ImageSource? TryLoadFlag(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/Assets/Flags/{countryCode.ToLowerInvariant()}.png",
                UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // missing or unreadable asset — fall back to badge
        }
    }

    /// <summary>
    /// Finds the dropdown entry that best matches a saved language code.
    /// Falls back gracefully:
    ///   1) exact match on <c>Code</c> (e.g. "bn-BD" → bn-BD entry),
    ///   2) prefix match on the base language (e.g. legacy "bn" → first
    ///      entry whose Code starts with "bn-"),
    ///   3) the "auto" entry at index 0.
    /// </summary>
    private static LangOption ResolveSavedLanguage(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return Languages[0];

        var key = saved.Trim();

        var exact = Languages.FirstOrDefault(l =>
            string.Equals(l.Code, key, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var dash = key.IndexOf('-');
        var baseLang = dash >= 0 ? key.Substring(0, dash) : key;
        var byBase = Languages.FirstOrDefault(l =>
            l.Code.StartsWith(baseLang + "-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Code, baseLang, StringComparison.OrdinalIgnoreCase));

        return byBase ?? Languages[0];
    }

    // -----------------------------------------------------------------
    // Init
    // -----------------------------------------------------------------
    private void EnsureVoiceBoxInitialised()
    {
        if (_vbInitialised) return;
        _vbInitialised = true;

        // Language dropdown.
        //
        // We deliberately DO NOT set DisplayMemberPath here. The custom
        // ComboBox template in Themes/Controls.xaml supplies an explicit
        // ItemTemplate (VoiceLangItemTemplate) that binds to the Label
        // property, which is used for BOTH dropdown rows and the
        // selected-pill display. Setting DisplayMemberPath alongside a
        // custom template causes WPF to fall back to record.ToString()
        // and leak strings like "LangOption { Code=…, Label=… }".
        VbLangCombo.ItemsSource = Languages;
        VbLangCombo.SelectedItem = ResolveSavedLanguage(App.Config.WhisperLanguage);

        // API key box: pre-fill if saved, but masked.
        // If a previous build accidentally persisted the masked placeholder
        // as the key (legacy bug — see VbSaveKey_Click), purge it so the
        // user starts from a clean empty box and can paste a fresh key.
        if (App.Config.WhisperApiKey.Contains('*'))
        {
            App.Config.WhisperApiKey = "";
            App.Config.Save();
        }

        if (!string.IsNullOrEmpty(App.Config.WhisperApiKey))
        {
            VbApiKeyBox.Text = MaskKey(App.Config.WhisperApiKey);
            UpdateKeyStatus(valid: App.Config.HasWhisperApiKey);
        }
        else
        {
            VbApiKeyBox.Text = "";
            UpdateKeyStatus(valid: false);
        }

        // Build pulse animation storyboard (triggered while mic is on).
        _pulseStoryboard = BuildPulseStoryboard();

        // Attach the global hotkey service.
        _hotkey = new GlobalHotkeyService();
        _hotkey.Attach(this);
        _hotkey.HotkeyPressed += (_, _) => Dispatcher.Invoke(ToggleMicFromHotkey);

        // Load saved hotkey.
        var saved = HotkeyBinding.Deserialize(App.Config.MicHotkey);
        ApplyHotkey(saved);

        UpdateMicVisualState(on: false);
        UpdateStatusMessages();
        RefreshUsageText();
        RotateSmartTip();
    }

    // -----------------------------------------------------------------
    // "How it works" pill — shows a short workflow explainer.
    // -----------------------------------------------------------------
    private void VbHowItWorks_Click(object sender, RoutedEventArgs e)
    {
        const string body =
            "1.  Add your OpenAI API key below (it's stored locally).\n" +
            "2.  Click the mic, then click into any app where you want text to appear (e.g. Word, browser, chat).\n" +
            "3.  Speak naturally — short pauses end each chunk and trigger transcription.\n" +
            "4.  Recognised text is typed into the active window via simulated keystrokes (no clipboard required).\n\n" +
            "Tips:\n" +
            "•  Pick the spoken language above for more accurate transcription.\n" +
            "•  Click the mic again to stop. Session usage and cost appear under the mic.";

        MessageBox.Show(this, body, "How Voice Typing works",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // -----------------------------------------------------------------
    // Mic toggle
    // -----------------------------------------------------------------
    private void VbMic_Click(object sender, RoutedEventArgs e)
    {
        EnsureVoiceBoxInitialised();

        if (_dictation != null && _dictation.IsListening)
        {
            StopMic();
            return;
        }

        // Require API key before turning on.
        if (!App.Config.HasWhisperApiKey)
        {
            VbStatusText.Text = "Add your OpenAI API key to enable the mic";
            VbStatusText.Foreground = (Brush)FindResource("Warning");
            return;
        }

        StartMic();
    }

    // Session usage accumulators (reset on each app launch, not persisted).
    private double _sessionAudioSec;
    private string _sessionModelForPricing = WhisperApiEngine.DefaultModel;

    private void StartMic()
    {
        try
        {
            var engine = new WhisperApiEngine(
                App.Config.WhisperApiKey,
                App.Config.WhisperLanguage);

            _sessionModelForPricing = engine.Model;
            RefreshUsageText();

            _dictation = new ContinuousDictationService(engine);
            _dictation.TextReady += (_, text) =>
                Dispatcher.Invoke(() => OnDictationTextReady(text));
            _dictation.StateChanged += (_, state) =>
                Dispatcher.Invoke(() => OnDictationStateChanged(state));
            _dictation.ErrorOccurred += (_, err) =>
                Dispatcher.Invoke(() =>
                {
                    VbSubStatusText.Text = "Whisper error — " + err;
                    StatusBarText.Text = "Voice error: " + err;
                });
            _dictation.ChunkTranscribed += (_, durationSec) =>
                Dispatcher.Invoke(() =>
                {
                    _sessionAudioSec += durationSec;
                    RefreshUsageText();
                });
            _dictation.ChunkSending += (_, durationSec) =>
                Dispatcher.Invoke(() =>
                {
                    // Tells the user "yes, your speech was captured —
                    // we're sending it to Whisper now". Critical for
                    // diagnosing the silent-failure pattern where chunks
                    // would never make it past the dictation service.
                    VbSubStatusText.Text =
                        $"Sending {durationSec:0.0}s of audio to Whisper…";
                });
            _dictation.EmptyResult += (_, reason) =>
                Dispatcher.Invoke(() =>
                {
                    // Whisper accepted the audio but returned nothing.
                    // Most common causes: mic gain too low, speaking off-
                    // axis, or noise without speech. Surface the reason
                    // so the user can adjust instead of staring at a
                    // frozen "Listening…" label forever.
                    VbSubStatusText.Text = "Whisper returned: " + reason +
                                           " — speak louder or closer to the mic.";
                });
            _dictation.PeakLevelChanged += (_, peak) =>
                Dispatcher.Invoke(() => UpdatePeakMeter(peak));
            _dictation.AutoStopped += (_, _) =>
                Dispatcher.Invoke(() =>
                {
                    // Auto-off after 8s silence: tidy up UI state.
                    _dictation?.Dispose();
                    _dictation = null;
                    UpdateMicVisualState(on: false);
                    HidePeakMeter();
                    VbStatusText.Text = "Mic auto-stopped (8s silence)";
                    VbStatusText.Foreground = (Brush)FindResource("TextSecondary");
                    UpdateStatusMessages();
                    StatusBarText.Text = "Mic auto-stopped after 8s silence.";
                    _tray?.SetMicState(on: false);
                    _tray?.SetTooltip("Voice Typing Desktop");
                });

            _dictation.Start();
            UpdateMicVisualState(on: true);
            ShowPeakMeter();
            VbStatusText.Text = "Listening…";
            VbStatusText.Foreground = (Brush)FindResource("Danger");
            VbSubStatusText.Text = "Speak now. Text goes to your active app.";
            StatusBarText.Text = "Mic on. Speak and text appears where your cursor is.";
            _tray?.SetMicState(on: true);
            _tray?.SetTooltip("Voice Typing Desktop — Mic ON");
        }
        catch (Exception ex)
        {
            VbStatusText.Text = "Mic error";
            VbStatusText.Foreground = (Brush)FindResource("Warning");
            VbSubStatusText.Text = ex.Message;
        }
    }

    /// <summary>
    /// Updates the small session-usage line under the mic: audio seconds
    /// consumed this session and the current OpenAI cost estimate. Kept
    /// in memory only — resets on each app launch.
    /// </summary>
    private void RefreshUsageText()
    {
        if (VbUsageText == null) return;

        if (_sessionAudioSec <= 0)
        {
            VbUsageText.Text = $"Model: {_sessionModelForPricing}  ·  Session: 0s  ·  $0.0000";
            return;
        }

        var usdPerMin = WhisperApiEngine.GetUsdPerMinute(_sessionModelForPricing);
        var cost = usdPerMin * (decimal)(_sessionAudioSec / 60.0);

        var minutes = (int)(_sessionAudioSec / 60);
        var seconds = (int)(_sessionAudioSec % 60);
        var durationLabel = minutes > 0 ? $"{minutes}m {seconds:D2}s" : $"{seconds}s";

        VbUsageText.Text =
            $"Model: {_sessionModelForPricing}  ·  Session: {durationLabel}  ·  ${cost:0.0000}";
    }

    private void StopMic()
    {
        _dictation?.Stop();
        _dictation?.Dispose();
        _dictation = null;

        UpdateMicVisualState(on: false);
        HidePeakMeter();
        VbStatusText.Text = "Tap the mic to start";
        VbStatusText.Foreground = (Brush)FindResource("TextSecondary");
        UpdateStatusMessages();
        StatusBarText.Text = "Mic off.";
        _tray?.SetMicState(on: false);
        _tray?.SetTooltip("Voice Typing Desktop");
    }

    private void OnDictationTextReady(string text)
    {
        try
        {
            ClipboardPasteHelper.CopyAndPaste(text);
            var diag = ClipboardPasteHelper.LastDiagnostic;
            // If the typing step couldn't reach the target window, surface
            // the helper's diagnostic instead of pretending we typed it.
            if (diag.Contains("no target hwnd") || diag.Contains("focus restore failed"))
            {
                StatusBarText.Text = $"Couldn't type — {diag}";
            }
            else
            {
                // Privacy: show only character count, not the text itself.
                // The text is already going into the user's active app —
                // mirroring it into our status bar just clutters the UI
                // and leaks content they may not want visible here.
                StatusBarText.Text = $"Typed {text.Length} chars  ·  {diag}";
            }
        }
        catch (Exception ex)
        {
            StatusBarText.Text = "Paste failed: " + ex.Message;
        }
    }

    private void OnDictationStateChanged(string state)
    {
        if (state == "Processing...")
        {
            VbSubStatusText.Text = "Transcribing…";
        }
        else if (state == "Listening")
        {
            VbSubStatusText.Text = "Speak now. Text goes to your active app.";
        }
    }

    // -----------------------------------------------------------------
    // Language + API key
    // -----------------------------------------------------------------
    private void VbLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_vbInitialised) return;
        if (VbLangCombo.SelectedItem is not LangOption opt) return;
        App.Config.WhisperLanguage = opt.Code;
        App.Config.Save();
    }

    private void VbSaveKey_Click(object sender, RoutedEventArgs e)
    {
        var raw = (VbApiKeyBox.Text ?? "").Trim();

        // Check for the masked placeholder FIRST. The mask itself starts with
        // "sk-" and is long enough to slip past the format check below, so if
        // we checked StartsWith("sk-") first, the user could accidentally save
        // the masked string back as the real key — which would then be sent to
        // OpenAI and rejected with 401 "Invalid API key".
        if (raw.Contains('*'))
        {
            VbStatusText.Text = "Paste a new key to replace the saved one";
            VbStatusText.Foreground = (Brush)FindResource("Warning");
            return;
        }

        if (string.IsNullOrEmpty(raw))
        {
            // Clearing the key.
            App.Config.WhisperApiKey = "";
            App.Config.Save();
            UpdateKeyStatus(valid: false);
            VbStatusText.Text = "Key cleared";
            VbStatusText.Foreground = (Brush)FindResource("TextSecondary");
            UpdateStatusMessages();
            return;
        }

        // Real key: must start with sk- and be reasonably long. We don't
        // validate further here — the first transcription request will surface
        // any account/billing errors with a clear status-bar message.
        if (raw.StartsWith("sk-") && raw.Length > 20)
        {
            App.Config.WhisperApiKey = raw;
            App.Config.Save();
            VbApiKeyBox.Text = MaskKey(raw);
            UpdateKeyStatus(valid: true);
            VbStatusText.Text = "API key saved";
            VbStatusText.Foreground = (Brush)FindResource("Success");
            UpdateStatusMessages();
            StatusBarText.Text = "OpenAI key saved to appsettings.json.";
        }
        else
        {
            VbStatusText.Text = "Invalid key (must start with sk-)";
            VbStatusText.Foreground = (Brush)FindResource("Warning");
        }
    }

    private void UpdateKeyStatus(bool valid)
    {
        VbKeyStatusText.Text = valid ? "Saved" : "Not set";
        VbKeyStatusText.Foreground = valid
            ? (Brush)FindResource("Success")
            : (Brush)FindResource("Warning");
    }

    private void UpdateStatusMessages()
    {
        if (!App.Config.HasWhisperApiKey)
        {
            VbSubStatusText.Text = "Set your OpenAI API key below to enable Whisper";
        }
        else if (_dictation?.IsListening != true)
        {
            VbSubStatusText.Text = "Ready. Pin the window and click the mic.";
        }
    }

    // -----------------------------------------------------------------
    // Peak audio meter (visual diagnostic — shows the user that the
    // microphone is actually capturing sound while the mic is on).
    //
    // The hero card has two Borders for this:
    //   VbPeakMeterTrack — the 220×5 px capsule background.
    //   VbPeakMeterFill  — the inner purple-gradient fill that grows
    //                      with the most recent peak level (0..1).
    //
    // While the wave bars are a stylised equaliser (random keyframes),
    // the peak meter is the LITERAL signal — if it stays flat-zero
    // while the user speaks, the OS-level audio path is broken (wrong
    // default mic, muted, no permission, etc.) and no amount of UI
    // polish will fix it. Keeping this meter wired is what makes the
    // app debuggable for end users.
    // -----------------------------------------------------------------

    /// <summary>Track width in DIPs — kept in sync with the XAML so
    /// width-mapping math doesn't drift if the meter is later resized.</summary>
    private const double PeakMeterWidth = 220;

    /// <summary>
    /// Smoothed peak level (0..1) — exponential decay so the bar doesn't
    /// flicker between every audio buffer. Higher peaks update instantly
    /// (attack=1.0); lower peaks fade out over ~150 ms (release=0.85).
    /// </summary>
    private double _smoothedPeak;

    private void ShowPeakMeter()
    {
        if (VbPeakMeterTrack != null)
            VbPeakMeterTrack.Visibility = Visibility.Visible;
        _smoothedPeak = 0;
        if (VbPeakMeterFill != null)
            VbPeakMeterFill.Width = 0;
    }

    private void HidePeakMeter()
    {
        if (VbPeakMeterTrack != null)
            VbPeakMeterTrack.Visibility = Visibility.Collapsed;
        if (VbPeakMeterFill != null)
            VbPeakMeterFill.Width = 0;
        _smoothedPeak = 0;
    }

    private void UpdatePeakMeter(float peak)
    {
        if (VbPeakMeterFill == null) return;

        // Clamp + smooth. Most consumer mics live in the 0.05–0.4 range
        // for normal speech, so we square-root the input to push quiet
        // speech further up the bar (perceptual loudness curve, like
        // a VU meter) without saturating on loud speech.
        var clamped = Math.Max(0, Math.Min(1, peak));
        var perceived = Math.Sqrt(clamped);

        if (perceived > _smoothedPeak)
            _smoothedPeak = perceived;
        else
            _smoothedPeak = _smoothedPeak * 0.85 + perceived * 0.15;

        VbPeakMeterFill.Width = _smoothedPeak * PeakMeterWidth;
    }

    // -----------------------------------------------------------------
    // Mic visual state (pulse animation + glow)
    // -----------------------------------------------------------------
    private void UpdateMicVisualState(bool on)
    {
        // Show/hide wave bars
        if (VbWaveBars != null)
            VbWaveBars.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        // Phase 3 — reflect mic state in the bottom status bar pill so
        // the user sees "Listening" even on other pages (pills are
        // visible shell-wide).
        UpdateStatusBarMicPill(on);

        if (_pulseStoryboard == null) return;

        if (on)
        {
            _pulseStoryboard.Begin(this, isControllable: true);
            StartWaveAnimation();
        }
        else
        {
            try { _pulseStoryboard.Stop(this); } catch { /* ignore */ }
            VbPulseRing1.Opacity = 0;
            VbPulseRing2.Opacity = 0;
            StopWaveAnimation();
        }
    }

    // Sync the status-bar mic pill with the dictation state.
    // Red dot + "Mic: Listening" while recording, muted otherwise.
    private void UpdateStatusBarMicPill(bool on)
    {
        if (StatusBarMicDot == null || StatusBarMicText == null) return;

        if (on)
        {
            StatusBarMicDot.Fill = (Brush)FindResource("Danger");
            StatusBarMicText.Text = "Mic: Listening";
            StatusBarMicText.Foreground = (Brush)FindResource("Danger");
        }
        else
        {
            StatusBarMicDot.Fill = (Brush)FindResource("TextMuted");
            StatusBarMicText.Text = "Mic: Idle";
            StatusBarMicText.Foreground = (Brush)FindResource("TextSecondary");
        }
    }

    // -----------------------------------------------------------------
    // Wave bar animation (equalizer-style bouncing bars)
    // -----------------------------------------------------------------
    private Storyboard? _waveStoryboard;

    private void StartWaveAnimation()
    {
        if (_waveStoryboard != null) { try { _waveStoryboard.Stop(this); } catch { } }

        _waveStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        var bars = new[] { WaveBar1, WaveBar2, WaveBar3, WaveBar4, WaveBar5, WaveBar6, WaveBar7 };
        var delays = new[] { 0, 120, 240, 80, 200, 320, 160 };
        var rng = new Random();

        for (int i = 0; i < bars.Length; i++)
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(delays[i])
            };
            var low = 0.2 + rng.NextDouble() * 0.2;
            var high = 0.7 + rng.NextDouble() * 0.3;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(low, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(high, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(low, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));

            Storyboard.SetTarget(anim, bars[i]);
            Storyboard.SetTargetProperty(anim, new PropertyPath(ScaleTransform.ScaleYProperty));
            _waveStoryboard.Children.Add(anim);
        }

        _waveStoryboard.Begin(this, isControllable: true);
    }

    private void StopWaveAnimation()
    {
        try { _waveStoryboard?.Stop(this); } catch { /* ignore */ }
        _waveStoryboard = null;
    }

    /// <summary>
    /// Two concentric rings that fade in + scale up on loop to give a
    /// "live mic" feel.
    /// </summary>
    private Storyboard BuildPulseStoryboard()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        AddRingAnim(sb, VbPulseRing1, VbPulseRing1Scale, delayMs: 0);
        AddRingAnim(sb, VbPulseRing2, VbPulseRing2Scale, delayMs: 600);

        return sb;
    }

    private static void AddRingAnim(Storyboard sb, FrameworkElement ring, ScaleTransform scale, int delayMs)
    {
        var dur = TimeSpan.FromMilliseconds(1600);
        var begin = TimeSpan.FromMilliseconds(delayMs);

        // Opacity 0 → 0.6 → 0
        var op = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(dur)));
        Storyboard.SetTarget(op, ring);
        Storyboard.SetTargetProperty(op, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(op);

        // ScaleX/Y 0.75 → 1.15
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var s = new DoubleAnimation
            {
                From = 0.75,
                To = 1.15,
                Duration = dur,
                BeginTime = begin
            };
            Storyboard.SetTarget(s, scale);
            Storyboard.SetTargetProperty(s, new PropertyPath(prop));
            sb.Children.Add(s);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------
    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 10) return key;
        return key.Substring(0, 5) + new string('*', Math.Max(4, key.Length - 10)) + key.Substring(key.Length - 4);
    }

    private void CleanupVoiceBox()
    {
        try { _dictation?.Stop(); _dictation?.Dispose(); } catch { /* ignore */ }
        try { _pulseStoryboard?.Stop(this); } catch { /* ignore */ }
        try { _hotkey?.Dispose(); } catch { /* ignore */ }
    }

    // -----------------------------------------------------------------
    // Global hotkey (click-to-capture UI)
    // -----------------------------------------------------------------
    private void ToggleMicFromHotkey()
    {
        EnsureVoiceBoxInitialised();
        if (_dictation != null && _dictation.IsListening)
        {
            StopMic();
        }
        else if (App.Config.HasWhisperApiKey)
        {
            // Must be on the Voice Box page? No — hotkey works from anywhere.
            StartMic();
        }
        else
        {
            StatusBarText.Text = "Set your OpenAI API key first.";
        }
    }

    private void VbHotkey_GotFocus(object sender, RoutedEventArgs e)
    {
        VbHotkeyBox.Text = "Press your shortcut…";
        _pendingHotkey = null;
        // While capturing, temporarily unregister so our own combo doesn't
        // fire the action it's trying to bind.
        _hotkey?.SetHotkey(null);
    }

    private void VbHotkey_LostFocus(object sender, RoutedEventArgs e)
    {
        // Commit whatever they pressed (or restore the saved one).
        if (_pendingHotkey != null)
        {
            ApplyHotkey(_pendingHotkey);
            App.Config.MicHotkey = _pendingHotkey.Serialize();
            App.Config.Save();
        }
        else
        {
            // Restore previously saved binding (since focus disarmed it).
            var saved = HotkeyBinding.Deserialize(App.Config.MicHotkey);
            ApplyHotkey(saved);
        }
    }

    private void VbHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var binding = HotkeyBinding.FromKeyEvent(e);
        if (binding == null) return; // modifier-only press
        _pendingHotkey = binding;
        VbHotkeyBox.Text = binding.DisplayText;
    }

    private void VbClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        _pendingHotkey = null;
        App.Config.MicHotkey = string.Empty;
        App.Config.Save();
        ApplyHotkey(null);
    }

    private void ApplyHotkey(HotkeyBinding? binding)
    {
        if (_hotkey == null) return;
        var ok = _hotkey.SetHotkey(binding);
        if (binding == null || binding.VirtualKey == 0)
        {
            VbHotkeyBox.Text = "Click here, then press keys";
            return;
        }
        VbHotkeyBox.Text = ok
            ? binding.DisplayText
            : binding.DisplayText + "  (in use)";
    }

    // -----------------------------------------------------------------
    // API key hyperlink — opens the OpenAI key page in the default browser
    // -----------------------------------------------------------------
    private void VbApiKeyLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch { /* swallow — the user can copy the URL from the hint */ }
        e.Handled = true;
    }

    // -----------------------------------------------------------------
    // Smart Tip card — picks a random copy line on each open. Kept
    // intentionally short so the card stays compact at MinHeight=720.
    // -----------------------------------------------------------------
    private static readonly string[] _smartTips =
    {
        "Whisper costs about $0.006 per minute. Pick the spoken language above for higher accuracy.",
        "Speak naturally — short pauses end each chunk and trigger transcription.",
        "Click into your target app first (Word, browser, chat), then start the mic.",
        "Set a global hotkey in Settings to toggle the mic without leaving your work.",
        "For best results, use a quiet room and a headset mic about a hand's width away."
    };

    private void RotateSmartTip()
    {
        if (VbSmartTipText == null) return;
        var idx = Random.Shared.Next(_smartTips.Length);
        VbSmartTipText.Text = _smartTips[idx];
    }
}
