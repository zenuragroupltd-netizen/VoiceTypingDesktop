using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VoiceTypingDesktop.Models;

namespace VoiceTypingDesktop.Services;

public enum UpdateDecision
{
    /// <summary>App is up-to-date. Proceed normally.</summary>
    Ok,
    /// <summary>Newer version available, but user can still use the app.</summary>
    OptionalUpdate,
    /// <summary>User must update before continuing.</summary>
    ForceUpdate,
    /// <summary>Version check failed (network, table missing). Let app run.</summary>
    Unknown
}

public sealed class UpdateCheckResult
{
    public UpdateDecision Decision { get; init; }
    public string CurrentVersion { get; init; } = "0.0.0";
    public string LatestVersion  { get; init; } = "0.0.0";
    public string DownloadUrl    { get; init; } = string.Empty;
    public string ReleaseNotes   { get; init; } = string.Empty;
}

/// <summary>
/// Checks the Supabase <c>app_versions</c> table at startup and tells the
/// caller whether to block the app, offer an optional update, or proceed.
///
/// The desktop version is read from the executing assembly — matches the
/// &lt;Version&gt; element in the csproj.
/// </summary>
public sealed class VersionCheckService
{
    private readonly SupabaseClient _supabase;

    public VersionCheckService(SupabaseClient supabase)
    {
        _supabase = supabase;
    }

    public static string GetCurrentVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version
                ?? new Version(1, 0, 0);
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Fetches the latest version row and compares against the current one.
    /// Never throws: on any error returns <see cref="UpdateDecision.Unknown"/>
    /// so a flaky network can't brick the app.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersion();

        try
        {
            var rows = await _supabase.SelectAsync<AppVersionRecord>(
                "app_versions",
                "select=*&id=eq.desktop&limit=1",
                ct);

            if (rows.Count == 0)
            {
                return new UpdateCheckResult
                {
                    Decision = UpdateDecision.Unknown,
                    CurrentVersion = current
                };
            }

            var row = rows[0];

            var cmpMin = CompareVersions(current, row.MinimumVersion);
            var cmpLatest = CompareVersions(current, row.LatestVersion);

            UpdateDecision decision;
            if (row.ForceUpdate || cmpMin < 0)
                decision = UpdateDecision.ForceUpdate;
            else if (cmpLatest < 0)
                decision = UpdateDecision.OptionalUpdate;
            else
                decision = UpdateDecision.Ok;

            return new UpdateCheckResult
            {
                Decision       = decision,
                CurrentVersion = current,
                LatestVersion  = row.LatestVersion,
                DownloadUrl    = row.DownloadUrl ?? string.Empty,
                ReleaseNotes   = row.ReleaseNotes ?? string.Empty
            };
        }
        catch
        {
            return new UpdateCheckResult
            {
                Decision = UpdateDecision.Unknown,
                CurrentVersion = current
            };
        }
    }

    /// <summary>
    /// Compares two version strings like "1.2.3" component-by-component.
    /// Returns -1 if a &lt; b, 0 if equal, 1 if a &gt; b.
    /// Missing components are treated as 0 (so "1.2" == "1.2.0").
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        int[] pa = Parse(a), pb = Parse(b);
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int ai = i < pa.Length ? pa[i] : 0;
            int bi = i < pb.Length ? pb[i] : 0;
            if (ai != bi) return ai.CompareTo(bi);
        }
        return 0;

        static int[] Parse(string s) =>
            (s ?? "").Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .ToArray();
    }
}
