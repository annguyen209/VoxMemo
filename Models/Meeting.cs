using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VoxMemo.Models;

public class Meeting
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? AudioPath { get; set; }
    public long? DurationMs { get; set; }
    public string Language { get; set; } = "en";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Transcript> Transcripts { get; set; } = [];
    public List<Summary> Summaries { get; set; } = [];
}

public class Transcript
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MeetingId { get; set; } = string.Empty;
    public string Engine { get; set; } = "whisper-local";
    public string? Model { get; set; }
    public string? Language { get; set; }
    public string? FullText { get; set; }
    public string? OriginalFullText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Meeting? Meeting { get; set; }
    public List<TranscriptSegment> Segments { get; set; } = [];
}

public class TranscriptSegment
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TranscriptId { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public float? Confidence { get; set; }

    public Transcript? Transcript { get; set; }
}

public class Summary
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MeetingId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptType { get; set; } = "meeting_summary";
    public string Content { get; set; } = string.Empty;
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Meeting? Meeting { get; set; }
}

public class AppSettings
{
    [Key]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
