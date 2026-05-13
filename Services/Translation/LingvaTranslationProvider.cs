using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Free, anonymous translation via Lingva — a public, read-only Google
/// Translate front-end that returns JSON. No API key required.
///
/// We try multiple mirror hosts because the main instance (lingva.ml) is
/// sometimes 503/rate-limited. The first host that answers wins; the list
/// is lightly cached so we don't re-probe every request.
/// </summary>
public sealed class LingvaTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Preferred hosts in priority order. Kept plain strings so they are
    // trivial to tweak without touching logic.
    private static readonly string[] Mirrors =
    {
        "https://lingva.ml",
        "https://translate.plausibility.cloud",
        "https://lingva.garudalinux.org",
        "https://lingva.pussthecat.org"
    };

    private static readonly object GateLock = new();
    private static string? _preferredMirror;

    public string DisplayName => "Lingva (free)";

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, null, DisplayName);

        var src = NormalizeLang(sourceLang);
        var tgt = NormalizeLang(targetLang);

        // Try the cached "good" mirror first; fall through to others on error.
        string? cached;
        lock (GateLock) cached = _preferredMirror;

        Exception? firstError = null;
        foreach (var host in BuildHostOrder(cached))
        {
            try
            {
                var url = $"{host}/api/v1/{Uri.EscapeDataString(src)}/{Uri.EscapeDataString(tgt)}/{Uri.EscapeDataString(text)}";
                using var resp = await Http.GetAsync(url, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"{host} returned {(int)resp.StatusCode}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("translation", out var t))
                    throw new InvalidOperationException("Missing translation field.");

                var translated = t.GetString() ?? string.Empty;
                string? detected = null;
                if (root.TryGetProperty("info", out var info) &&
                    info.TryGetProperty("detectedSource", out var dl))
                {
                    detected = dl.GetString();
                }

                // Lock in the winning host for the next call.
                lock (GateLock) _preferredMirror = host;
                return new TranslationResult(translated, detected, DisplayName);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                firstError ??= ex;
                // try next mirror
            }
        }

        throw new InvalidOperationException(
            "All Lingva mirrors failed. " + (firstError?.Message ?? ""));
    }

    /// <summary>
    /// Lingva expects "auto" (lowercase) for detection and plain ISO codes.
    /// Some codes like "zh-CN" need to become "zh_HANS".
    /// </summary>
    private static string NormalizeLang(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "auto";
        var s = lang.Trim();
        return s.ToLowerInvariant() switch
        {
            "auto"        => "auto",
            "zh-cn" or "zh_cn" or "zh-hans"         => "zh",
            "zh-tw" or "zh_tw" or "zh-hant"         => "zh_HANT",
            _ => s
        };
    }

    private static IEnumerable<string> BuildHostOrder(string? preferred)
    {
        if (!string.IsNullOrEmpty(preferred)) yield return preferred;
        foreach (var m in Mirrors) if (m != preferred) yield return m;
    }
}
