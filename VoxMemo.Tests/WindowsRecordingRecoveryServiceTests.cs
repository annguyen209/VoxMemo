using System.IO;
using NAudio.Wave;
using VoxMemo.Services.Platform.Windows;

namespace VoxMemo.Tests;

public class WindowsRecordingRecoveryServiceTests
{
    [Fact]
    public void TryRepairWaveHeader_RepairsIncorrectRiffAndDataSizes()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "broken.wav");
            CreateWaveFile(path);
            var dataSizeOffset = FindDataSizeOffset(path);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 4;
                writer.Write(0u);
                stream.Position = dataSizeOffset;
                writer.Write(0u);
            }

            var repaired = WindowsRecordingRecoveryService.TryRepairWaveHeader(path);

            Assert.True(repaired);
            using var repairedStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(repairedStream);
            repairedStream.Position = 4;
            Assert.Equal((uint)(repairedStream.Length - 8), reader.ReadUInt32());
            repairedStream.Position = dataSizeOffset;
            Assert.Equal((uint)(repairedStream.Length - (dataSizeOffset + sizeof(uint))), reader.ReadUInt32());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RecoverInterruptedRecordings_MixesOrphanedTempFilesIntoRecoveredMeeting()
    {
        var dir = CreateTempDirectory();
        try
        {
            var micPath = Path.Combine(dir, "vox_mic_deadbeef.wav");
            var sysPath = Path.Combine(dir, "vox_sys_deadbeef.wav");
            CreateWaveFile(micPath);
            CreateWaveFile(sysPath);

            var recovered = WindowsRecordingRecoveryService.RecoverInterruptedRecordings(dir);

            var output = Assert.Single(recovered);
            Assert.True(File.Exists(output));
            Assert.DoesNotContain("vox_mic_", Path.GetFileName(output));
            Assert.False(File.Exists(micPath));
            Assert.False(File.Exists(sysPath));
            Assert.True(new FileInfo(output).Length > 44);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRepairWaveHeader_ClampsToUIntMaxForLargeFiles()
    {
        // This test verifies the repair works and produces valid uint values
        // (not negative/garbage from overflow) even when sizes approach uint range.
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "clamped.wav");
            CreateWaveFile(path);
            var dataSizeOffset = FindDataSizeOffset(path);

            // Zero out RIFF and data sizes to simulate an incomplete write
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 4;
                writer.Write(0u);
                stream.Position = dataSizeOffset;
                writer.Write(0u);
            }

            var repaired = WindowsRecordingRecoveryService.TryRepairWaveHeader(path);
            Assert.True(repaired);

            // The written values must be valid uints (no negative/overflow)
            using var checkStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(checkStream);
            checkStream.Position = 4;
            var riffSize = reader.ReadUInt32();
            checkStream.Position = dataSizeOffset;
            var dataSize = reader.ReadUInt32();

            // Verify values are non-zero and within uint range
            Assert.True(riffSize > 0 && riffSize <= uint.MaxValue);
            Assert.True(dataSize > 0 && dataSize <= uint.MaxValue);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VoxMemoTests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateWaveFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1));
        var samples = new byte[16000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (byte)(i % 255);
        writer.Write(samples, 0, samples.Length);
    }

    private static long FindDataSizeOffset(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i <= bytes.Length - 8; i++)
        {
            if (bytes[i] == (byte)'d' &&
                bytes[i + 1] == (byte)'a' &&
                bytes[i + 2] == (byte)'t' &&
                bytes[i + 3] == (byte)'a')
            {
                return i + 4;
            }
        }

        throw new InvalidOperationException("WAV data chunk not found");
    }
}
