using System;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Stub recorder. Produces a fake transcript so the UI wiring can be tested.
/// Replace with Faster Whisper / Whisper API in a later step.
/// </summary>
public class PlaceholderRecorderService : IRecorderService
{
    public bool IsRecording { get; private set; }

    public event EventHandler<string>? TranscriptPartial;
    public event EventHandler<string>? TranscriptFinal;

    private DateTime _startedAt;

    public Task StartAsync()
    {
        IsRecording = true;
        _startedAt = DateTime.Now;
        TranscriptPartial?.Invoke(this, "[listening...]");
        return Task.CompletedTask;
    }

    public Task<string> StopAsync()
    {
        IsRecording = false;
        var duration = (DateTime.Now - _startedAt).TotalSeconds;
        var text = $"[placeholder transcript, recorded {duration:F1}s at {DateTime.Now:T}]";
        TranscriptFinal?.Invoke(this, text);
        return Task.FromResult(text);
    }
}
