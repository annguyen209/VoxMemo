using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoxMemo.Services.AI;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public string ProviderName => "OpenAI";

    public OpenAiProvider(string apiKey, string baseUrl = "https://api.openai.com/v1")
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // Try fetching models from the API (works with LM Studio, vLLM, LocalAI, etc.)
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/models", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var models = new List<AiModel>();

                foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? "";
                    models.Add(new AiModel(id, id));
                }

                if (models.Count > 0) return models;
            }
        }
        catch { }

        return [];
    }

    public async Task<string> SummarizeAsync(
        string transcript,
        string promptType,
        string language,
        string model,
        CancellationToken ct = default)
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
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
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
            temperature = 0.3,
            stream = true
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
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
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var delta = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta");

            if (delta.TryGetProperty("content", out var tokenElement))
            {
                var token = tokenElement.GetString();
                if (token != null)
                    yield return token;
            }
        }
    }
}
