using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Translation;

/// <summary>
/// Free, anonymous translation backend: https://mymemory.translated.net/
/// • No sign-up, no API key.
/// • Anonymous daily quota is ~5 000 characters; well beyond casual use.
/// • Accepts ISO 639-1 codes. Source = "auto" tells the service to detect.
/// </summary>
public sealed class MyMemoryTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public string DisplayName => "MyMemory (free)";

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, null, DisplayName);

        // MyMemory uses `langpair=<src>|<tgt>`. It accepts "Autodetect" for
        // source. We normalise "auto" to that spelling.
        var src = string.IsNullOrWhiteSpace(sourceLang) || sourceLang == "auto"
            ? "Autodetect"
            : sourceLang;

        var url = "https://api.mymemory.translated.net/get" +
                  "?q=" + Uri.EscapeDataString(text) +
                  "&langpair=" + Uri.EscapeDataString($"{src}|{targetLang}");

        using var resp = await Http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"MyMemory returned {(int)resp.StatusCode}. {Trim(body, 200)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Typical payload:
        // { "responseData": { "translatedText": "...", "match": 0.85 },
        //   "responseStatus": 200, "responseDetails": "", ... }
        var status = root.TryGetProperty("responseStatus", out var s)
            ? s.ValueKind == JsonValueKind.Number ? s.GetInt32() : int.TryParse(s.GetString(), out var si) ? si : 0
            : 0;
        if (status != 200 && status != 0)
        {
            var details = root.TryGetProperty("responseDetails", out var d) ? d.GetString() : null;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details) ? "Translation failed." : details);
        }

        string translated = string.Empty;
        if (root.TryGetProperty("responseData", out var data) &&
            data.TryGetProperty("translatedText", out var tt))
        {
            translated = tt.GetString() ?? string.Empty;
        }

        // MyMemory occasionally embeds its error string in translatedText
        // (e.g. "PLEASE SELECT TWO DISTINCT LANGUAGES"). Surface that.
        if (translated.StartsWith("PLEASE ") || translated.StartsWith("INVALID "))
            throw new InvalidOperationException(translated);

        string? detected = null;
        if (root.TryGetProperty("responseData", out data) &&
            data.TryGetProperty("detectedLanguage", out var dl))
        {
            detected = dl.GetString();
        }

        return new TranslationResult(translated, detected, DisplayName);
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
}
