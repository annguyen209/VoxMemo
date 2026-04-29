namespace VoxMemo.Tests;

public class SegmentItemViewModelTests
{
    [Fact]
    public void Timestamp_FormatsStartMsAsMinutesAndSeconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(65_000, 70_000, "Hello world");
        Assert.Equal("01:05", vm.Timestamp);
    }

    [Fact]
    public void Timestamp_PadsZeroBelowTenSeconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(5_000, 10_000, "Short");
        Assert.Equal("00:05", vm.Timestamp);
    }

    [Fact]
    public void StartSeconds_ConvertsMilliseconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(90_000, 95_000, "Text");
        Assert.Equal(90.0, vm.StartSeconds);
    }
}
