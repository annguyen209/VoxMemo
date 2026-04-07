namespace VoxMemo.Services.Platform;

public interface IStartupService
{
    bool IsSupported { get; }
    bool IsStartupEnabled();
    void SetStartupEnabled(bool enable);
}
