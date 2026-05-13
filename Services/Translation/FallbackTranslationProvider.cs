using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Tries a primary provider and, on failure, silently falls back to a
/// secondary one. Keeps the UI resilient when one public service is
/// having a bad day.
/// </summary>
public sealed class FallbackTranslationProvider : ITranslationProvider
{
    private readonly ITranslationProvider _primary;
    private readonly ITranslationProvider _secondary;

    public FallbackTranslationProvider(ITranslationProvider primary, ITranslationProvider secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public string DisplayName => $"{_primary.DisplayName} ▸ {_secondary.DisplayName}";

    public async Task<TranslationResult> TranslateAsync(
        string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        try
        {
            return await _primary.TranslateAsync(text, sourceLang, targetLang, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return await _secondary.TranslateAsync(text, sourceLang, targetLang, ct);
        }
    }
}
