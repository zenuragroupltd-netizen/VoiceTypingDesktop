using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// OpenAI Whisper API transcription engine — direct client mode.
/// Each user pastes their own OpenAI API key in Settings; the key lives
/// locally in appsettings.json. The developer pays zero API cost.
///
/// Supports: Bangla (bn), English (en), Hindi (hi), Auto-detect, and 50+
/// other languages.
/// </summary>
public sealed class WhisperApiEngine : ITranscriptionEngine
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly string _apiKey;
    private readonly string _language;
    private readonly string _model;

    /// <summary>
    /// OpenAI's flagship 2025 transcription model. Best non-English
    /// accuracy (Bangla/Hindi/Urdu/Arabic) among the hosted OpenAI audio
    /// models, on par with whisper-1 price-wise ($0.006/min) but much
    /// better output quality. Default for this app.
    /// </summary>
    public const string DefaultModel = "whisper-1";

    /// <summary>
    /// Approximate per-minute audio input rate in USD, used by the UI to
    /// show a live cost estimate. Kept in-code so we don't need a network
    /// call to OpenAI; values reflect public pricing as of 2025.
    /// </summary>
    public static decimal GetUsdPerMinute(string model) => model switch
    {
        "gpt-4o-mini-transcribe" => 0.003m,
        "gpt-4o-transcribe"      => 0.006m,
        "whisper-1"              => 0.006m,
        _                        => 0.006m // safe default
    };

    /// <summary>The model this engine was created with.</summary>
    public string Model => _model;

    /// <summary>
    /// Creates the engine with the user's personal OpenAI API key.
    /// <paramref name="model"/> defaults to <see cref="DefaultModel"/>;
    /// pass "whisper-1" to use the legacy model if needed.
    /// </summary>
    public WhisperApiEngine(string apiKey, string language = "auto", string? model = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));
        _apiKey = apiKey.Trim();

        // OpenAI's transcription API expects a plain ISO 639-1 language
        // code (e.g. "bn", "en"). The UI persists BCP-47-style values
        // like "bn-BD" or "en-US" so we can remember a user's specific
        // regional preference; strip everything after the dash here.
        if (string.IsNullOrWhiteSpace(language) || language == "auto")
        {
            _language = "";
        }
        else
        {
            var trimmed = language.Trim().ToLowerInvariant();
            var dash = trimmed.IndexOf('-');
            _language = dash >= 0 ? trimmed.Substring(0, dash) : trimmed;
        }

        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    public string DisplayName => string.IsNullOrEmpty(_language)
        ? $"{_model} (auto)"
        : $"{_model} ({_language})";

    public async Task<string> TranscribeAsync(string wavFilePath)
    {
        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException("Recording file not found.", wavFilePath);

        var fileInfo = new FileInfo(wavFilePath);
        if (fileInfo.Length < 1000) return "[recording too short]";
        if (fileInfo.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("Recording too large (max 25 MB).");

        var fileBytes = await File.ReadAllBytesAsync(wavFilePath);

        // A short seed sentence in the user's chosen language. Whisper uses
        // the `prompt` parameter as a style / language hint, so this nudges
        // the output to stay in the right script even when the language
        // detector would otherwise confuse phonetically-close languages
        // (Bangla ↔ Hindi being the classic confusion).
        var prompt = GetLanguagePrompt(_language);

        // First attempt: with both the language code AND the seed prompt.
        var firstAttempt = await PostTranscriptionAsync(fileBytes, _language, prompt);
        if (firstAttempt.success) return firstAttempt.text;

        // Defensive retry: some OpenAI accounts/APIs reject specific ISO
        // codes with a 400 "language not supported" response. In that case
        // drop the language code but KEEP the seed prompt — Whisper will
        // pick up the right script from the prompt without us telling it
        // an unsupported code. This is safer than retrying with pure
        // auto-detect, which routinely mis-identifies Bangla as Hindi.
        if (!string.IsNullOrEmpty(_language)
            && firstAttempt.statusCode == HttpStatusCode.BadRequest
            && firstAttempt.errorMessage.IndexOf("language",
                   StringComparison.OrdinalIgnoreCase) >= 0
            && !string.IsNullOrEmpty(prompt))
        {
            var retry = await PostTranscriptionAsync(fileBytes, "", prompt);
            if (retry.success) return retry.text;
            // Fall through to throw the ORIGINAL error.
        }

        throw firstAttempt.statusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(
                "Invalid API key. Check your OpenAI key in Settings."),
            HttpStatusCode.TooManyRequests => new InvalidOperationException(
                "Rate limit exceeded. Wait a moment and try again."),
            HttpStatusCode.PaymentRequired => new InvalidOperationException(
                "Your OpenAI account has no credits. Add billing at platform.openai.com."),
            HttpStatusCode.RequestEntityTooLarge => new InvalidOperationException(
                "Recording too large (max 25 MB)."),
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway =>
                new InvalidOperationException("OpenAI service temporarily unavailable."),
            _ => new InvalidOperationException(
                $"Whisper error ({(int)firstAttempt.statusCode}): {firstAttempt.errorMessage}")
        };
    }

    /// <summary>
    /// Posts the audio to OpenAI's transcription endpoint and either returns
    /// the transcribed text (success=true) or the HTTP error details for
    /// the caller to decide how to surface / retry.
    /// </summary>
    private async Task<(bool success, string text, HttpStatusCode statusCode, string errorMessage)>
        PostTranscriptionAsync(byte[] fileBytes, string language, string prompt)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "recording.wav");

        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("text"), "response_format");
        if (!string.IsNullOrEmpty(language))
            form.Add(new StringContent(language.Trim().ToLowerInvariant()), "language");
        if (!string.IsNullOrEmpty(prompt))
            form.Add(new StringContent(prompt), "prompt");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = form;

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException("Request timed out. Check your internet.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Network error: {ex.Message}. Check your internet connection.");
        }

        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var text = body?.Trim();

            // Whisper sometimes echoes the `prompt` verbatim in its output
            // — especially on silent, very short, or noisy audio where it
            // has nothing real to transcribe. Strip any leading/trailing
            // occurrence so the seed sentence never gets typed into the
            // user's document.
            if (!string.IsNullOrEmpty(prompt) && !string.IsNullOrWhiteSpace(text))
            {
                text = StripPromptEcho(text!, prompt);
            }

            // Second pass: drop known Whisper hallucinations (greetings /
            // YouTube-style filler that the model emits when it hears only
            // silence or noise). Catches "নমস্কার।", "नमस्ते।",
            // "Thanks for watching", etc.
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = StripHallucinations(text!);
            }

            return (true,
                    string.IsNullOrWhiteSpace(text) ? "[no speech detected]" : text!,
                    response.StatusCode,
                    "");
        }

        return (false, "", response.StatusCode, ExtractErrorMessage(body));
    }

    /// <summary>
    /// Removes any verbatim echo of the seed <paramref name="prompt"/> from
    /// the start and/or end of <paramref name="text"/>. Comparison is
    /// punctuation-tolerant (leading/trailing whitespace + "।" + "." +
    /// "," are normalised away) so we also catch the common case where
    /// Whisper drops a period or swaps Bangla "।" for "."
    /// </summary>
    private static string StripPromptEcho(string text, string prompt)
    {
        static string Normalise(string s) =>
            s.Trim().TrimEnd('।', '.', ',', '!', '?', ' ').Trim();

        var normalisedPrompt = Normalise(prompt);
        if (normalisedPrompt.Length == 0) return text;

        var result = text;

        // Strip prompt from the START, possibly repeated.
        while (true)
        {
            var trimmed = result.TrimStart();
            if (trimmed.StartsWith(normalisedPrompt, StringComparison.Ordinal))
            {
                result = trimmed.Substring(normalisedPrompt.Length)
                                .TrimStart('।', '.', ',', '!', '?', ' ');
                continue;
            }
            if (trimmed.StartsWith(prompt, StringComparison.Ordinal))
            {
                result = trimmed.Substring(prompt.Length)
                                .TrimStart('।', '.', ',', '!', '?', ' ');
                continue;
            }
            break;
        }

        // Strip prompt from the END, possibly repeated.
        while (true)
        {
            var trimmed = result.TrimEnd();
            if (trimmed.EndsWith(prompt, StringComparison.Ordinal))
            {
                result = trimmed.Substring(0, trimmed.Length - prompt.Length)
                                .TrimEnd('।', '.', ',', '!', '?', ' ');
                continue;
            }
            if (trimmed.EndsWith(normalisedPrompt, StringComparison.Ordinal))
            {
                result = trimmed.Substring(0, trimmed.Length - normalisedPrompt.Length)
                                .TrimEnd('।', '.', ',', '!', '?', ' ');
                continue;
            }
            break;
        }

        return result.Trim();
    }

    /// <summary>
    /// Known Whisper hallucinations — boilerplate that the model produces
    /// when it hears only silence, room noise, or audio that's too short
    /// to have real content. We strip these wholesale from the start and
    /// end of any transcript so they never leak into the user's document.
    /// Each entry is matched case-insensitively and ignores trailing
    /// punctuation (. , ! ? । ।) so we catch variants like "নমস্কার।",
    /// "নমস্কার.", "নমস্কার " and "নমস্কার" identically.
    /// </summary>
    private static readonly string[] KnownHallucinations =
    {
        // --- Bangla / Hindi greetings (the main complaint) ---
        "নমস্কার",
        "সবাইকে নমস্কার",
        "আসসালামু আলাইকুম",
        "ধন্যবাদ",
        "ধন্যবাদ সবাইকে",
        "নমস্তে",
        "नमस्ते",
        "नमस्कार",
        "सभी को नमस्कार",
        "धन्यवाद",
        "शुक्रिया",
        // --- Urdu / Arabic ---
        "السلام علیکم",
        "شكرا",
        "شكرا لكم",
        // --- English YouTube-style filler ---
        "Thanks for watching",
        "Thanks for watching!",
        "Thank you for watching",
        "Thank you.",
        "Thank you",
        "Please subscribe",
        "Like and subscribe",
        "Subscribe to my channel",
        "See you next time",
        "See you in the next video",
        "Bye bye",
        "Bye",
        // --- Chinese / Japanese / Korean filler ---
        "ご視聴ありがとうございました",
        "请订阅",
        "感谢观看",
        "구독 부탁드립니다",
    };

    /// <summary>
    /// Removes any of <see cref="KnownHallucinations"/> that appear at the
    /// start or end of <paramref name="text"/>. If after stripping the
    /// text is empty, returns "" so the pipeline reports "[no speech]".
    /// </summary>
    private static string StripHallucinations(string text)
    {
        string TrimPunct(string s) =>
            s.Trim().Trim('।', '.', ',', '!', '?', ' ', '\u0964', '\u06D4').Trim();

        var current = text;
        bool changed;
        do
        {
            changed = false;
            foreach (var h in KnownHallucinations)
            {
                var trimmedStart = current.TrimStart();
                if (trimmedStart.StartsWith(h, StringComparison.OrdinalIgnoreCase))
                {
                    var after = trimmedStart.Substring(h.Length);
                    current = TrimPunct(after);
                    changed = true;
                    break;
                }

                var trimmedEnd = current.TrimEnd();
                if (trimmedEnd.EndsWith(h, StringComparison.OrdinalIgnoreCase))
                {
                    var before = trimmedEnd.Substring(0, trimmedEnd.Length - h.Length);
                    current = TrimPunct(before);
                    changed = true;
                    break;
                }

                // Whole transcript is just the greeting → drop it entirely.
                if (string.Equals(TrimPunct(current), h, StringComparison.OrdinalIgnoreCase))
                {
                    current = string.Empty;
                    changed = true;
                    break;
                }
            }
        }
        while (changed && !string.IsNullOrEmpty(current));

        return current;
    }

    /// <summary>
    /// A short native-script seed sentence per supported language. Passed
    /// to Whisper as the `prompt` parameter so the model preserves the
    /// correct script in its output — this is the official OpenAI
    /// recommendation for biasing transcription style and language.
    ///
    /// **Important:** the seed must NOT contain any common greeting or
    /// other phrase that Whisper loves to echo on silent audio (নমস্কার,
    /// नमस्ते, السلام علیکم, "Hello", "Thank you", etc.), otherwise the
    /// model will paste those words into real transcripts. We use neutral
    /// descriptive sentences instead.
    ///
    /// Returns "" for "auto" / unknown codes so we fall back to pure
    /// auto-detect (no prompt) in those cases.
    /// </summary>
    private static string GetLanguagePrompt(string language) => language switch
    {
        "bn" => "এই অডিওটি বাংলা ভাষায় রেকর্ড করা হয়েছে।",
        "hi" => "यह ऑडियो हिंदी भाषा में रिकॉर्ड की गई है।",
        "ur" => "یہ آڈیو اردو میں ریکارڈ کی گئی ہے۔",
        "ar" => "هذا التسجيل باللغة العربية.",
        "en" => "The audio below is recorded in English.",
        "es" => "Esta grabación está en español.",
        "fr" => "Cet enregistrement est en français.",
        "de" => "Diese Aufnahme ist auf Deutsch.",
        "ru" => "Эта запись на русском языке.",
        "zh" => "这段录音是中文的。",
        "ja" => "この録音は日本語で録音されました。",
        "ko" => "이 녹음은 한국어로 녹음되었습니다.",
        "tr" => "Bu ses kaydı Türkçe.",
        "id" => "Rekaman ini dalam Bahasa Indonesia.",
        "pt" => "Esta gravação está em português.",
        _    => ""
    };

    private static string ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Unknown error";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Unknown error";
        }
        catch { /* not JSON */ }
        return body.Length > 200 ? body.Substring(0, 200) : body;
    }
}
