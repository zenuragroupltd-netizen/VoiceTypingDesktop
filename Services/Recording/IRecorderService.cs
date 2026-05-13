using System;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Contract for a recorder/transcriber. Concrete implementations will be
/// added later (Faster Whisper, Whisper API). For now we ship a placeholder
/// so the UI flow can be validated.
/// </summary>
public interface IRecorderService
{
    bool IsRecording { get; }

    /// <summary>Raised as interim text arrives (optional).</summary>
    event EventHandler<string>? TranscriptPartial;

    /// <summary>Raised when final transcript is ready after StopAsync.</summary>
    event EventHandler<string>? TranscriptFinal;

    Task StartAsync();
    Task<string> StopAsync();
}
