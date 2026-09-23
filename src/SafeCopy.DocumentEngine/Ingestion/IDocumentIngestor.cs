namespace SafeCopy.DocumentEngine.Ingestion;

using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using CoreHashAlgorithm = global::SafeCopy.Core.Abstractions.HashAlgorithm;

public interface IDocumentIngestor
{
    DocumentFormat SupportedFormat { get; }
    string[] SupportedExtensions { get; }

    Result<Document> Ingest(string filePath, IngestionOptions options, CancellationToken cancellationToken = default);
    Task<Result<Document>> IngestAsync(string filePath, IngestionOptions options, CancellationToken cancellationToken = default);

    Result<Document> Ingest(Stream stream, IngestionOptions options, CancellationToken cancellationToken = default);
    Task<Result<Document>> IngestAsync(Stream stream, IngestionOptions options, CancellationToken cancellationToken = default);
}

public sealed class IngestionOptions
{
    public static IngestionOptions Default => new();

    public long MaxFileSizeBytes { get; init; } = 500_000_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public bool ValidateFormat { get; init; } = true;
    public bool ComputeHash { get; init; } = true;
    public CoreHashAlgorithm HashAlgorithm { get; init; } = CoreHashAlgorithm.SHA256;
    public bool ExtractImages { get; init; } = false;
    public bool ExtractMetadata { get; init; } = true;
    public CancellationToken CancellationToken { get; init; } = default;
}

public interface IDocumentIngestionEngine
{
    Result<Document> Ingest(string filePath, IngestionOptions? options = null, CancellationToken cancellationToken = default);
    Task<Result<Document>> IngestAsync(string filePath, IngestionOptions? options = null, CancellationToken cancellationToken = default);

    Result<DocumentFormat> DetectFormat(string filePath);
    Task<Result<DocumentFormat>> DetectFormatAsync(string filePath);

    Result<IReadOnlyList<DocumentFormat>> GetSupportedFormats();
}

public sealed class IngestionResult
{
    public Document? Document { get; init; }
    public DocumentFormat Format { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public TimeSpan ProcessingDuration { get; init; }
    public bool Success { get; init; }
    public Error? Error { get; init; }

    public static IngestionResult CreateSuccess(Document document, DocumentFormat format, string sourcePath, long fileSize, string fileHash, TimeSpan duration)
        => new() { Document = document, Format = format, SourcePath = sourcePath, FileSize = fileSize, FileHash = fileHash, ProcessingDuration = duration, Success = true };

    public static IngestionResult CreateFailure(Error error, DocumentFormat format, string sourcePath, TimeSpan duration)
        => new() { Format = format, SourcePath = sourcePath, ProcessingDuration = duration, Success = false, Error = error };
}