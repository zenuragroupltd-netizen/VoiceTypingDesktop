using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Thin orchestrator around an <see cref="ITranslationProvider"/>. Keeps a
/// single active provider so the UI can bind once. Defaults to the free
/// MyMemory provider; swap with <see cref="Set"/>.
/// </summary>
public sealed class TranslationService
{
    private ITranslationProvider _provider;

    public TranslationService() : this(
        new FallbackTranslationProvider(
            new LingvaTranslationProvider(),
            new MyMemoryTranslationProvider())) { }

    public TranslationService(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ITranslationProvider Active => _provider;

    public void Set(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
        => _provider.TranslateAsync(text, sourceLang, targetLang, ct);
}
