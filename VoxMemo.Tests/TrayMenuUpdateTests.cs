using System.Reflection;

namespace VoxMemo.Tests;

public class TrayMenuUpdateTests
{
    private static readonly MethodInfo ShouldRebuildTrayMenuMethod =
        typeof(App).GetMethod("ShouldRebuildTrayMenu", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData("IsRecording", true)]
    [InlineData("IsPaused", true)]
    [InlineData("SelectedAudioSource", true)]
    [InlineData("SelectedDevice", true)]
    [InlineData("SelectedLanguage", true)]
    [InlineData("ElapsedTime", false)]
    [InlineData("AudioLevel", false)]
    [InlineData("StatusMessage", false)]
    [InlineData("MeetingTitle", false)]
    [InlineData(null, false)]
    public void ShouldRebuildTrayMenu_MatchesExpectedProperties(string? propertyName, bool expected)
    {
        var result = (bool)ShouldRebuildTrayMenuMethod.Invoke(null, [propertyName])!;
        Assert.Equal(expected, result);
    }
}
