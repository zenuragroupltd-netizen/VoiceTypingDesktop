using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.Config;

/// <summary>
/// Simple persistable app config. Loads from appsettings.json next to the exe.
/// Replace the placeholder values with real Supabase credentials.
/// </summary>
public class AppConfig
{
    [JsonPropertyName("SUPABASE_URL")]
    public string SupabaseUrl { get; set; } = "REPLACE_WITH_YOUR_SUPABASE_URL";

    [JsonPropertyName("SUPABASE_ANON_KEY")]
    public string SupabaseAnonKey { get; set; } = "REPLACE_WITH_YOUR_SUPABASE_ANON_KEY";

    [JsonPropertyName("desktop_device_id")]
    public string DesktopDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("current_session_id")]
    public string CurrentSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Pair code shown to the mobile (persisted so the same QR works across
    /// desktop restarts instead of generating a fresh code every launch).
    /// Regenerated only when expired or when the user hits Re-pair.
    /// </summary>
    [JsonPropertyName("current_pair_code")]
    public string CurrentPairCode { get; set; } = string.Empty;

    /// <summary>
    /// ISO-8601 UTC timestamp until which the current pair code + session
    /// should be considered valid. Empty string means "not paired".
    /// 24 hours by convention.
    /// </summary>
    [JsonPropertyName("paired_until_utc")]
    public string PairedUntilUtc { get; set; } = string.Empty;

    /// <summary>"system", "dark" or "light". Defaults to "light".</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "light";

    // ---- Whisper / Voice Box settings ----

    /// <summary>
    /// OpenAI API key for Whisper transcription. Empty = use Windows Speech (free fallback).
    /// Store format: "sk-..." (never hardcoded, user enters in Settings).
    /// </summary>
    [JsonPropertyName("whisper_api_key")]
    public string WhisperApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Language mode for voice transcription.
    /// "auto" = let Whisper detect, "bn" = Bangla, "en" = English, or any ISO 639-1 code.
    /// </summary>
    [JsonPropertyName("whisper_language")]
    public string WhisperLanguage { get; set; } = "auto";

    /// <summary>
    /// When true, transcript is automatically sent to Direct Typing after recording stops.
    /// </summary>
    [JsonPropertyName("auto_paste_after_record")]
    public bool AutoPasteAfterRecord { get; set; } = false;

    /// <summary>
    /// Serialised user hotkey binding for toggling the mic. Empty = no hotkey.
    /// Format handled by HotkeyBinding.Serialize / Deserialize.
    /// </summary>
    [JsonPropertyName("mic_hotkey")]
    public string MicHotkey { get; set; } = string.Empty;

    /// <summary>True if a valid-looking OpenAI key is configured.</summary>
    [JsonIgnore]
    public bool HasWhisperApiKey =>
        !string.IsNullOrWhiteSpace(WhisperApiKey) &&
        WhisperApiKey.StartsWith("sk-") &&
        WhisperApiKey.Length > 20;

    [JsonIgnore]
    public static string ConfigFilePath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig LoadOrCreate()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // Fall through and create defaults.
        }

        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // Intentionally swallow in v1; later we can surface errors.
        }
    }

    [JsonIgnore]
    public bool HasSupabaseCredentials =>
        !string.IsNullOrWhiteSpace(SupabaseUrl) &&
        !SupabaseUrl.StartsWith("REPLACE_") &&
        !string.IsNullOrWhiteSpace(SupabaseAnonKey) &&
        !SupabaseAnonKey.StartsWith("REPLACE_");

    /// <summary>
    /// True if we have a saved pair code + session that hasn't expired yet.
    /// Used to short-circuit the pairing flow across restarts so the user
    /// doesn't have to rescan the QR within 24h.
    /// </summary>
    [JsonIgnore]
    public bool HasValidPairing
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentSessionId)) return false;
            if (string.IsNullOrWhiteSpace(PairedUntilUtc))   return false;
            if (!DateTime.TryParse(PairedUntilUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var expiry)) return false;
            return expiry.ToUniversalTime() > DateTime.UtcNow;
        }
    }

    /// <summary>
    /// True if the saved pair code is still within its 24h window, regardless
    /// of whether a session has been established yet.
    /// </summary>
    [JsonIgnore]
    public bool HasValidPairCode
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPairCode)) return false;
            if (string.IsNullOrWhiteSpace(PairedUntilUtc))  return false;
            if (!DateTime.TryParse(PairedUntilUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var expiry)) return false;
            return expiry.ToUniversalTime() > DateTime.UtcNow;
        }
    }

    [JsonIgnore]
    public DateTime? PairedUntilUtcParsed
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PairedUntilUtc)) return null;
            if (!DateTime.TryParse(PairedUntilUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var ts)) return null;
            return ts.ToUniversalTime();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
