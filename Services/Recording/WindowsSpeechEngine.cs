using System;
using System.Globalization;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Uses Windows built-in System.Speech (SAPI) for real-time speech-to-text.
/// Free, offline, no API key needed. Works on Windows 10/11.
///
/// Supported languages depend on which Windows language packs are installed.
/// English works out of the box on all Windows installations.
/// </summary>
public sealed class WindowsSpeechEngine : ITranscriptionEngine, IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private string _transcript = string.Empty;
    private readonly ManualResetEventSlim _done = new(false);
    private readonly string _culture;

    /// <summary>
    /// Creates the engine. Pass a culture string like "en-US", "hi-IN", "bn-IN".
    /// Default is the system's current culture.
    /// </summary>
    public WindowsSpeechEngine(string? culture = null)
    {
        _culture = culture ?? CultureInfo.CurrentCulture.Name;
    }

    public string DisplayName => $"Windows Speech ({_culture})";

    /// <summary>
    /// Transcribes a WAV file using Windows SAPI offline recognition.
    /// </summary>
    public Task<string> TranscribeAsync(string wavFilePath)
    {
        return Task.Run(() =>
        {
            _transcript = string.Empty;
            _done.Reset();

            try
            {
                var ci = new CultureInfo(_culture);
                _engine = new SpeechRecognitionEngine(ci);
            }
            catch
            {
                // Fallback to any available recognizer if the requested culture isn't installed.
                _engine = new SpeechRecognitionEngine();
            }

            _engine.LoadGrammar(new DictationGrammar());

            _engine.SpeechRecognized += (_, args) =>
            {
                if (args.Result?.Text != null)
                {
                    if (_transcript.Length > 0) _transcript += " ";
                    _transcript += args.Result.Text;
                }
            };

            _engine.RecognizeCompleted += (_, _) => _done.Set();

            _engine.SetInputToWaveFile(wavFilePath);
            _engine.RecognizeAsync(RecognizeMode.Multiple);

            // Wait up to 60 seconds for recognition to complete.
            _done.Wait(TimeSpan.FromSeconds(60));

            _engine.RecognizeAsyncCancel();
            _engine.Dispose();
            _engine = null;

            return string.IsNullOrWhiteSpace(_transcript)
                ? "[no speech detected]"
                : _transcript;
        });
    }

    public void Dispose()
    {
        try { _engine?.RecognizeAsyncCancel(); } catch { }
        try { _engine?.Dispose(); } catch { }
        _done.Dispose();
    }
}
