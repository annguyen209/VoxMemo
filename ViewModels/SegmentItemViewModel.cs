namespace VoxMemo.ViewModels;

public class SegmentItemViewModel
{
    public string Timestamp { get; }
    public string Text { get; }
    public double StartSeconds { get; }

    public SegmentItemViewModel(long startMs, long endMs, string text)
    {
        var ts = System.TimeSpan.FromMilliseconds(startMs);
        Timestamp = ts.ToString(@"mm\:ss");
        Text = text;
        StartSeconds = startMs / 1000.0;
    }
}
