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
using Serilog;

namespace VoxMemo.Services.AI;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public string ProviderName => "OpenAI";

    public OpenAiProvider(string apiKey, string baseUrl = "https://api.openai.com/v1", TimeSpan? timeout = null)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(15) };
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            Log.Debug("OpenAI: Fetching available models from {BaseUrl}", _baseUrl);
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

                if (models.Count > 0)
                {
                    Log.Information("OpenAI: Found {Count} available models", models.Count);
                    return models;
                }
            }
            else
            {
                Log.Warning("OpenAI: GetAvailableModels returned {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "OpenAI: HTTP error fetching models");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "OpenAI: Error fetching available models");
        }

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
        var transcriptLength = transcript.Length;

        Log.Information("OpenAI: Starting summarization - model={Model} promptType={PromptType} transcriptChars={Chars}", 
            model, promptType, transcriptLength);

        var request = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Transcript:\n{transcript}" }
            },
            temperature = 0.3,
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var preview = responseJson.Length > 200 ? responseJson[..200] : responseJson;
            Log.Error("OpenAI: Summarization failed with {StatusCode}: {Error}", response.StatusCode, preview);
            throw new HttpRequestException($"API returned {(int)response.StatusCode} {response.ReasonPhrase}: {preview}");
        }

        string result;
        try
        {
            var jsonToParse = responseJson.Trim();
            if (jsonToParse.Contains('\n'))
            {
                var lines = jsonToParse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("data: ")) trimmed = trimmed["data: ".Length..];
                    if (trimmed == "[DONE]") continue;
                    if (trimmed.StartsWith('{') && trimmed.Contains("choices"))
                    {
                        jsonToParse = trimmed;
                        break;
                    }
                }
            }

            using var doc = JsonDocument.Parse(jsonToParse);

            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                Log.Error("OpenAI: Invalid response - no choices in response");
                throw new InvalidOperationException("OpenAI response has no choices");
            }

            if (!choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                Log.Error("OpenAI: Invalid response structure - missing message.content");
                throw new InvalidOperationException("OpenAI response missing message.content");
            }

            result = contentElement.GetString() ?? string.Empty;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "OpenAI: Failed to parse JSON response");
            throw new InvalidOperationException("OpenAI returned invalid JSON", ex);
        }
        catch (Exception ex) when (ex is not HttpRequestException && ex is not InvalidOperationException)
        {
            Log.Error(ex, "OpenAI: Error parsing response");
            throw;
        }

        Log.Information("OpenAI: Summarization completed - {Length} chars", result.Length);
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
