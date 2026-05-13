using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Contract for a translation backend. Keeping this abstract means we can
/// swap MyMemory for DeepL / Google / LibreTranslate later without touching
/// the UI.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>Display name shown in the Settings / Translator UI.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Translate <paramref name="text"/> from <paramref name="sourceLang"/> to
    /// <paramref name="targetLang"/>. Use "auto" as source to let the provider
    /// detect the language.
    /// </summary>
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default);
}

public sealed record TranslationResult(
    string TranslatedText,
    string? DetectedSourceLang,
    string ProviderName);
