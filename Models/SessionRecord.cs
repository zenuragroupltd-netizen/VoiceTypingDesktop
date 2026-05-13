using System;
using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.Models;

public class SessionRecord
{
    /// <summary>
    /// Primary key from the Supabase row. Schemas vary: some projects use
    /// `id`, others use `session_id`. We deserialize both and the consumer
    /// picks whichever one is populated (see <see cref="EffectiveId"/>).
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("desktop_device_id")]
    public string? DesktopDeviceId { get; set; }

    [JsonPropertyName("mobile_device_id")]
    public string? MobileDeviceId { get; set; }

    /// <summary>Boolean flag used by some schemas to mark the active session.</summary>
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; set; }

    /// <summary>Text flag used by other schemas (`active` | `ended`).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Picks `session_id` if present, otherwise `id`. Matches the precedence
    /// used by the mobile client when it reads back the inserted row.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveId =>
        !string.IsNullOrWhiteSpace(SessionId) ? SessionId : Id;
}
