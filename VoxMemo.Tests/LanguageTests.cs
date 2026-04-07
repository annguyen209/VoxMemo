using VoxMemo.Models;

namespace VoxMemo.Tests;

public class LanguageTests
{
    [Fact]
    public void WhisperLanguages_ContainsEnglish()
    {
        Assert.Contains(WhisperLanguages.All, l => l.Code == "en");
    }

    [Fact]
    public void WhisperLanguages_ContainsVietnamese()
    {
        Assert.Contains(WhisperLanguages.All, l => l.Code == "vi");
    }

    [Fact]
    public void WhisperLanguages_HasAtLeast20Languages()
    {
        Assert.True(WhisperLanguages.All.Count >= 20);
    }

    [Fact]
    public void WhisperLanguages_NoDuplicateCodes()
    {
        var codes = WhisperLanguages.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void WhisperLanguages_AllHaveNonEmptyCodeAndName()
    {
        foreach (var lang in WhisperLanguages.All)
        {
            Assert.False(string.IsNullOrEmpty(lang.Code), $"Empty code found");
            Assert.False(string.IsNullOrEmpty(lang.Name), $"Empty name for code {lang.Code}");
        }
    }

    [Fact]
    public void LanguageItem_ToStringShowsNameAndCode()
    {
        var item = new LanguageItem("vi", "Vietnamese");
        Assert.Equal("Vietnamese (vi)", item.ToString());
    }

    [Theory]
    [InlineData("zh", "Chinese")]
    [InlineData("ja", "Japanese")]
    [InlineData("ko", "Korean")]
    [InlineData("fr", "French")]
    [InlineData("de", "German")]
    [InlineData("es", "Spanish")]
    public void WhisperLanguages_ContainsCommonLanguages(string code, string name)
    {
        var lang = WhisperLanguages.All.FirstOrDefault(l => l.Code == code);
        Assert.NotNull(lang);
        Assert.Equal(name, lang.Name);
    }
}
