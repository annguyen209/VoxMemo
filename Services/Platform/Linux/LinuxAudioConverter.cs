using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace VoxMemo.Services.Platform.Linux;

/// <summary>
/// Linux audio converter using ffmpeg.
/// </summary>
public class LinuxAudioConverter : IAudioConverter
{
    public void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        // ffmpeg -i input -ar 16000 -ac 1 -sample_fmt s16 output.wav
        var result = RunFfmpeg($"-i \"{inputPath}\" -ar 16000 -ac 1 -sample_fmt s16 -y \"{outputPath}\"");
        if (result != 0)
        {
            Log.Warning("ffmpeg conversion failed (exit {Code}), copying as-is", result);
            File.Copy(inputPath, outputPath, true);
        }
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
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return TimeSpan.Zero;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10000);

            if (double.TryParse(output, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex)
        {
            Log.Warning("ffprobe duration failed: {Error}", ex.Message);
        }

        return TimeSpan.Zero;
    }

    private static int RunFfmpeg(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            proc.WaitForExit(120000);
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ffmpeg execution failed");
            return -1;
        }
    }
}
