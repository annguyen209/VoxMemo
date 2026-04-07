using VoxMemo.Services.AI;

namespace VoxMemo.Tests;

public class OllamaProviderTests
{
    [Fact]
    public void ProviderName_IsOllama()
    {
        var provider = new OllamaProvider();
        Assert.Equal("Ollama", provider.ProviderName);
    }

    [Fact]
    public void Constructor_DefaultUrl()
    {
        // Should not throw
        var provider = new OllamaProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_CustomUrl()
    {
        var provider = new OllamaProvider("http://custom:1234");
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task GetAvailableModels_ReturnsEmptyWhenOffline()
    {
        // Point to a non-existent server
        var provider = new OllamaProvider("http://localhost:99999");
        var models = await provider.GetAvailableModelsAsync();
        Assert.NotNull(models);
        Assert.Empty(models);
    }
}

public class OpenAiProviderTests
{
    [Fact]
    public void ProviderName_IsOpenAI()
    {
        var provider = new OpenAiProvider("test-key");
        Assert.Equal("OpenAI", provider.ProviderName);
    }

    [Fact]
    public void Constructor_AllowsEmptyKey()
    {
        // For local servers like LM Studio
        var provider = new OpenAiProvider("", "http://localhost:1234/v1");
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task GetAvailableModels_ReturnsEmptyWhenOffline()
    {
        var provider = new OpenAiProvider("fake", "http://localhost:99999/v1");
        var models = await provider.GetAvailableModelsAsync();
        Assert.NotNull(models);
        Assert.Empty(models);
    }
}

public class AnthropicProviderTests
{
    [Fact]
    public void ProviderName_IsAnthropic()
    {
        var provider = new AnthropicProvider("test-key");
        Assert.Equal("Anthropic", provider.ProviderName);
    }

    [Fact]
    public async Task GetAvailableModels_ReturnsEmptyWhenOffline()
    {
        // Will fail to connect, should return empty
        var provider = new AnthropicProvider("fake-key", "http://localhost:99999");
        var models = await provider.GetAvailableModelsAsync();
        Assert.NotNull(models);
        Assert.Empty(models);
    }
}
