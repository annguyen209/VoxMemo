using System.IO;
using NAudio.Wave;
using VoxMemo.Services.Platform.Windows;

namespace VoxMemo.Tests;

public class AudioConverterTests
{
    private static string CreateTestWav(int sampleRate, int channels, int durationSeconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Path.GetRandomFileName()}.wav");
        var format = new WaveFormat(sampleRate, 16, channels);
        using var writer = new WaveFileWriter(path, format);
        int totalSamples = sampleRate * channels * durationSeconds;
        var data = new byte[totalSamples * 2]; // 16-bit = 2 bytes per sample
        writer.Write(data, 0, data.Length);
        return path;
    }

    [Fact]
    public void ConvertToWhisperFormat_Produces16kHzMono16bit()
    {
        var inputPath = CreateTestWav(44100, 2, 2);
        var outputPath = inputPath + ".out.wav";
        try
        {
            new WindowsAudioConverter().ConvertToWhisperFormat(inputPath, outputPath);
            Assert.True(File.Exists(outputPath));
            using var reader = new WaveFileReader(outputPath);
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void ConvertToWhisperFormat_PreservesAudioContent()
    {
        var inputPath = CreateTestWav(48000, 2, 1);
        var outputPath = inputPath + ".out.wav";
        try
        {
            new WindowsAudioConverter().ConvertToWhisperFormat(inputPath, outputPath);
            var info = new FileInfo(outputPath);
            // 1 second at 16kHz mono 16-bit = 16000 * 2 = 32000 bytes of audio data + WAV header (~44 bytes)
            Assert.True(info.Length > 32000);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
