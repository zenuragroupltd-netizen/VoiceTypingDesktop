using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Records audio from the default microphone using NAudio. Saves to a
/// temporary WAV file. Transcription is delegated to the configured
/// <see cref="ITranscriptionEngine"/>. If no engine is set, the raw
/// recording duration is returned as a placeholder.
///
/// This class is the "real" recorder that replaces PlaceholderRecorderService.
/// </summary>
public sealed class MicRecorderService : IRecorderService, IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempFile;
    private DateTime _startedAt;

    public bool IsRecording { get; private set; }

    public event EventHandler<string>? TranscriptPartial;
    public event EventHandler<string>? TranscriptFinal;

    /// <summary>
    /// Optional transcription engine. If null, a placeholder message is
    /// returned after recording stops. Set this to a Whisper implementation
    /// when ready.
    /// </summary>
    public ITranscriptionEngine? Engine { get; set; }

    /// <summary>Peak level 0..1 updated ~10× per second while recording.</summary>
    public event EventHandler<float>? PeakLevelChanged;

    public Task StartAsync()
    {
        if (IsRecording) return Task.CompletedTask;

        _tempFile = Path.Combine(Path.GetTempPath(), $"vt_rec_{Guid.NewGuid():N}.wav");
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1), // 16 kHz mono — ideal for Whisper
            BufferMilliseconds = 100
        };
        _writer = new WaveFileWriter(_tempFile, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, args) =>
        {
            _writer?.Write(args.Buffer, 0, args.BytesRecorded);

            // Compute peak for the visual level meter.
            float peak = 0;
            for (int i = 0; i < args.BytesRecorded; i += 2)
            {
                var sample = Math.Abs((short)(args.Buffer[i] | (args.Buffer[i + 1] << 8)));
                if (sample > peak) peak = sample;
            }
            PeakLevelChanged?.Invoke(this, peak / 32768f);
        };

        _waveIn.RecordingStopped += (_, _) =>
        {
            _writer?.Dispose();
            _writer = null;
            _waveIn?.Dispose();
            _waveIn = null;
        };

        _startedAt = DateTime.Now;
        IsRecording = true;
        _waveIn.StartRecording();
        TranscriptPartial?.Invoke(this, "[recording...]");
        return Task.CompletedTask;
    }

    public async Task<string> StopAsync()
    {
        if (!IsRecording) return string.Empty;
        IsRecording = false;

        _waveIn?.StopRecording();
        // Give NAudio a moment to flush.
        await Task.Delay(150);

        var duration = (DateTime.Now - _startedAt).TotalSeconds;
        string transcript;

        if (Engine != null && File.Exists(_tempFile))
        {
            TranscriptPartial?.Invoke(this, "[transcribing...]");
            try
            {
                transcript = await Engine.TranscribeAsync(_tempFile);
            }
            catch (Exception ex)
            {
                transcript = $"[transcription error: {ex.Message}]";
            }
        }
        else
        {
            transcript = $"[recorded {duration:F1}s — connect a Whisper engine in Settings to transcribe]";
        }

        TranscriptFinal?.Invoke(this, transcript);
        CleanupTempFile();
        return transcript;
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();
        CleanupTempFile();
    }

    private void CleanupTempFile()
    {
        try { if (_tempFile != null && File.Exists(_tempFile)) File.Delete(_tempFile); }
        catch { /* best-effort */ }
        _tempFile = null;
    }
}
