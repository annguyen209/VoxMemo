using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoxMemo.Services.Transcription;

public record TranscriptionResult(
    string FullText,
    List<TranscriptionSegment> Segments,
    string Language,
    string Engine,
    string? Model);

public record TranscriptionSegment(
    long StartMs,
    long EndMs,
    string Text,
    float Confidence);

public interface ITranscriptionService
{
    event EventHandler<int>? ProgressChanged;

    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string language = "en",
        string? modelName = null,
        CancellationToken ct = default);

    Task<List<string>> GetAvailableModelsAsync();
    Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken ct = default);
    string EngineName { get; }
}
