namespace SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using global::SafeCopy.DocumentEngine.Security;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
public sealed class DocumentEngine : IDocumentEngine, IDocumentIngestionEngine
{
    private readonly IEnumerable<IDocumentIngestor> _ingestors;
    private readonly IDocumentSecurityValidator _securityValidator;
    private readonly IFileSystem _fileSystem;
    public DocumentEngine(
        IEnumerable<IDocumentIngestor> ingestors,
        IDocumentSecurityValidator securityValidator,
        IFileSystem fileSystem)
    {
        _ingestors = ingestors ?? throw new ArgumentNullException(nameof(ingestors));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }
    public Result<Document> Load(string filePath, CancellationToken cancellationToken = default)
    {
        return Load(filePath, new IngestionOptions(), cancellationToken);
    }
    public Result<Document> Load(string filePath, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var ingestor = GetIngestorForFile(filePath);
        if (ingestor == null)
        {
            var formatResult = DetectFormat(filePath);
            if (formatResult.IsFailure)
                return Result<Document>.Failure(formatResult.Error);
            // Use detected format to find ingestor
            ingestor = GetIngestorForFormat(formatResult.Value);
            if (ingestor == null)
                return Result<Document>.Failure(Error.FormatError($"No ingestor for detected format: {formatResult.Value}"));
        }
        return ingestor.Ingest(filePath, options, cancellationToken);
    }
    public Result<Document> Load(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        return Load(stream, format, new IngestionOptions(), cancellationToken);
    }
    public Result<Document> Load(Stream stream, DocumentFormat format, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var ingestor = GetIngestorForFormat(format);
        if (ingestor == null)
        {
            return Result<Document>.Failure(Error.FormatError($"Unsupported document format: {format}"));
        }
        return ingestor.Ingest(stream, options, cancellationToken);
    }
    public Task<Result<Document>> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return LoadAsync(filePath, new IngestionOptions(), cancellationToken);
    }
    public Task<Result<Document>> LoadAsync(string filePath, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var ingestor = GetIngestorForFile(filePath);
        if (ingestor == null)
        {
            var format = DetectFormat(filePath);
            return Task.FromResult(Result<Document>.Failure(Error.FormatError($"Unsupported document format: {format}")));
        }
        return ingestor.IngestAsync(filePath, options, cancellationToken);
    }
    public Task<Result<Document>> LoadAsync(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        return LoadAsync(stream, format, new IngestionOptions(), cancellationToken);
    }
    public Task<Result<Document>> LoadAsync(Stream stream, DocumentFormat format, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        var ingestor = GetIngestorForFormat(format);
        if (ingestor == null)
        {
            return Task.FromResult(Result<Document>.Failure(Error.FormatError($"Unsupported document format: {format}")));
        }
        return ingestor.IngestAsync(stream, options, cancellationToken);
    }
    // IDocumentIngestionEngine implementation
    public Result<Document> Ingest(string filePath, IngestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Load(filePath, options ?? new IngestionOptions(), cancellationToken);
    }
    public async Task<Result<Document>> IngestAsync(string filePath, IngestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await LoadAsync(filePath, options ?? new IngestionOptions(), cancellationToken);
    }
    public Result<Document> Ingest(Stream stream, IngestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var formatResult = DetectFormat(stream);
        if (formatResult.IsFailure)
        {
            return Result<Document>.Failure(formatResult.Error);
        }
        return Load(stream, formatResult.Value, options ?? new IngestionOptions(), cancellationToken);
    }
    public async Task<Result<Document>> IngestAsync(Stream stream, IngestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var formatResult = DetectFormat(stream);
        if (formatResult.IsFailure)
        {
            return Result<Document>.Failure(formatResult.Error);
        }
        return await LoadAsync(stream, formatResult.Value, options ?? new IngestionOptions(), cancellationToken);
    }
    public Result<DocumentFormat> DetectFormat(string filePath)
    {
        // Handle double extension .udf.zip before Path.GetExtension
        if (filePath.EndsWith(".udf.zip", StringComparison.OrdinalIgnoreCase))
        {
            var zipFormat = _securityValidator.IdentifyZipBasedFormat(filePath);
            if (zipFormat != DocumentFormat.Unknown)
                return Result<DocumentFormat>.Success(zipFormat);
            // Fallback to Udf if ZIP inspection fails but extension suggests it
            return Result<DocumentFormat>.Success(DocumentFormat.Udf);
        }
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var format = extension switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".docx" => DocumentFormat.Docx,
            ".xlsx" => DocumentFormat.Xlsx,
            ".txt" => DocumentFormat.Txt,
            ".udf" => DocumentFormat.Udf,
            ".zip" => DocumentFormat.Unknown, // delegate to ZIP inspection
            ".png" => DocumentFormat.Png,
            ".jpg" or ".jpeg" => DocumentFormat.Jpeg,
            ".tiff" or ".tif" => DocumentFormat.Tiff,
            ".bmp" => DocumentFormat.Bmp,
            _ => DocumentFormat.Unknown
        };
        var signatureFormat = _securityValidator.DetectFormatFromSignature(filePath);
        // If signature detection succeeds and differs from extension, trust the signature
        // (signature is more reliable than extension)
        if (signatureFormat != DocumentFormat.Unknown && signatureFormat != format)
        {
            if (format == DocumentFormat.Unknown)
            {
                // Extension unknown but signature known - trust signature
                return Result<DocumentFormat>.Success(signatureFormat);
            }
            // Both known but differ - this is a mismatch error
            return Result<DocumentFormat>.Failure(Error.FormatError($"Format mismatch: extension={format}, signature={signatureFormat}"));
        }
        // If signature is Unknown but extension suggests ZIP-based format, do deep inspection
        if (signatureFormat == DocumentFormat.Unknown &&
            (format == DocumentFormat.Docx || format == DocumentFormat.Xlsx || format == DocumentFormat.Udf))
        {
            var zipFormat = _securityValidator.IdentifyZipBasedFormat(filePath);
            if (zipFormat != DocumentFormat.Unknown)
                return Result<DocumentFormat>.Success(zipFormat);
        }
        // If both extension and signature are Unknown, try ZIP inspection for unknown formats
        if (format == DocumentFormat.Unknown && signatureFormat == DocumentFormat.Unknown)
        {
            // Check if file has ZIP signature (PK header)
            var zipFormat = _securityValidator.IdentifyZipBasedFormat(filePath);
            if (zipFormat != DocumentFormat.Unknown)
                return Result<DocumentFormat>.Success(zipFormat);
            // Not a ZIP format either - truly unknown
            return Result<DocumentFormat>.Failure(Error.FormatError($"Unsupported document format: {extension}"));
        }
        return Result<DocumentFormat>.Success(format);
    }
    public async Task<Result<DocumentFormat>> DetectFormatAsync(string filePath)
    {
        return await Task.FromResult(DetectFormat(filePath));
    }
    public Result<DocumentFormat> DetectFormat(Stream stream)
    {
        var format = _securityValidator.DetectFormatFromStream(stream);
        if (format == DocumentFormat.Unknown)
        {
            return Result<DocumentFormat>.Failure(Error.FormatError("Could not detect format from stream signature"));
        }
        return Result<DocumentFormat>.Success(format);
    }
    public async Task<Result<DocumentFormat>> DetectFormatAsync(Stream stream)
    {
        return await Task.FromResult(DetectFormat(stream));
    }
    public Result<IReadOnlyList<DocumentFormat>> GetSupportedFormats()
    {
        var formats = _ingestors
            .SelectMany(i => i.SupportedExtensions)
            .Select(ext => DetectFormatFromExtension("dummy" + ext))
            .Distinct()
            .ToList();
        return Result<IReadOnlyList<DocumentFormat>>.Success(formats);
    }
    private IDocumentIngestor? GetIngestorForFile(string filePath)
    {
        if (filePath.EndsWith(".udf.zip", StringComparison.OrdinalIgnoreCase))
        {
            var udfIngestor = _ingestors.FirstOrDefault(i => i.SupportedFormat == DocumentFormat.Udf);
            if (udfIngestor != null) return udfIngestor;
        }
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return _ingestors.FirstOrDefault(i => i.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }
    private IDocumentIngestor? GetIngestorForFormat(DocumentFormat format)
    {
        return _ingestors.FirstOrDefault(i => i.SupportedFormat == format);
    }
    private DocumentFormat DetectFormatFromExtension(string filePath)
    {
        if (filePath.EndsWith(".udf.zip", StringComparison.OrdinalIgnoreCase))
            return DocumentFormat.Udf;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".docx" => DocumentFormat.Docx,
            ".xlsx" => DocumentFormat.Xlsx,
            ".txt" => DocumentFormat.Txt,
            ".udf" => DocumentFormat.Udf,
            ".zip" => DocumentFormat.Unknown,
            ".png" => DocumentFormat.Png,
            ".jpg" or ".jpeg" => DocumentFormat.Jpeg,
            ".tiff" or ".tif" => DocumentFormat.Tiff,
            ".bmp" => DocumentFormat.Bmp,
            _ => DocumentFormat.Unknown
        };
    }
}
