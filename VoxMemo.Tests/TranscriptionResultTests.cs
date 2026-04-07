using VoxMemo.Services.Transcription;

namespace VoxMemo.Tests;

public class TranscriptionResultTests
{
    [Fact]
    public void TranscriptionResult_StoresAllFields()
    {
        var segments = new List<TranscriptionSegment>
        {
            new(0, 5000, "Hello world", 0.95f),
            new(5000, 10000, "How are you", 0.88f),
        };

        var result = new TranscriptionResult("Hello world How are you", segments, "en", "whisper-local", "tiny");

        Assert.Equal("Hello world How are you", result.FullText);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("en", result.Language);
        Assert.Equal("whisper-local", result.Engine);
        Assert.Equal("tiny", result.Model);
    }

    [Fact]
    public void TranscriptionSegment_StoresTimestamps()
    {
        var seg = new TranscriptionSegment(1500, 3000, "Test", 0.9f);
        Assert.Equal(1500, seg.StartMs);
        Assert.Equal(3000, seg.EndMs);
        Assert.Equal("Test", seg.Text);
        Assert.Equal(0.9f, seg.Confidence);
    }
}
