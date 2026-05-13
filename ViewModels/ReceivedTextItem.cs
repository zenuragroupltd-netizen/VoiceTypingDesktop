using System;
using System.Text.Json.Serialization;

namespace VoiceTypingDesktop.ViewModels;

public class ReceivedTextItem
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string CreatedAtDisplay =>
        CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
