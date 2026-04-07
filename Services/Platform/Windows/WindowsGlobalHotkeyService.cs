using System;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace VoxMemo.Services.Platform.Windows;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;
    private Thread? _thread;
    private volatile bool _running;

    public void Register(int modifiers, int vk, Action callback)
    {
        // Stop any previous registration
        Dispose();

        _running = true;
        _thread = new Thread(() =>
        {
            if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID, modifiers, vk))
            {
                Log.Warning("Failed to register global hotkey");
                return;
            }

            while (_running)
            {
                if (PeekMessage(out var msg, IntPtr.Zero, WM_HOTKEY, WM_HOTKEY, 1))
                {
                    if (msg.message == WM_HOTKEY)
                        callback.Invoke();
                }
                Thread.Sleep(50);
            }

            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        })
        {
            IsBackground = true,
            Name = "GlobalHotkeyThread"
        };
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(2000);
        _thread = null;
    }
}
