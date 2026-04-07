using System;

namespace VoxMemo.Services.Platform;

public interface IAudioConverter
{
    void ConvertToWhisperFormat(string inputPath, string outputPath);
    void ConvertInPlace(string wavPath);
    TimeSpan GetDuration(string audioPath);
}
