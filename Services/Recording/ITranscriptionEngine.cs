using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Contract for a speech-to-text engine. Implementations:
/// - WhisperApiEngine (OpenAI Whisper API, online)
/// - FasterWhisperEngine (local, offline, fast)
/// - PlaceholderEngine (returns a stub message)
///
/// The engine receives a path to a WAV file and returns the transcript.
/// </summary>
public interface ITranscriptionEngine
{
    string DisplayName { get; }
    Task<string> TranscribeAsync(string wavFilePath);
}
