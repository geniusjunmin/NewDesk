using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public interface IAiProvider
{
    string ProviderId { get; }
    AiProviderConfig Config { get; }
    AiProviderCapabilities Capabilities { get; }

    Task<AiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, CancellationToken cancellationToken = default);
}
