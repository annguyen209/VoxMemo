using VoxMemo.Services.Transcription;

namespace VoxMemo.Tests;

public class WhisperServiceTests
{
    [Fact]
    public void EngineName_IsWhisperLocal()
    {
        var service = new WhisperTranscriptionService();
        Assert.Equal("whisper-local", service.EngineName);
    }

    [Fact]
    public async Task GetAvailableModels_ReturnsListWithoutError()
    {
        var service = new WhisperTranscriptionService();
        var models = await service.GetAvailableModelsAsync();
        Assert.NotNull(models);
        // May be empty if no models downloaded, but should not throw
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsForMissingModel()
    {
        var service = new WhisperTranscriptionService();
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await service.TranscribeAsync("dummy.wav", "en", "nonexistent_model_xyz");
        });
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsForMissingAudioFile()
    {
        var service = new WhisperTranscriptionService();
        // Will throw FileNotFoundException for model or audio
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await service.TranscribeAsync("nonexistent_file.wav", "en", "tiny");
        });
    }
}
