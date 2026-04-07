using System;
using System.IO;
using Serilog;

namespace VoxMemo.Services.Platform.Stub;

public class StubAudioConverter : IAudioConverter
{
    public void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        Log.Warning("Audio conversion not available on this platform, copying file as-is");
        File.Copy(inputPath, outputPath, true);
    }

    public void ConvertInPlace(string wavPath)
    {
        Log.Warning("Audio conversion not available on this platform, keeping original format");
    }

    public TimeSpan GetDuration(string audioPath) => TimeSpan.Zero;
}
