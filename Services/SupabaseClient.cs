using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTypingDesktop.Services;

/// <summary>
/// Minimal Supabase REST (PostgREST) client using HttpClient only.
/// Keeps the desktop app lightweight (no extra Supabase SDK).
/// </summary>
public class SupabaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SupabaseClient(string url, string anonKey)
    {
        _baseUrl = url.TrimEnd('/');
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("apikey", anonKey);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", anonKey);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<List<T>> SelectAsync<T>(string table, string query,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/v1/{table}?{query}";
        using var resp = await _http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {table} failed: {(int)resp.StatusCode} {body}");
        return JsonSerializer.Deserialize<List<T>>(body, JsonOpts) ?? new List<T>();
    }

    public async Task<List<T>> UpsertAsync<T>(string table, string onConflictColumn,
        object body, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/v1/{table}?on_conflict={onConflictColumn}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");
        req.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOpts),
            Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"UPSERT {table} failed: {(int)resp.StatusCode} {respBody}");
        return JsonSerializer.Deserialize<List<T>>(respBody, JsonOpts) ?? new List<T>();
    }

    public async Task<List<T>> UpdateAsync<T>(string table, string filterQuery,
        object body, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/v1/{table}?{filterQuery}";
        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Add("Prefer", "return=representation");
        req.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOpts),
            Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"UPDATE {table} failed: {(int)resp.StatusCode} {respBody}");
        return JsonSerializer.Deserialize<List<T>>(respBody, JsonOpts) ?? new List<T>();
    }

    public void Dispose() => _http.Dispose();
}
