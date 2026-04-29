using System;
using System.IO;
using NAudio.Wave;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsAudioConverter : IAudioConverter
{
    public void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        using var reader = new AudioFileReader(inputPath);

        // Resample to 16 kHz if needed
        ISampleProvider resampled = reader.WaveFormat.SampleRate != 16000
            ? (ISampleProvider)new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(reader, 16000)
            : reader;

        // Downmix to mono if needed
        ISampleProvider mono = resampled.WaveFormat.Channels != 1
            ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(resampled)
            : resampled;

        // Write as 16-bit PCM
        WaveFileWriter.CreateWaveFile16(outputPath, mono);
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
