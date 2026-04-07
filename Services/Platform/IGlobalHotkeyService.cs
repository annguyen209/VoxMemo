using System;

namespace VoxMemo.Services.Platform;

public interface IGlobalHotkeyService : IDisposable
{
    void Register(int modifiers, int vk, Action callback);

    const int MOD_ALT = 0x0001;
    const int MOD_CONTROL = 0x0002;
    const int MOD_SHIFT = 0x0004;
    const int MOD_WIN = 0x0008;
}
