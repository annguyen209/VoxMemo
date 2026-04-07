using VoxMemo.Models;
using VoxMemo.Services.AI;

namespace VoxMemo.Tests;

public class ModelTests
{
    [Fact]
    public void Meeting_DefaultValues()
    {
        var meeting = new Meeting();
        Assert.NotNull(meeting.Id);
        Assert.NotEmpty(meeting.Id);
        Assert.Equal(string.Empty, meeting.Title);
        Assert.Equal("en", meeting.Language);
        Assert.NotNull(meeting.Transcripts);
        Assert.NotNull(meeting.Summaries);
        Assert.Empty(meeting.Transcripts);
        Assert.Empty(meeting.Summaries);
    }

    [Fact]
    public void Meeting_IdIsUnique()
    {
        var m1 = new Meeting();
        var m2 = new Meeting();
        Assert.NotEqual(m1.Id, m2.Id);
    }

    [Fact]
    public void Transcript_DefaultValues()
    {
        var t = new Transcript();
        Assert.NotEmpty(t.Id);
        Assert.Equal("whisper-local", t.Engine);
        Assert.NotNull(t.Segments);
        Assert.Empty(t.Segments);
    }

    [Fact]
    public void Summary_DefaultValues()
    {
        var s = new Summary();
        Assert.NotEmpty(s.Id);
        Assert.Equal("meeting_summary", s.PromptType);
    }

    [Fact]
    public void AppSettings_DefaultValues()
    {
        var s = new AppSettings();
        Assert.Equal(string.Empty, s.Key);
        Assert.Equal(string.Empty, s.Value);
    }

    [Fact]
    public void AiModel_RecordEquality()
    {
        var m1 = new AiModel("gpt-4", "GPT-4");
        var m2 = new AiModel("gpt-4", "GPT-4");
        Assert.Equal(m1, m2);
    }

    [Fact]
    public void AiModel_RecordInequality()
    {
        var m1 = new AiModel("gpt-4", "GPT-4");
        var m2 = new AiModel("gpt-3.5", "GPT-3.5");
        Assert.NotEqual(m1, m2);
    }
}
