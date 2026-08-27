namespace EksimSafeCopy.Core.Abstractions;

using EksimSafeCopy.Core.Models;

public enum BatchItemState
{
    Queued,
    Processing,
    Success,
    Failed,
    Unsupported,
    Cancelled,
    Skipped
}

public sealed class BatchItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string InputPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DocumentFormat DetectedFormat { get; init; } = DocumentFormat.Unknown;
    public BatchItemState State { get; init; } = BatchItemState.Queued;
    public string StatusMessage { get; init; } = string.Empty;
    public Error? Error { get; init; }
    public string? OutputPath { get; init; }
    public VerificationResult? VerificationResult { get; init; }
    public IReadOnlyList<Detection> Detections { get; init; } = Array.Empty<Detection>();
    public string? OriginalHash { get; init; }
    public string? OutputHash { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
}

public sealed class BatchRequest
{
    public IReadOnlyList<string> InputPaths { get; init; } = Array.Empty<string>();
    public RenderOptions Options { get; init; } = new RenderOptions();
    public bool ContinueOnError { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = 1;

    public BatchRequest() { }

    public BatchRequest(IReadOnlyList<string> inputPaths, RenderOptions? options = null, bool continueOnError = true, int maxDegreeOfParallelism = 1)
    {
        InputPaths = inputPaths ?? throw new ArgumentNullException(nameof(inputPaths));
        Options = options ?? new RenderOptions();
        ContinueOnError = continueOnError;
        MaxDegreeOfParallelism = maxDegreeOfParallelism < 1 ? 1 : maxDegreeOfParallelism;
    }
}

public sealed class BatchResult
{
    public IReadOnlyList<BatchItem> Items { get; init; } = Array.Empty<BatchItem>();
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int UnsupportedCount { get; init; }
    public int CancelledCount { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public bool IsCancelled { get; init; }

    public int TotalCount => Items.Count;
    public int SkippedCount => Items.Count(i => i.State == BatchItemState.Skipped);
}

public interface IBatchProcessor
{
    Task<Result<BatchResult>> ProcessAsync(BatchRequest request, IProgress<BatchItem>? progress = null, CancellationToken cancellationToken = default);
    Result<BatchResult> Process(BatchRequest request, IProgress<BatchItem>? progress = null, CancellationToken cancellationToken = default);
}
