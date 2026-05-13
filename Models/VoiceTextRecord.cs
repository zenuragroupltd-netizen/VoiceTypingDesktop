using System;
using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.Models;

public class VoiceTextRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
