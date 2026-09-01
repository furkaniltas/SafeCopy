namespace EksimSafeCopy.Renderer.Verification;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.Detectors.Detection.Pipeline;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.Detectors;
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
        return Verify(filePath, format, Array.Empty<Detection>(), new RenderOptions(), cancellationToken);
    }

    public Result<VerificationResult> Verify(string filePath, DocumentFormat format, IReadOnlyList<Detection> originalDetections, RenderOptions options, CancellationToken cancellationToken = default)
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

            var document = loadResult.Value.Document;
            var detectionEngine = loadResult.Value.DetectionEngine;

            var detectResult = detectionEngine.Detect(document, cancellationToken);
            if (detectResult.IsFailure)
                return Result<VerificationResult>.Failure(detectResult.Error);

            var detections = detectResult.Value;
            var isPartial = options.Mode == MaskingMode.PartialMask;
            List<ResidualDetection> residualDetections;
            if (isPartial && originalDetections.Count > 0)
            {
                // Policy-aware partial verification: original full values must not be present, only allowed visible parts
                var originalByType = originalDetections.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Select(d => d.Value).ToHashSet(StringComparer.OrdinalIgnoreCase));
                residualDetections = new List<ResidualDetection>();
                foreach (var d in detections.Where(d => d.State != DetectionState.FalsePositive && d.State != DetectionState.Deselected))
                {
                    // If this detection's value was originally selected for partial masking, check if its visible part is allowed
                    bool wasOriginalPartial = originalDetections.Any(o => string.Equals(o.Value, d.Value, StringComparison.OrdinalIgnoreCase));
                    if (wasOriginalPartial)
                    {
                        // This exact original value should have been masked - if still found, it's a failure
                        residualDetections.Add(new ResidualDetection { Type = d.Type, Value = d.Value, Context = d.Context, Confidence = d.Confidence, Location = d.Location, PageNumber = d.PageNumber });
                        continue;
                    }
                    // For partial, the full original should not be found, but a substring (like last 4 of TC) might be found as new detection
                    foreach (var orig in originalDetections)
                    {
                        var expectedMasked = EksimSafeCopy.Renderer.Redaction.PartialMaskingPolicy.Mask(orig.Type, orig.Value);
                        // If the detected value is exactly the expected masked suffix/prefix, it's allowed
                        if (expectedMasked.Contains(d.Value) && !string.Equals(expectedMasked, d.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            // The detected value is a substring of the masked version - check if it's the allowed visible part
                            // For TC *******8901, the visible part is 8901, but detector would need 11 digits to detect TC, so 8901 alone shouldn't be detected as TC
                            // So any detection of original type after partial masking indicates the masking was insufficient
                            continue;
                        }
                        // If the detected value equals the original full value, it's definitely residual
                        if (string.Equals(orig.Value, d.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                    // If not a direct original, check if it's a new PII that was not in original - still residual
                    // For partial, we allow that the output may contain the masked version, but not the original
                    // So only add if it's not an allowed partial fragment
                    var isOriginalFullValueStillPresent = originalDetections.Any(o => string.Equals(o.Value, d.Value, StringComparison.OrdinalIgnoreCase));
                    if (isOriginalFullValueStillPresent)
                        residualDetections.Add(new ResidualDetection { Type = d.Type, Value = d.Value, Context = d.Context, Confidence = d.Confidence, Location = d.Location, PageNumber = d.PageNumber });
                    else
                    {
                        // Check if the detected value contains any original's sensitive part that should have been hidden
                        bool containsSensitivePart = originalDetections.Any(o =>
                        {
                            var masked = EksimSafeCopy.Renderer.Redaction.PartialMaskingPolicy.Mask(o.Type, o.Value);
                            // The part that should be hidden is original without the visible suffix/prefix
                            var sensitivePart = GetSensitivePart(o.Type, o.Value, masked);
                            return !string.IsNullOrEmpty(sensitivePart) && d.Value.Contains(sensitivePart, StringComparison.OrdinalIgnoreCase);
                        });
                        if (containsSensitivePart)
                            residualDetections.Add(new ResidualDetection { Type = d.Type, Value = d.Value, Context = d.Context, Confidence = d.Confidence, Location = d.Location, PageNumber = d.PageNumber });
                    }
                }
            }
            else
            {
                residualDetections = detections
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
            }

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

    private static string GetSensitivePart(DetectionType type, string original, string masked)
    {
        // Return the part that should have been hidden (masked with *)
        var digits = new string(original.Where(char.IsDigit).ToArray());
        var maskedDigits = new string(masked.Where(char.IsDigit).ToArray());
        // For TC, sensitive is first 7 digits
        if (type == DetectionType.TcKimlikNo && digits.Length == 11)
            return digits.Substring(0, 7);
        if ((type == DetectionType.TesisatNo || type == DetectionType.AboneNo || type == DetectionType.SayacNo) && digits.Length >= 6)
            return digits.Substring(0, digits.Length - 4);
        return string.Empty;
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

private Result<(Document Document, IDetectionEngine DetectionEngine)> LoadDocumentForVerification(string filePath, DocumentFormat format, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, EksimSafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Pdf.PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Docx.DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Xlsx.XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Txt.TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Udf.UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Image.ImageDocumentIngestor>();

        // Add detectors for verification
        services.AddSingleton<ITurkishIdentityNumberDetector, EksimSafeCopy.Detectors.Detection.Detectors.TurkishIdentityNumberDetector>();
        services.AddSingleton<IPhoneNumberDetector, EksimSafeCopy.Detectors.Detection.Detectors.PhoneNumberDetector>();
        services.AddSingleton<IEmailDetector, EksimSafeCopy.Detectors.Detection.Detectors.EmailDetector>();
        services.AddSingleton<IBirthDateDetector, EksimSafeCopy.Detectors.Detection.Detectors.BirthDateDetector>();
        services.AddSingleton<IPersonNameDetector, EksimSafeCopy.Detectors.Detection.Detectors.PersonNameDetector>();
        services.AddSingleton<IAddressDetector, EksimSafeCopy.Detectors.Detection.Detectors.AddressDetector>();
        services.AddSingleton<IInstallationNumberDetector, EksimSafeCopy.Detectors.Detection.Detectors.InstallationNumberDetector>();

        services.AddSingleton<IReadOnlyList<IDetector>>(sp =>
        {
            return new List<IDetector>
            {
                sp.GetRequiredService<ITurkishIdentityNumberDetector>(),
                sp.GetRequiredService<IPhoneNumberDetector>(),
                sp.GetRequiredService<IEmailDetector>(),
                sp.GetRequiredService<IBirthDateDetector>(),
                sp.GetRequiredService<IPersonNameDetector>(),
                sp.GetRequiredService<IAddressDetector>(),
                sp.GetRequiredService<IInstallationNumberDetector>()
            }.AsReadOnly();
        });

        services.AddSingleton<IDetectionEngine>(sp =>
        {
            var detectors = sp.GetRequiredService<IReadOnlyList<IDetector>>();
            return new EksimSafeCopy.Detectors.Detection.Pipeline.DetectionEngine(detectors);
        });

        services.AddSingleton<IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();

        var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<IDocumentEngine>();
        var detectionEngine = provider.GetRequiredService<IDetectionEngine>();

        var formatResult = engine.DetectFormat(filePath);
        if (formatResult.IsFailure)
            return Result<(Document Document, IDetectionEngine DetectionEngine)>.Failure(formatResult.Error);

        // Use simple load with default options - the engine will auto-detect format
        var loadResult = engine.Load(filePath, cancellationToken);
        if (loadResult.IsFailure)
            return Result<(Document Document, IDetectionEngine DetectionEngine)>.Failure(loadResult.Error);

        return Result<(Document Document, IDetectionEngine DetectionEngine)>.Success((loadResult.Value, detectionEngine));
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