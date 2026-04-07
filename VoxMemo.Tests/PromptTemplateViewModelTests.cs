using VoxMemo.ViewModels;

namespace VoxMemo.Tests;

public class PromptTemplateViewModelTests
{
    [Fact]
    public void PromptTemplate_ToStringShowsName()
    {
        var t = new PromptTemplate("Action Items Focus", "some prompt");
        Assert.Equal("Action Items Focus", t.ToString());
    }

    [Fact]
    public void PromptTemplate_DefaultHasEmptyPrompt()
    {
        var t = new PromptTemplate("(Default - built-in)", "");
        Assert.Equal("", t.Prompt);
    }

    [Fact]
    public void PromptTemplate_RecordEquality()
    {
        var t1 = new PromptTemplate("Test", "prompt");
        var t2 = new PromptTemplate("Test", "prompt");
        Assert.Equal(t1, t2);
    }
}

public class ProcessingJobViewModelTests
{
    [Fact]
    public void NewJob_DefaultState()
    {
        var job = new ProcessingJobViewModel("meeting-1", "Test Meeting");
        Assert.Equal("meeting-1", job.MeetingId);
        Assert.Equal("Test Meeting", job.MeetingTitle);
        Assert.Equal("Queued", job.Status);
        Assert.False(job.IsActive);
        Assert.False(job.IsComplete);
        Assert.False(job.IsFailed);
        Assert.Equal(string.Empty, job.Step);
        Assert.Equal(string.Empty, job.Elapsed);
    }

    [Fact]
    public void Job_CtsCanBeCancelled()
    {
        var job = new ProcessingJobViewModel("m1", "Test");
        Assert.False(job.Cts.IsCancellationRequested);
        job.Cts.Cancel();
        Assert.True(job.Cts.IsCancellationRequested);
    }

    [Fact]
    public void Job_CreatedAtIsSet()
    {
        var before = DateTime.Now;
        var job = new ProcessingJobViewModel("m1", "Test");
        var after = DateTime.Now;
        Assert.InRange(job.CreatedAt, before, after);
    }

    [Fact]
    public void Job_MarkFinished_SetsElapsed()
    {
        var job = new ProcessingJobViewModel("m1", "Test");
        job.MarkStarted();
        Thread.Sleep(100);
        job.MarkFinished();
        Assert.False(string.IsNullOrEmpty(job.Elapsed));
    }
}
