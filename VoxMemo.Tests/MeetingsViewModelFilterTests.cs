using System;
using System.Collections.Generic;
using VoxMemo.Models;
using VoxMemo.ViewModels;

namespace VoxMemo.Tests;

public class MeetingsViewModelFilterTests
{
    private static MeetingItemViewModel MakeVm(
        string title, string transcript = "", string summary = "")
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Language = "en",
            StartedAt = DateTime.UtcNow,
        };
        if (!string.IsNullOrEmpty(transcript))
            meeting.Transcripts.Add(new Transcript { MeetingId = meeting.Id, FullText = transcript, Language = "en" });
        if (!string.IsNullOrEmpty(summary))
            meeting.Summaries.Add(new Summary { MeetingId = meeting.Id, Content = summary });
        return new MeetingItemViewModel(meeting);
    }

    [Fact]
    public void FilterMatches_Title()
    {
        var all = new List<MeetingItemViewModel> { MakeVm("Team standup"), MakeVm("Client call") };
        var filtered = MeetingsViewModel.ApplyFilter(all, "standup");
        Assert.Single(filtered);
        Assert.Equal("Team standup", filtered[0].Title);
    }

    [Fact]
    public void FilterMatches_TranscriptContent()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("Meeting A", transcript: "discussed quarterly budget"),
            MakeVm("Meeting B", transcript: "team building exercise"),
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "quarterly");
        Assert.Single(filtered);
        Assert.Equal("Meeting A", filtered[0].Title);
    }

    [Fact]
    public void FilterMatches_SummaryContent()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("Meeting A", summary: "action item: deploy hotfix"),
            MakeVm("Meeting B", summary: "no action items"),
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "hotfix");
        Assert.Single(filtered);
        Assert.Equal("Meeting A", filtered[0].Title);
    }

    [Fact]
    public void FilterEmpty_ReturnsAll()
    {
        var all = new List<MeetingItemViewModel> { MakeVm("A"), MakeVm("B"), MakeVm("C") };
        Assert.Equal(3, MeetingsViewModel.ApplyFilter(all, "").Count);
    }

    [Fact]
    public void FilterIsCaseInsensitive()
    {
        var all = new List<MeetingItemViewModel> { MakeVm("Team Standup") };
        Assert.Single(MeetingsViewModel.ApplyFilter(all, "STANDUP"));
        Assert.Single(MeetingsViewModel.ApplyFilter(all, "standup"));
    }
}
