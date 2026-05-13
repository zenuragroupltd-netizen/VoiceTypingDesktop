using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VoiceTypingDesktop.ViewModels;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Persists translation history to a JSON file next to the exe.
/// Capped at 200 entries (newest first). Simple, dependency-free.
/// </summary>
public static class TranslationHistoryService
{
    private const int MaxItems = 200;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(System.AppContext.BaseDirectory, "translation-history.json");

    public static List<TranslationHistoryItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<TranslationHistoryItem>>(json, JsonOpts)
                   ?? new();
        }
        catch { return new(); }
    }

    public static void Save(IEnumerable<TranslationHistoryItem> items)
    {
        try
        {
            var capped = items.Take(MaxItems).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(capped, JsonOpts));
        }
        catch { /* best-effort */ }
    }
}
