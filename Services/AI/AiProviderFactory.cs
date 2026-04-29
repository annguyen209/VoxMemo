using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoxMemo.Services.Database;
using VoxMemo.Services.Security;
using VoxMemo.Models;

namespace VoxMemo.Services.AI;

public static class AiProviderFactory
{
    private static readonly string[] SensitiveKeys = 
    { 
        "openai_api_key", 
        "anthropic_api_key",
        "ollama_api_key"
    };

    public static async Task<(IAiProvider provider, string? modelId)> CreateFromSettingsAsync()
    {
        string providerName = "Ollama";
        string ollamaUrl = "http://localhost:11434";
        string openAiKey = "";
        string openAiBaseUrl = "https://api.openai.com/v1";
        string anthropicKey = "";
        int timeoutMinutes = 15;

        try
        {
            await using var db = AppDbContextFactory.Create();

            // Load provider name
            var providerSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "ai_provider");
            providerName = providerSetting?.Value ?? "Ollama";
            
            // Load Ollama URL
            var ollamaUrlSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "ollama_url");
            ollamaUrl = ollamaUrlSetting?.Value ?? ollamaUrl;
            
            // Load and decrypt API keys
            var openAiKeySetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "openai_api_key");
            if (openAiKeySetting != null)
            {
                openAiKey = DecryptSettingValue(openAiKeySetting) ?? "";
                if (!string.IsNullOrEmpty(openAiKey) && string.IsNullOrEmpty(openAiKeySetting.Value))
                {
                    Log.Information("AIProviderFactory: Loaded encrypted OpenAI API key");
                }
            }
            
            var openAiBaseUrlSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "openai_base_url");
            openAiBaseUrl = openAiBaseUrlSetting?.Value ?? openAiBaseUrl;
            
            var anthropicKeySetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "anthropic_api_key");
            if (anthropicKeySetting != null)
            {
                anthropicKey = DecryptSettingValue(anthropicKeySetting) ?? "";
                if (!string.IsNullOrEmpty(anthropicKey) && string.IsNullOrEmpty(anthropicKeySetting.Value))
                {
                    Log.Information("AIProviderFactory: Loaded encrypted Anthropic API key");
                }
            }
            
            var timeoutSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "ai_timeout_minutes");
            if (int.TryParse(timeoutSetting?.Value, out var t))
                timeoutMinutes = t;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AIProviderFactory: Error loading settings from database");
        }

        Log.Information("AIProviderFactory: Creating {Provider} provider", providerName);

        var timeout = TimeSpan.FromMinutes(timeoutMinutes);

        IAiProvider provider = providerName switch
        {
            "OpenAI" => new OpenAiProvider(openAiKey, openAiBaseUrl, timeout),
            "Anthropic" when !string.IsNullOrEmpty(anthropicKey) => new AnthropicProvider(anthropicKey, timeout: timeout),
            "Anthropic" => new AnthropicProvider("", timeout: timeout),
            _ => new OllamaProvider(ollamaUrl, timeout)
        };

        // Read user's selected model, fall back to first available
        string? modelId = null;
        try
        {
            await using var db2 = AppDbContextFactory.Create();
            modelId = (await db2.AppSettings.FirstOrDefaultAsync(s => s.Key == "ai_model"))?.Value;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AIProviderFactory: Error loading saved model setting");
        }

        // Verify the saved model still exists, otherwise pick first
        if (!string.IsNullOrEmpty(modelId))
        {
            try
            {
                var models = await provider.GetAvailableModelsAsync();
                if (!models.Exists(m => m.Id == modelId))
                {
                    modelId = models.Count > 0 ? models[0].Id : null;
                    Log.Warning("AIProviderFactory: Saved model {ModelId} no longer available, using {NewModel}", modelId, modelId ?? "none");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AIProviderFactory: Error verifying model availability");
            }
        }
        else
        {
            try
            {
                var models = await provider.GetAvailableModelsAsync();
                if (models.Count > 0)
                    modelId = models[0].Id;
                else
                    Log.Warning("AIProviderFactory: No models available from {Provider}", providerName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AIProviderFactory: Error fetching available models");
            }
        }

        Log.Information("AIProviderFactory: Selected model {ModelId}", modelId ?? "none");
        return (provider, modelId);
    }

    private static string? DecryptSettingValue(AppSettings setting)
    {
        // Try EncryptedValue first (new encrypted format)
        if (!string.IsNullOrEmpty(setting.EncryptedValue))
        {
            try
            {
                var decrypted = SecureStorage.Decrypt(setting.EncryptedValue);
                if (!string.IsNullOrEmpty(decrypted))
                    return decrypted;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SecureStorage: Error decrypting setting {Key}", setting.Key);
            }
        }
        
        // Fall back to plain Value (legacy format)
        return setting.Value;
    }

    public static async Task SaveApiKeyAsync(string keyName, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return;

        if (!SensitiveKeys.Contains(keyName))
            return;

        try
        {
            var encrypted = SecureStorage.Encrypt(apiKey);
            await using var db = AppDbContextFactory.Create();
            var setting = await db.AppSettings.FindAsync(keyName);
            if (setting == null)
            {
                setting = new AppSettings { Key = keyName };
                db.AppSettings.Add(setting);
            }
            setting.EncryptedValue = encrypted;
            setting.Value = ""; // Clear plaintext
            await db.SaveChangesAsync();
            Log.Information("SecureStorage: Saved encrypted API key for {KeyName}", keyName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage: Error saving API key for {KeyName}", keyName);
        }
    }
}
