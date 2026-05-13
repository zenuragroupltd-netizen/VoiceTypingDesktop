using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.Models;

public class DeviceRecord
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("device_type")]
    public string DeviceType { get; set; } = string.Empty;

    [JsonPropertyName("pair_code")]
    public string? PairCode { get; set; }

    [JsonPropertyName("is_connected")]
    public bool IsConnected { get; set; }
}
