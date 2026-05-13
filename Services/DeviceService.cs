using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VoiceTypingDesktop.Config;
using VoiceTypingDesktop.Models;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Manages the desktop device identity and pair code, and registers with Supabase.
///
/// Pair codes are persisted for 24 hours. A user who has paired their phone
/// once does not need to scan again during that window, even across desktop
/// restarts. After the window expires (or they hit Re-pair) a fresh code is
/// minted and written back to Supabase.
/// </summary>
public class DeviceService
{
    private readonly SupabaseClient _supabase;
    private readonly AppConfig _config;

    /// <summary>Pair code + session validity window (24h by design).</summary>
    public static readonly TimeSpan PairValidity = TimeSpan.FromHours(24);

    public DeviceService(SupabaseClient supabase, AppConfig config)
    {
        _supabase = supabase;
        _config = config;
    }

    public string DeviceId { get; private set; } = string.Empty;
    public string PairCode { get; private set; } = string.Empty;
    public DateTime? PairedUntilUtc { get; private set; }

    /// <summary>
    /// Generates or loads desktop_device_id; reuses the saved pair code if
    /// it is still within its 24h window, otherwise mints a new one. Either
    /// way, the device row is upserted in Supabase so the mobile app can
    /// always find it.
    /// </summary>
    public async Task RegisterAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.DesktopDeviceId))
        {
            _config.DesktopDeviceId = Guid.NewGuid().ToString();
            _config.Save();
        }
        DeviceId = _config.DesktopDeviceId;

        // Reuse the existing pair code if it has not expired.
        if (_config.HasValidPairCode)
        {
            PairCode = _config.CurrentPairCode;
            PairedUntilUtc = _config.PairedUntilUtcParsed;
        }
        else
        {
            MintNewPairCode();
        }

        var body = new
        {
            device_id = DeviceId,
            device_type = "desktop",
            pair_code = PairCode,
            is_connected = false
        };

        await _supabase.UpsertAsync<DeviceRecord>("devices", "device_id", body, ct);
    }

    /// <summary>
    /// Forces a fresh pair code + validity window and pushes it to Supabase.
    /// Called by the "Re-pair" button and by auto-expiry.
    /// </summary>
    public async Task ForceNewPairCodeAsync(CancellationToken ct = default)
    {
        MintNewPairCode();

        var body = new
        {
            device_id = DeviceId,
            device_type = "desktop",
            pair_code = PairCode,
            is_connected = false
        };

        await _supabase.UpsertAsync<DeviceRecord>("devices", "device_id", body, ct);
    }

    private void MintNewPairCode()
    {
        PairCode = GeneratePairCode();
        PairedUntilUtc = DateTime.UtcNow + PairValidity;

        _config.CurrentPairCode = PairCode;
        _config.PairedUntilUtc  = PairedUntilUtc.Value.ToString("o");
        // A new pair code invalidates any previously bound session.
        _config.CurrentSessionId = string.Empty;
        _config.Save();
    }

    private static string GeneratePairCode()
    {
        // 6 chars, uppercase + digits, no ambiguous chars (0/O, 1/I/L).
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        Span<char> result = stackalloc char[6];
        for (int i = 0; i < 6; i++)
            result[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(result);
    }
}
