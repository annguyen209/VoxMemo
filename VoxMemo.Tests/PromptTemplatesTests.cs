using VoxMemo.Services.AI;

namespace VoxMemo.Tests;

public class PromptTemplatesTests
{
    [Theory]
    [InlineData("meeting_summary", "en")]
    [InlineData("meeting_summary", "vi")]
    [InlineData("identify_speakers", "en")]
    [InlineData("action_items", "en")]
    [InlineData("key_decisions", "en")]
    [InlineData("meeting_notes", "en")]
    [InlineData("auto_title", "en")]
    public void GetSystemPrompt_ReturnsNonEmptyForAllTypes(string promptType, string language)
    {
        var result = PromptTemplates.GetSystemPrompt(promptType, language);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void GetSystemPrompt_EnglishContainsEnglishInstruction()
    {
        var result = PromptTemplates.GetSystemPrompt("meeting_summary", "en");
        Assert.Contains("English", result);
        Assert.DoesNotContain("Vietnamese", result);
    }

    [Fact]
    public void GetSystemPrompt_VietnameseContainsVietnameseInstruction()
    {
        var result = PromptTemplates.GetSystemPrompt("meeting_summary", "vi");
        Assert.Contains("Vietnamese", result);
    }

    [Fact]
    public void GetSystemPrompt_SummaryContainsSpeakerAwareness()
    {
        var result = PromptTemplates.GetSystemPrompt("meeting_summary", "en");
        Assert.Contains("speaker", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSystemPrompt_IdentifySpeakersContainsDialogInstructions()
    {
        var result = PromptTemplates.GetSystemPrompt("identify_speakers", "en");
        Assert.Contains("dialog", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Speaker", result);
    }

    [Fact]
    public void GetSystemPrompt_AutoTitleIsShort()
    {
        var result = PromptTemplates.GetSystemPrompt("auto_title", "en");
        Assert.Contains("title", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSystemPrompt_SummaryContainsPlainTextInstruction()
    {
        var result = PromptTemplates.GetSystemPrompt("meeting_summary", "en");
        Assert.Contains("plain text", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSystemPrompt_UnknownType_ReturnsFallback()
    {
        var result = PromptTemplates.GetSystemPrompt("unknown_type", "en");
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("meeting assistant", result, StringComparison.OrdinalIgnoreCase);
    }
}
