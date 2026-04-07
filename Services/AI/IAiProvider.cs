using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoxMemo.Services.AI;

public record AiModel(string Id, string Name);

public interface IAiProvider
{
    string ProviderName { get; }
    Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default);

    Task<string> SummarizeAsync(
        string transcript,
        string promptType,
        string language,
        string model,
        CancellationToken ct = default);

    IAsyncEnumerable<string> SummarizeStreamAsync(
        string transcript,
        string promptType,
        string language,
        string model,
        CancellationToken ct = default);
}
