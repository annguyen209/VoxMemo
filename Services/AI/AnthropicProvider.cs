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

public class AnthropicProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string ProviderName => "Anthropic";

    public AnthropicProvider(string apiKey, string baseUrl = "https://api.anthropic.com", TimeSpan? timeout = null)
    {
        _baseUrl = baseUrl;
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            Log.Debug("Anthropic: Fetching available models from {BaseUrl}", _baseUrl);
            var response = await _http.GetAsync($"{_baseUrl}/v1/models", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var models = new List<AiModel>();

                foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? "";
                    var name = item.TryGetProperty("display_name", out var dn)
                        ? dn.GetString() ?? id : id;
                    models.Add(new AiModel(id, name));
                }

                if (models.Count > 0)
                {
                    Log.Information("Anthropic: Found {Count} available models", models.Count);
                    return models;
                }
            }
            else
            {
                Log.Warning("Anthropic: GetAvailableModels returned {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Anthropic: HTTP error fetching models");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Anthropic: Error fetching available models");
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

        Log.Information("Anthropic: Starting summarization - model={Model} promptType={PromptType} transcriptChars={Chars}", 
            model, promptType, transcriptLength);

        var request = new
        {
            model,
            max_tokens = 4096,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = $"Transcript:\n{transcript}" }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/v1/messages", content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var preview = responseJson.Length > 200 ? responseJson[..200] : responseJson;
            Log.Error("Anthropic: Summarization failed with {StatusCode}: {Error}", response.StatusCode, preview);
            throw new HttpRequestException($"Anthropic returned {(int)response.StatusCode}: {preview}");
        }

        string result;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("content", out var contentArray) ||
                contentArray.GetArrayLength() == 0)
            {
                Log.Error("Anthropic: Invalid response - no content in response");
                throw new InvalidOperationException("Anthropic response has no content");
            }

            if (!contentArray[0].TryGetProperty("text", out var textElement))
            {
                Log.Error("Anthropic: Invalid response structure - missing text property");
                throw new InvalidOperationException("Anthropic response missing text property");
            }

            result = textElement.GetString() ?? string.Empty;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Anthropic: Failed to parse JSON response");
            throw new InvalidOperationException("Anthropic returned invalid JSON", ex);
        }
        catch (Exception ex) when (ex is not HttpRequestException && ex is not InvalidOperationException)
        {
            Log.Error(ex, "Anthropic: Error parsing response");
            throw;
        }

        Log.Information("Anthropic: Summarization completed - {Length} chars", result.Length);
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
            max_tokens = 4096,
            system = systemPrompt,
            stream = true,
            messages = new[]
            {
                new { role = "user", content = $"Transcript:\n{transcript}" }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages")
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

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var type) &&
                type.GetString() == "content_block_delta" &&
                root.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("text", out var text))
            {
                var token = text.GetString();
                if (token != null)
                    yield return token;
            }
        }
    }
}
