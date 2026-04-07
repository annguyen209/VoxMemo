using System.IO;
using NAudio.Wave;

namespace VoxMemo.Services.Audio;

/// <summary>
/// Converts WAV files to 16kHz 16-bit mono PCM format required by Whisper.
/// </summary>
public static class AudioConverter
{
    public static void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        var targetFormat = new WaveFormat(16000, 16, 1);

        using var reader = new AudioFileReader(inputPath);
        using var resampler = new MediaFoundationResampler(reader, targetFormat);
        resampler.ResamplerQuality = 60;
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
    }

    /// <summary>
    /// Converts in-place: renames original, converts, deletes original.
    /// </summary>
    public static void ConvertInPlace(string wavPath)
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
            // Restore original if conversion fails
            if (!File.Exists(wavPath) && File.Exists(tempPath))
                File.Move(tempPath, wavPath);
            throw;
        }
    }
}
