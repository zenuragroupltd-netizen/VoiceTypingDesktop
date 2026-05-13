using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceTypingDesktop.Config;
using VoiceTypingDesktop.Models;
using VoiceTypingDesktop.ViewModels;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Polls Supabase for the session(s) bound to this desktop device and
/// streams their <c>voice_texts</c> rows. Multiple mobiles can pair with
/// the same desktop at once — every active session inside the 24 h window
/// is polled together in a single request.
///
/// Lightweight polling (no WebSocket) keeps the app dependency-free.
/// </summary>
public class MobileSyncService
{
    private readonly SupabaseClient _supabase;
    private readonly AppConfig _config;
    private readonly string _desktopDeviceId;

    // ---- Cadence ----
    // Fast poll when we already have a session bound: users expect text to
    // appear "instantly". Slower session discovery cadence so we don't spam
    // PostgREST when nothing is happening.
    private static readonly TimeSpan TextPollInterval        = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan DiscoveryPollInterval   = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiscoveryRefreshWhenBound = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Initial lookback window: 24 h. We dedupe via _seenTextIds, so this just
    // controls how far back we are willing to scan on a fresh launch. Once
    // the cursor advances, older texts are not re-fetched.
    private DateTime _lastTextAtUtc = DateTime.UtcNow.AddHours(-24);
    private readonly HashSet<string> _seenTextIds = new();

    // All session ids we are currently streaming from. One entry per mobile
    // paired with this desktop within the 24 h window. Updated by the
    // discovery pass.
    private readonly HashSet<string> _activeSessionIds = new();
    private DateTime _lastDiscoveryAtUtc = DateTime.MinValue;

    public event EventHandler<string>? SessionStarted;
    public event EventHandler<VoiceTextRecord>? TextReceived;
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Primary session id (the first one we bound to). Kept for backwards
    /// compatibility — the config stores a single id and downstream UI shows
    /// one. Multi-session data still flows through <see cref="TextReceived"/>.
    /// </summary>
    public string? CurrentSessionId { get; private set; }

    /// <summary>
    /// Snapshot of every session id this desktop is currently receiving
    /// from. Handy for the diagnostic line in the UI.
    /// </summary>
    public IReadOnlyCollection<string> ActiveSessionIds
    {
        get { lock (_activeSessionIds) return _activeSessionIds.ToArray(); }
    }

    /// <summary>Total number of poll iterations completed since Start().</summary>
    public int PollCount { get; private set; }

    /// <summary>Total number of texts delivered to subscribers since Start().</summary>
    public int TextsReceivedTotal { get; private set; }

    /// <summary>Timestamp of the most recently received text (UTC), or null.</summary>
    public DateTime? LastTextAtUtc { get; private set; }

    /// <summary>Last raw error message, useful for diagnostics in the UI.</summary>
    public string? LastError { get; private set; }

    public MobileSyncService(SupabaseClient supabase, AppConfig config, string desktopDeviceId)
    {
        _supabase = supabase;
        _config = config;
        _desktopDeviceId = desktopDeviceId;

        // Resume a saved session if it's still within its 24 h validity window.
        // Keeps the user "connected" across desktop restarts — no re-pair
        // unless the window actually elapsed.
        if (_config.HasValidPairing && !string.IsNullOrWhiteSpace(_config.CurrentSessionId))
        {
            CurrentSessionId = _config.CurrentSessionId;
            lock (_activeSessionIds) _activeSessionIds.Add(_config.CurrentSessionId);
        }
    }

    /// <summary>
    /// Call before Start() to seed already-known items from persisted history.
    /// Prevents re-delivering the same rows after app restart.
    /// </summary>
    public void SeedFromHistory(IEnumerable<ReceivedTextItem> items)
    {
        foreach (var it in items)
        {
            if (!string.IsNullOrEmpty(it.Id)) _seenTextIds.Add(it.Id);
            var createdUtc = it.CreatedAt.Kind == DateTimeKind.Utc
                ? it.CreatedAt
                : it.CreatedAt.ToUniversalTime();
            if (createdUtc > _lastTextAtUtc) _lastTextAtUtc = createdUtc;
        }
    }

    public void Start()
    {
        if (_loopTask != null && !_loopTask.IsCompleted) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try { if (_loopTask != null) await _loopTask; }
        catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    // ------------------------------------------------------------------
    // Main loop
    // ------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (HasAnyBound)
            RaiseStatus(BuildPairedStatus());
        else
            RaiseStatus("Waiting for pairing...");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Expire binding if the user's 24 h window has elapsed.
                if (HasAnyBound && !_config.HasValidPairing)
                {
                    lock (_activeSessionIds) _activeSessionIds.Clear();
                    CurrentSessionId = null;
                    RaiseStatus("Pairing expired. Waiting for re-pair...");
                }

                // Refresh the active session set:
                //   - always, if we are unbound (fast path to first pair)
                //   - every DiscoveryRefreshWhenBound, if already bound
                //     (to pick up a second mobile that pairs later)
                var sinceDiscovery = DateTime.UtcNow - _lastDiscoveryAtUtc;
                if (!HasAnyBound || sinceDiscovery >= DiscoveryRefreshWhenBound)
                {
                    await RefreshActiveSessionsAsync(ct);
                    _lastDiscoveryAtUtc = DateTime.UtcNow;
                }

                if (HasAnyBound)
                    await PollVoiceTextsAsync(ct);

                LastError = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RaiseStatus($"Sync error: {ex.Message}");
            }

            PollCount++;
            var delay = HasAnyBound ? TextPollInterval : DiscoveryPollInterval;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool HasAnyBound
    {
        get { lock (_activeSessionIds) return _activeSessionIds.Count > 0; }
    }

    // ------------------------------------------------------------------
    // Discovery: find all sessions bound to this desktop inside the window
    // ------------------------------------------------------------------

    private async Task RefreshActiveSessionsAsync(CancellationToken ct)
    {
        // Look back at the 24 h window so we don't miss a mobile that paired
        // an hour ago. We keep the result unfiltered by `status` and skip
        // only rows explicitly ended. Different mobile builds use different
        // status vocabularies — a too-strict filter silently drops texts.
        var since = DateTime.UtcNow.AddHours(-24).ToString("o");
        var query =
            $"select=*" +
            $"&desktop_device_id=eq.{Uri.EscapeDataString(_desktopDeviceId)}" +
            $"&created_at=gte.{Uri.EscapeDataString(since)}" +
            $"&order=created_at.desc" +
            $"&limit=20";

        var sessions = await _supabase.SelectAsync<SessionRecord>("sessions", query, ct);
        if (sessions.Count == 0) return;

        // Filter explicitly-ended sessions. Everything else is eligible —
        // including rows with a null or unknown status flag.
        var eligible = new List<string>();
        foreach (var s in sessions)
        {
            var status = s.Status?.Trim().ToLowerInvariant();
            if (status == "ended" || status == "closed" || status == "inactive")
                continue;

            var sid = s.EffectiveId;
            if (string.IsNullOrWhiteSpace(sid)) continue;
            eligible.Add(sid);
        }
        if (eligible.Count == 0) return;

        // Merge into the active set. Fire SessionStarted for each NEW session
        // so the UI can surface that a second mobile joined.
        var newlyJoined = new List<string>();
        lock (_activeSessionIds)
        {
            foreach (var sid in eligible)
            {
                if (_activeSessionIds.Add(sid))
                    newlyJoined.Add(sid);
            }
        }

        if (newlyJoined.Count > 0)
        {
            // Primary session = most recent row (list is already desc).
            CurrentSessionId = eligible[0];
            _config.CurrentSessionId = CurrentSessionId;

            // Extend the validity window from the moment we bind.
            _config.PairedUntilUtc = (DateTime.UtcNow + DeviceService.PairValidity)
                .ToString("o");
            _config.Save();

            foreach (var sid in newlyJoined)
                SessionStarted?.Invoke(this, sid);

            RaiseStatus(BuildPairedStatus());
        }
    }

    // ------------------------------------------------------------------
    // Text polling across every bound session
    // ------------------------------------------------------------------

    private async Task PollVoiceTextsAsync(CancellationToken ct)
    {
        string[] sids;
        lock (_activeSessionIds) sids = _activeSessionIds.ToArray();
        if (sids.Length == 0) return;

        var sinceUtc = _lastTextAtUtc.Kind == DateTimeKind.Utc
            ? _lastTextAtUtc
            : _lastTextAtUtc.ToUniversalTime();
        var since = sinceUtc.ToString("o");

        // PostgREST: session_id=in.("a","b","c")  (values are quoted so UUIDs
        // with dashes don't need further escaping).
        var inList = string.Join(",", sids.Select(s => "\"" + s + "\""));
        var query =
            $"select=id,session_id,text,created_at" +
            $"&session_id=in.({Uri.EscapeDataString(inList)})" +
            $"&created_at=gte.{Uri.EscapeDataString(since)}" +
            $"&order=created_at.asc" +
            $"&limit=100";

        var rows = await _supabase.SelectAsync<VoiceTextRecord>("voice_texts", query, ct);
        var newCount = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Id) || _seenTextIds.Contains(row.Id)) continue;
            _seenTextIds.Add(row.Id);
            if (row.CreatedAt.HasValue)
            {
                var rowUtc = row.CreatedAt.Value.Kind == DateTimeKind.Utc
                    ? row.CreatedAt.Value
                    : row.CreatedAt.Value.ToUniversalTime();
                if (rowUtc > _lastTextAtUtc) _lastTextAtUtc = rowUtc;
                LastTextAtUtc = rowUtc;
            }
            TextsReceivedTotal++;
            newCount++;
            TextReceived?.Invoke(this, row);
        }
        if (newCount > 0)
            RaiseStatus($"Received {newCount} new text{(newCount == 1 ? "" : "s")}.");
    }

    // ------------------------------------------------------------------
    // Public actions
    // ------------------------------------------------------------------

    /// <summary>
    /// Forgets all current sessions so the next poll re-discovers sessions.
    /// Used by the "Re-pair" UI action.
    /// </summary>
    public void ResetSession()
    {
        lock (_activeSessionIds) _activeSessionIds.Clear();
        CurrentSessionId = null;
        _config.CurrentSessionId = string.Empty;
        _config.PairedUntilUtc   = string.Empty;
        _lastDiscoveryAtUtc = DateTime.MinValue;
        _config.Save();
        RaiseStatus("Session reset. Re-pair from your phone.");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string BuildPairedStatus()
    {
        int count;
        lock (_activeSessionIds) count = _activeSessionIds.Count;
        return count <= 1
            ? "Paired. Receiving from your mobile."
            : $"Paired with {count} mobiles.";
    }

    private void RaiseStatus(string s) => StatusChanged?.Invoke(this, s);
}
