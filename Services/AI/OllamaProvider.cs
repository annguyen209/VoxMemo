using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace VoxMemo.Services.AI;

public class OllamaProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string ProviderName => "Ollama";

    public OllamaProvider(string baseUrl = "http://localhost:11434", TimeSpan? timeout = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(15) };
    }

    public async Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/tags", ct);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var models = new List<AiModel>();

            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var item in modelsArray.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    models.Add(new AiModel(name, name));
                }
            }

            return models;
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> SummarizeAsync(
        string transcript,
        string promptType,
        string language,
        string model,
        CancellationToken ct = default)
    {
        var systemPrompt = PromptTemplates.GetSystemPrompt(promptType, language);

        Log.Debug("Ollama SummarizeAsync: model={Model} promptType={PromptType}", model, promptType);

        var request = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Transcript:\n{transcript}" }
            },
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/api/chat", content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var preview = responseJson.Length > 200 ? responseJson[..200] : responseJson;
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {preview}");
        }

        using var doc = JsonDocument.Parse(responseJson);

        var result = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        Log.Debug("Ollama SummarizeAsync completed: {Length} chars", result.Length);
        return result;
    }

    public async IAsyncEnumerable<string> SummarizeStreamAsync(
        string transcript,
        string promptType,
        string language,
        string model,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemPrompt = PromptTemplates.GetSystemPrompt(promptType, language);

        var request = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Transcript:\n{transcript}" }
            },
            stream = true
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = content
        };

        var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var tokenElement))
            {
                var token = tokenElement.GetString();
                if (!string.IsNullOrEmpty(token))
                    yield return token;
            }
        }
    }
}
