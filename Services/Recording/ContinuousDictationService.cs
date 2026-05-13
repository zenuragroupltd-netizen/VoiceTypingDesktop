using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VoiceTypingDesktop.Services.Recording;

/// <summary>
/// Continuous dictation: listens to the microphone non-stop, detects speech
/// chunks via silence detection, sends each chunk to the transcription engine,
/// and fires an event with the resulting text so the UI can paste it into
/// the active window immediately.
///
/// No Start/Stop per utterance — just Mic On / Mic Off.
/// </summary>
public sealed class ContinuousDictationService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentChunkFile;
    private readonly ITranscriptionEngine _engine;

    private CancellationTokenSource? _cts;
    private DateTime _lastSoundAt;
    private bool _isSpeaking;
    private int _chunkIndex;

    // Tuning parameters. Picked to feel responsive without cutting words in
    // half: 600ms silence after a spoken phrase is enough to mark its end
    // for typical dictation, and 0.5s minimum chunk catches short replies
    // like "yes" / "ঠিক আছে" without discarding them as noise.
    private const float SilenceThreshold    = 0.02f;   // below this = silence
    private const double SilenceGapMs       = 600;     // 0.6s silence = end of utterance
    private const double MaxChunkSec        = 30;      // force-send after 30s of continuous speech
    private const double MinChunkSec        = 0.5;     // ignore chunks shorter than this
    private const double AutoOffSilenceSec  = 8;       // mic auto-off after 8s of no speech — keeps idle audio (and API cost) down

    public bool IsListening { get; private set; }

    /// <summary>Fired when a transcribed chunk is ready to be typed out.</summary>
    public event EventHandler<string>? TextReady;

    /// <summary>State changes: "Listening", "Processing...", "Idle".</summary>
    public event EventHandler<string>? StateChanged;

    /// <summary>Peak level for UI meter.</summary>
    public event EventHandler<float>? PeakLevelChanged;

    /// <summary>Errors that don't kill the service (e.g. one chunk failed).</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Raised when the mic auto-stops because no speech was heard for
    /// <c>AutoOffSilenceSec</c>. UI can update its button state.
    /// </summary>
    public event EventHandler? AutoStopped;

    /// <summary>
    /// Raised after a chunk is successfully sent to the transcription
    /// engine so the UI can accumulate billable audio seconds and show
    /// a live cost estimate. <c>e</c> is the chunk's audio duration in
    /// seconds (already billed by the upstream provider).
    /// </summary>
    public event EventHandler<double>? ChunkTranscribed;

    public ContinuousDictationService(ITranscriptionEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void Start()
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _chunkIndex = 0;
        _isSpeaking = false;
        _lastSoundAt = DateTime.UtcNow;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += (_, _) => { };

        IsListening = true;
        _waveIn.StartRecording();
        StateChanged?.Invoke(this, "Listening");
    }

    public void Stop()
    {
        if (!IsListening) return;
        IsListening = false;

        _cts?.Cancel();
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        // Flush any in-progress chunk.
        FinalizeCurrentChunk();

        StateChanged?.Invoke(this, "Idle");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (!IsListening) return;

        // Compute peak.
        float peak = 0;
        for (int i = 0; i < args.BytesRecorded; i += 2)
        {
            var sample = Math.Abs((short)(args.Buffer[i] | (args.Buffer[i + 1] << 8)));
            if (sample > peak) peak = sample;
        }
        var normalizedPeak = peak / 32768f;
        PeakLevelChanged?.Invoke(this, normalizedPeak);

        var now = DateTime.UtcNow;
        bool hasSound = normalizedPeak > SilenceThreshold;

        if (hasSound)
        {
            _lastSoundAt = now;

            if (!_isSpeaking)
            {
                // Speech started — begin a new chunk.
                _isSpeaking = true;
                StartNewChunk();
            }
        }

        // Write audio to current chunk if we're in a speech segment.
        if (_isSpeaking && _writer != null)
        {
            _writer.Write(args.Buffer, 0, args.BytesRecorded);

            // Check if we should finalize: silence gap exceeded or max duration.
            var silenceElapsed = (now - _lastSoundAt).TotalMilliseconds;
            var chunkDuration = _writer.TotalTime.TotalSeconds;

            if (silenceElapsed >= SilenceGapMs || chunkDuration >= MaxChunkSec)
            {
                FinalizeCurrentChunk();
            }
        }
        else
        {
            // Idle state: check auto-off timeout.
            var idleSec = (now - _lastSoundAt).TotalSeconds;
            if (idleSec >= AutoOffSilenceSec)
            {
                // Trigger auto-off from a separate thread so we don't block
                // the audio callback (Stop() calls WaveIn.StopRecording).
                Task.Run(() =>
                {
                    if (!IsListening) return;
                    Stop();
                    AutoStopped?.Invoke(this, EventArgs.Empty);
                });
            }
        }
    }

    private void StartNewChunk()
    {
        _chunkIndex++;
        _currentChunkFile = Path.Combine(Path.GetTempPath(),
            $"vt_chunk_{_chunkIndex}_{Guid.NewGuid():N}.wav");
        _writer = new WaveFileWriter(_currentChunkFile, new WaveFormat(16000, 16, 1));
    }

    private void FinalizeCurrentChunk()
    {
        if (_writer == null || _currentChunkFile == null)
        {
            _isSpeaking = false;
            return;
        }

        var duration = _writer.TotalTime.TotalSeconds;
        _writer.Dispose();
        _writer = null;
        _isSpeaking = false;

        var filePath = _currentChunkFile;
        _currentChunkFile = null;

        // Skip very short chunks (noise, clicks).
        if (duration < MinChunkSec)
        {
            TryDeleteFile(filePath);
            return;
        }

        // Transcribe asynchronously — don't block the audio thread.
        var ct = _cts?.Token ?? CancellationToken.None;
        _ = TranscribeChunkAsync(filePath, duration, ct);
    }

    private async Task TranscribeChunkAsync(string filePath, double durationSec, CancellationToken ct)
    {
        try
        {
            StateChanged?.Invoke(this, "Processing...");

            var text = await _engine.TranscribeAsync(filePath);

            if (ct.IsCancellationRequested) return;

            // Fire the usage event regardless of whether the text is
            // surfaced to the user — OpenAI already billed us for the
            // audio the moment the request succeeded.
            ChunkTranscribed?.Invoke(this, durationSec);

            if (!string.IsNullOrWhiteSpace(text) &&
                !text.StartsWith("[no speech") &&
                !text.StartsWith("[recording too"))
            {
                TextReady?.Invoke(this, text);
            }

            StateChanged?.Invoke(this, "Listening");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            StateChanged?.Invoke(this, "Listening");
        }
        finally
        {
            TryDeleteFile(filePath);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try { if (path != null && File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
