using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.Models;

/// <summary>
/// Row from the Supabase <c>app_versions</c> table. One row per app
/// (id = "desktop"). Used by <see cref="Services.VersionCheckService"/>
/// at startup to decide whether the user must update before continuing.
/// </summary>
public sealed class AppVersionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("minimum_version")]
    public string MinimumVersion { get; set; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("force_update")]
    public bool ForceUpdate { get; set; }
}
