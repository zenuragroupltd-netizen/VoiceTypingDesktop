using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VoiceTypingDesktop.ViewModels;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Persists received voice texts to a local JSON file so history
/// survives app restarts.
/// File path: next to the exe (same folder as appsettings.json).
/// </summary>
public static class HistoryService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(System.AppContext.BaseDirectory, "history.json");

    public static List<ReceivedTextItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ReceivedTextItem>();
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<ReceivedTextItem>>(json, JsonOpts);
            return list ?? new List<ReceivedTextItem>();
        }
        catch
        {
            return new List<ReceivedTextItem>();
        }
    }

    public static void Save(IEnumerable<ReceivedTextItem> items)
    {
        try
        {
            var json = JsonSerializer.Serialize(items, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence; ignore write errors in v1.
        }
    }
}
