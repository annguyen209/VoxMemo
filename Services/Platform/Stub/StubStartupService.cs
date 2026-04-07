namespace VoxMemo.Services.Platform.Stub;

public class StubStartupService : IStartupService
{
    public bool IsSupported => false;
    public bool IsStartupEnabled() => false;
    public void SetStartupEnabled(bool enable) { }
}
