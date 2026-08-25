namespace EksimSafeCopy.DocumentEngine.Ingestion;

using global::EksimSafeCopy.Core.Abstractions;
using global::EksimSafeCopy.Core.Models;
using global::EksimSafeCopy.DocumentEngine.Security;

public abstract class DocumentIngestorBase : IDocumentIngestor
{
    protected readonly IDocumentSecurityValidator _securityValidator;
    protected readonly IFileSystem _fileSystem;

    public abstract DocumentFormat SupportedFormat { get; }
    public abstract string[] SupportedExtensions { get; }

    protected DocumentIngestorBase(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
    {
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public virtual Result<Document> Ingest(string filePath, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var effectiveOptions = options ?? new IngestionOptions();

        try
        {
            // Validate file access
            var accessResult = _securityValidator.ValidateFileAccess(filePath);
            if (accessResult.IsFailure)
                return Result<Document>.Failure(accessResult.Error);

            // Validate format if requested
            if (effectiveOptions.ValidateFormat)
            {
                var formatResult = _securityValidator.ValidateFormatMatch(filePath, SupportedFormat);
                if (formatResult.IsFailure)
                    return Result<Document>.Failure(formatResult.Error);
            }

            // Compute hash if requested
            string fileHash = string.Empty;
            if (effectiveOptions.ComputeHash)
            {
                var hashResult = _securityValidator.ComputeFileHash(filePath, effectiveOptions.HashAlgorithm);
                if (hashResult.IsFailure)
                    return Result<Document>.Failure(hashResult.Error);
                fileHash = hashResult.Value;
            }

            // Get file size
            long fileSize = 0;
            var sizeResult = _fileSystem.GetSize(filePath);
            if (sizeResult.IsSuccess)
                fileSize = sizeResult.Value;

            // Perform ingestion with timeout
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveOptions.Timeout);

            var document = IngestInternal(filePath, effectiveOptions, cts.Token);
            
            if (document.IsSuccess)
            {
                // Create new document with source and metadata
                var sourceRef = new SourceReference
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Format = SupportedFormat,
                    FileSize = fileSize,
                    FileHash = fileHash,
                    LoadedAt = DateTime.UtcNow
                };
                
                var metadata = document.Value.Metadata.WithFileInfo(fileSize, fileHash);
                
                document = document.Value
                    .WithSource(sourceRef)
                    .WithMetadata(metadata);
            }

            return document;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<Document>.Failure(Error.Cancelled("Ingestion was cancelled"));
        }
        catch (OperationCanceledException)
        {
            return Result<Document>.Failure(Error.Timeout($"Ingestion timed out after {effectiveOptions.Timeout}"));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"Ingestion failed: {ex.Message}", ex));
        }
        finally
        {
            // Log duration if needed
        }
    }

    public virtual async Task<Result<Document>> IngestAsync(string filePath, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Ingest(filePath, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public virtual Result<Document> Ingest(Stream stream, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new IngestionOptions();
        
        try
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveOptions.Timeout);

            return IngestFromStreamInternal(stream, effectiveOptions, cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<Document>.Failure(Error.Cancelled("Ingestion was cancelled"));
        }
        catch (OperationCanceledException)
        {
            return Result<Document>.Failure(Error.Timeout($"Ingestion timed out after {effectiveOptions.Timeout}"));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"Ingestion failed: {ex.Message}", ex));
        }
    }

    public virtual async Task<Result<Document>> IngestAsync(Stream stream, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Ingest(stream, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    protected abstract Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken);
    protected abstract Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken);

    protected virtual DocumentFormat DetectFormatFromExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".docx" => DocumentFormat.Docx,
            ".xlsx" => DocumentFormat.Xlsx,
            ".txt" => DocumentFormat.Txt,
            ".udf" => DocumentFormat.Udf,
            ".png" => DocumentFormat.Png,
            ".jpg" or ".jpeg" => DocumentFormat.Jpeg,
            ".tiff" or ".tif" => DocumentFormat.Tiff,
            ".bmp" => DocumentFormat.Bmp,
            _ => DocumentFormat.Unknown
        };
    }
}