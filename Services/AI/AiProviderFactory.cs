using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VoxMemo.Services.Database;

namespace VoxMemo.Services.AI;

public static class AiProviderFactory
{
    public static async Task<(IAiProvider provider, string? modelId)> CreateFromSettingsAsync()
    {
        string providerName = "Ollama";
        string ollamaUrl = "http://localhost:11434";
        string openAiKey = "";
        string openAiBaseUrl = "https://api.openai.com/v1";
        string anthropicKey = "";

        try
        {
            await using var db = new AppDbContext();
            providerName = (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "ai_provider"))?.Value ?? "Ollama";
            ollamaUrl = (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "ollama_url"))?.Value ?? ollamaUrl;
            openAiKey = (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "openai_api_key"))?.Value ?? "";
            openAiBaseUrl = (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "openai_base_url"))?.Value ?? openAiBaseUrl;
            anthropicKey = (await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "anthropic_api_key"))?.Value ?? "";
        }
        catch { }

        IAiProvider provider = providerName switch
        {
            "OpenAI" => new OpenAiProvider(openAiKey, openAiBaseUrl),
            "Anthropic" when !string.IsNullOrEmpty(anthropicKey) => new AnthropicProvider(anthropicKey),
            _ => new OllamaProvider(ollamaUrl)
        };

        // Try to get the first available model
        string? modelId = null;
        try
        {
            var models = await provider.GetAvailableModelsAsync();
            if (models.Count > 0)
                modelId = models[0].Id;
        }
        catch { }

        return (provider, modelId);
    }
}
