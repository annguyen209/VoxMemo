using System;
using System.IO;
using NAudio.Wave;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsAudioConverter : IAudioConverter
{
    public void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        var targetFormat = new WaveFormat(16000, 16, 1);

        using var reader = new AudioFileReader(inputPath);
        using var resampler = new MediaFoundationResampler(reader, targetFormat);
        resampler.ResamplerQuality = 60;
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
    }

    public void ConvertInPlace(string wavPath)
    {
        var tempPath = wavPath + ".original.wav";
        File.Move(wavPath, tempPath, overwrite: true);

        try
        {
            ConvertToWhisperFormat(tempPath, wavPath);
            File.Delete(tempPath);
        }
        catch
        {
            if (!File.Exists(wavPath) && File.Exists(tempPath))
                File.Move(tempPath, wavPath);
            throw;
        }
    }

    public TimeSpan GetDuration(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        return reader.TotalTime;
    }
}
