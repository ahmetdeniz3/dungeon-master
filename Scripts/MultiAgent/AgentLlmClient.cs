using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Lightweight Ollama chat helper shared by specialized DM and NPC agents.</summary>
public static class AgentLlmClient
{
    private const string OllamaUrl = "http://127.0.0.1:11434/api/chat";
    private const string TagsUrl = "http://127.0.0.1:11434/api/tags";

    private static readonly System.Net.Http.HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private static string _cachedModel = null;
    private static DateTime _lastProbeTime = DateTime.MinValue;

    /// <summary>
    /// Returns gemma4:12b (or gemma3:12b) if downloaded, otherwise falls back gracefully to qwen3:8b or gemma3:4b.
    /// </summary>
    public static async Task<string> GetPreferredModelAsync(HttpClient client = null)
    {
        if (_cachedModel != null && (DateTime.UtcNow - _lastProbeTime).TotalSeconds < 20)
            return _cachedModel;

        HttpClient c = client ?? _http;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await c.GetAsync(TagsUrl, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                string tagsJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(tagsJson);
                if (doc.RootElement.TryGetProperty("models", out JsonElement modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
                {
                    var names = new List<string>();
                    foreach (var m in modelsEl.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out JsonElement n))
                            names.Add(n.GetString() ?? "");
                    }

                    if (names.Any(n => n.StartsWith("gemma4:12b", StringComparison.OrdinalIgnoreCase)))
                        _cachedModel = "gemma4:12b";
                    else if (names.Any(n => n.StartsWith("gemma3:12b", StringComparison.OrdinalIgnoreCase)))
                        _cachedModel = "gemma3:12b";
                    else if (names.Any(n => n.StartsWith("qwen3:8b", StringComparison.OrdinalIgnoreCase)))
                        _cachedModel = "qwen3:8b";
                    else if (names.Any(n => n.StartsWith("gemma3:4b", StringComparison.OrdinalIgnoreCase)))
                        _cachedModel = "gemma3:4b";
                    else
                        _cachedModel = "gemma4:12b";

                    _lastProbeTime = DateTime.UtcNow;
                    return _cachedModel;
                }
            }
        }
        catch
        {
        }

        _cachedModel = "gemma4:12b";
        _lastProbeTime = DateTime.UtcNow;
        return _cachedModel;
    }

    public static async Task<string> CompleteShortAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken ct)
    {
        try
        {
            string chosenModel = await GetPreferredModelAsync(_http).ConfigureAwait(false);

            var payload = new
            {
                model = chosenModel,
                think = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                keep_alive = "10m",
                options = new
                {
                    temperature,
                    top_p = 0.9,
                    repeat_penalty = 1.18,
                    num_ctx = 2048,
                    num_predict = maxTokens
                }
            };

            string json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(20));

            using HttpResponseMessage res = await _http.SendAsync(req, linkedCts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return "";

            string body = await res.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("message", out JsonElement msgEl) ||
                !msgEl.TryGetProperty("content", out JsonElement contentEl))
                return "";

            string raw = (contentEl.GetString() ?? "").Trim();
            raw = Regex.Replace(raw, @"\[[^\]]*\]", "").Trim();
            raw = Regex.Replace(raw, @"\s+", " ").Trim();
            return raw;
        }
        catch
        {
            return "";
        }
    }
}
