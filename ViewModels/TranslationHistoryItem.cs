using System;
using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.ViewModels;

public sealed class TranslationHistoryItem
{
    public string SourceText     { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLang     { get; set; } = "auto";
    public string TargetLang     { get; set; } = "en";
    public DateTime CreatedAt    { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string LangPairDisplay =>
        $"{SourceLang.ToUpperInvariant()} → {TargetLang.ToUpperInvariant()}";

    [JsonIgnore]
    public string CreatedAtDisplay =>
        CreatedAt.ToLocalTime().ToString("HH:mm");

    [JsonIgnore]
    public string Preview
    {
        get
        {
            var t = (TranslatedText ?? string.Empty).Replace("\n", " ").Trim();
            return t.Length <= 90 ? t : t.Substring(0, 90) + "...";
        }
    }
}
