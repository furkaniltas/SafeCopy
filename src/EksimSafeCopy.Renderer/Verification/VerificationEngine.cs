namespace EksimSafeCopy.Renderer.Verification;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.Detectors.Detection.Pipeline;
using EksimSafeCopy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using EksimSafeCopy.DocumentEngine.Security;

public sealed class VerificationEngine : EksimSafeCopy.Core.Abstractions.IVerificationEngine
{
    private readonly IDetectionEngine _detectionEngine;
    private readonly IFileSystem _fileSystem;
    private readonly IDocumentSecurityValidator _securityValidator;

    public VerificationEngine(IDetectionEngine detectionEngine, IFileSystem fileSystem, IDocumentSecurityValidator securityValidator)
    {
        _detectionEngine = detectionEngine ?? throw new ArgumentNullException(nameof(detectionEngine));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
    }

    public Result<VerificationResult> Verify(string filePath, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var accessResult = _securityValidator.ValidateFileAccess(filePath);
            if (accessResult.IsFailure)
                return Result<VerificationResult>.Failure(accessResult.Error);

            var formatResult = _securityValidator.ValidateFormatMatch(filePath, format);
            if (formatResult.IsFailure)
                return Result<VerificationResult>.Failure(formatResult.Error);

            var loadResult = LoadDocumentForVerification(filePath, format, cancellationToken);
            if (loadResult.IsFailure)
                return Result<VerificationResult>.Failure(loadResult.Error);

            var document = loadResult.Value;

            var detectResult = _detectionEngine.Detect(document, cancellationToken);
            if (detectResult.IsFailure)
                return Result<VerificationResult>.Failure(detectResult.Error);

            var detections = detectResult.Value;
            var residualDetections = detections
                .Where(d => d.State != DetectionState.FalsePositive && d.State != DetectionState.Deselected)
                .Select(d => new ResidualDetection
                {
                    Type = d.Type,
                    Value = d.Value,
                    Context = d.Context,
                    Confidence = d.Confidence,
                    Location = d.Location,
                    PageNumber = d.PageNumber
                })
                .ToList();

            var metadataIssues = CheckMetadata(document);
            var hiddenContentIssues = CheckHiddenContent(document);

            stopwatch.Stop();

            var result = new VerificationResult
            {
                Passed = residualDetections.Count == 0 && metadataIssues.Count == 0 && hiddenContentIssues.Count == 0,
                ResidualDetections = residualDetections,
                MetadataIssues = metadataIssues,
                HiddenContentIssues = hiddenContentIssues,
                ScanDuration = stopwatch.Elapsed,
                VerifiedAt = DateTime.UtcNow
            };

            return Result<VerificationResult>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<VerificationResult>.Failure(Error.Cancelled("Verification was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<VerificationResult>.Failure(Error.Internal($"Verification failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<VerificationResult>> VerifyAsync(string filePath, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Verify(filePath, format, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<VerificationResult> Verify(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var tempPath = Path.GetTempFileName();
            try
            {
                using var fileStream = File.Create(tempPath);
                stream.CopyTo(fileStream);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            var result = Verify(tempPath, format, cancellationToken);
            
            if (File.Exists(tempPath)) File.Delete(tempPath);
            
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<VerificationResult>.Failure(Error.Cancelled("Verification was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<VerificationResult>.Failure(Error.Internal($"Verification failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<VerificationResult>> VerifyAsync(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Verify(stream, format, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private Result<Document> LoadDocumentForVerification(string filePath, DocumentFormat format, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, EksimSafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Pdf.PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Docx.DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Xlsx.XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Txt.TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Udf.UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Image.ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();

        var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<IDocumentEngine>();

        var formatResult = engine.DetectFormat(filePath);
        if (formatResult.IsFailure)
            return Result<Document>.Failure(formatResult.Error);

        // Use simple load with default options - the engine will auto-detect format
        return engine.Load(filePath, cancellationToken);
    }

    private IReadOnlyList<string> CheckMetadata(Document document)
    {
        var issues = new List<string>();

        if (!string.IsNullOrWhiteSpace(document.Metadata.Author))
            issues.Add("Author metadata contains potential PII");

        if (!string.IsNullOrWhiteSpace(document.Metadata.Title))
            issues.Add("Title metadata may contain PII");

        if (!string.IsNullOrWhiteSpace(document.Metadata.Subject))
            issues.Add("Subject metadata may contain PII");

        if (!string.IsNullOrWhiteSpace(document.Metadata.Keywords))
            issues.Add("Keywords metadata may contain PII");

        if (!string.IsNullOrWhiteSpace(document.Metadata.Creator))
            issues.Add("Creator metadata may contain PII");

        if (!string.IsNullOrWhiteSpace(document.Metadata.Producer))
            issues.Add("Producer metadata may contain PII");

        foreach (var kvp in document.Metadata.CustomProperties)
        {
            issues.Add($"Custom property '{kvp.Key}' may contain PII");
        }

        return issues;
    }

    private IReadOnlyList<string> CheckHiddenContent(Document document)
    {
        var issues = new List<string>();

        foreach (var page in document.Pages)
        {
            if (page.IsScanned && page.TextBlocks.Count == 0 && page.Images.Count > 0)
            {
                issues.Add($"Page {page.PageNumber} appears to be scanned but has no OCR text - may contain hidden PII in images");
            }

            foreach (var block in page.TextBlocks)
            {
                if (block.Properties.TryGetValue("IsHidden", out var isHidden) && isHidden is true)
                {
                    issues.Add($"Page {page.PageNumber} contains hidden text block: {block.Text}");
                }
            }
        }

        return issues;
    }
}