using System;
using Serilog;

namespace VoxMemo.Services.Platform.Stub;

public class StubGlobalHotkeyService : IGlobalHotkeyService
{
    public void Register(int modifiers, int vk, Action callback)
    {
        Log.Warning("Global hotkeys are not supported on this platform");
    }

    public void Dispose() { }
}
