using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Human-readable language list for the Translator UI. Each entry carries
/// the ISO 639-1 code (what translation providers consume), a display
/// name, an emoji flag (legacy fallback), and the country code we use to
/// pick a PNG flag from <c>Assets\Flags</c>.
///
/// === How to add a new language ===
/// 1. Add a new entry below with an appropriate CountryCode.
/// 2. If the country's PNG doesn't exist yet, either drop a new
///    <c>Assets\Flags\xx.png</c> into the project or re-run
///    <c>Tools\make-flags.ps1</c> after extending it.
/// 3. No other code needs to change — the UI uses
///    <see cref="LanguageOption.FlagAssetPath"/> which is resolved
///    dynamically.
/// </summary>
public static class LanguageCatalog
{
    public sealed record LanguageOption(
        string Code,
        string Name,
        string Flag,
        string CountryCode)
    {
        /// <summary>True for the special "Auto detect" entry.</summary>
        public bool IsAutoDetect => string.Equals(Code, "auto", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Pack URI of the PNG flag inside the exe's resources. Always
        /// returns a path — actual on-disk availability is verified by
        /// <see cref="HasFlagAsset"/>.
        /// </summary>
        public string FlagAssetPath =>
            $"pack://application:,,,/Assets/Flags/{CountryCode}.png";

        /// <summary>
        /// True if a matching PNG actually ships with the app. Checks the
        /// WPF resource stream (our PNGs are embedded as &lt;Resource&gt; in
        /// the csproj, not loose files on disk), with the result cached per
        /// country code so we only probe each flag once.
        /// </summary>
        public bool HasFlagAsset =>
            _flagExistenceCache.GetOrAdd(CountryCode, static cc =>
            {
                try
                {
                    var uri = new Uri(
                        $"pack://application:,,,/Assets/Flags/{cc}.png",
                        UriKind.Absolute);
                    var info = System.Windows.Application.GetResourceStream(uri);
                    if (info?.Stream == null) return false;
                    info.Stream.Dispose();
                    return true;
                }
                catch { return false; }
            });

        private static readonly ConcurrentDictionary<string, bool> _flagExistenceCache = new();

        /// <summary>
        /// Short 2-letter fallback badge text (e.g. "BD" for Bengali). Shown
        /// only when <see cref="HasFlagAsset"/> is false.
        /// </summary>
        public string FallbackBadge => CountryCode.ToUpperInvariant();
    }

    /// <summary>
    /// The special "auto" option that asks the provider to detect the source
    /// language. Not valid as a target.
    /// </summary>
    public static readonly LanguageOption Auto =
        new("auto", "Auto detect", "🌐", "auto");

    /// <summary>
    /// Top-level curated list. Broad enough that users feel the translator
    /// supports "every language", while keeping the dropdown navigable.
    /// Providers may not support every combination.
    /// </summary>
    public static readonly IReadOnlyList<LanguageOption> All = new List<LanguageOption>
    {
        new("en",    "English",            "🇬🇧", "gb"),
        new("bn",    "বাংলা (Bengali)",    "🇧🇩", "bd"),
        new("hi",    "हिन्दी (Hindi)",      "🇮🇳", "in"),
        new("ur",    "اردو (Urdu)",         "🇵🇰", "pk"),
        new("ar",    "العربية (Arabic)",    "🇸🇦", "sa"),
        new("es",    "Español (Spanish)",  "🇪🇸", "es"),
        new("fr",    "Français (French)",  "🇫🇷", "fr"),
        new("de",    "Deutsch (German)",   "🇩🇪", "de"),
        new("it",    "Italiano (Italian)", "🇮🇹", "it"),
        new("pt",    "Português",          "🇵🇹", "pt"),
        new("ru",    "Русский (Russian)",  "🇷🇺", "ru"),
        new("zh-CN", "中文 (Chinese)",      "🇨🇳", "cn"),
        new("ja",    "日本語 (Japanese)",   "🇯🇵", "jp"),
        new("ko",    "한국어 (Korean)",     "🇰🇷", "kr"),
        new("tr",    "Türkçe (Turkish)",   "🇹🇷", "tr"),
        new("id",    "Indonesia",           "🇮🇩", "id"),
        new("ms",    "Melayu",              "🇲🇾", "my"),
        new("th",    "ไทย (Thai)",          "🇹🇭", "th"),
        new("vi",    "Tiếng Việt",          "🇻🇳", "vn"),
        new("nl",    "Nederlands",          "🇳🇱", "nl"),
        new("pl",    "Polski",              "🇵🇱", "pl"),
        new("uk",    "Українська",          "🇺🇦", "ua"),
        new("sv",    "Svenska",             "🇸🇪", "se"),
        new("no",    "Norsk",               "🇳🇴", "no"),
        new("da",    "Dansk",               "🇩🇰", "dk"),
        new("fi",    "Suomi",               "🇫🇮", "fi"),
        new("cs",    "Čeština",             "🇨🇿", "cz"),
        new("el",    "Ελληνικά",            "🇬🇷", "gr"),
        new("he",    "עברית (Hebrew)",       "🇮🇱", "il"),
        new("fa",    "فارسی (Persian)",      "🇮🇷", "ir"),
        new("ta",    "தமிழ் (Tamil)",        "🇮🇳", "in"),
        new("te",    "తెలుగు (Telugu)",      "🇮🇳", "in"),
        new("ml",    "മലയാളം (Malayalam)",    "🇮🇳", "in"),
        new("mr",    "मराठी (Marathi)",       "🇮🇳", "in"),
        new("gu",    "ગુજરાતી (Gujarati)",     "🇮🇳", "in"),
        new("pa",    "ਪੰਜਾਬੀ (Punjabi)",       "🇮🇳", "in"),
        new("si",    "සිංහල (Sinhala)",        "🇱🇰", "lk"),
        new("ne",    "नेपाली (Nepali)",        "🇳🇵", "np"),
        new("ro",    "Română",                 "🇷🇴", "ro"),
        new("hu",    "Magyar",                 "🇭🇺", "hu"),
        new("bg",    "Български",              "🇧🇬", "bg"),
        new("sr",    "Српски",                 "🇷🇸", "rs"),
        new("hr",    "Hrvatski",               "🇭🇷", "hr"),
        new("sk",    "Slovenčina",             "🇸🇰", "sk"),
        new("sl",    "Slovenščina",            "🇸🇮", "si"),
        new("is",    "Íslenska",               "🇮🇸", "is"),
        new("af",    "Afrikaans",              "🇿🇦", "za"),
        new("sw",    "Kiswahili",              "🇰🇪", "ke"),
        new("tl",    "Filipino",               "🇵🇭", "ph"),
    };
}
