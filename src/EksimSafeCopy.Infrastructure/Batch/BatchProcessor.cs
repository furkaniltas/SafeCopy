namespace EksimSafeCopy.Infrastructure.Batch;

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Security;
using CoreHashAlgorithm = EksimSafeCopy.Core.Abstractions.HashAlgorithm;

internal static class BatchDiagLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EksimSafeCopy", "batch_diag.log");
    private static readonly object _lock = new();

    static BatchDiagLog()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); } catch { }
    }

    public static void Write(string msg, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        var tid = Thread.CurrentThread.ManagedThreadId;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var lineStr = $"[{timestamp}] TID={tid} {caller}:{line} | {msg}";
        try
        {
            lock (_lock) File.AppendAllText(LogPath, lineStr + Environment.NewLine);
        }
        catch { }
        Debug.WriteLine(lineStr);
    }
}

public sealed class BatchProcessor : IBatchProcessor
{
    private readonly IDocumentEngine _documentEngine;
    private readonly IDetectionEngine _detectionEngine;
    private readonly IRenderer _renderer;
    private readonly IRedactionPlanner _redactionPlanner;
    private readonly IVerificationEngine _verificationEngine;
    private readonly IFileSystem _fileSystem;
    private readonly IDocumentSecurityValidator _securityValidator;

    public BatchProcessor(
        IDocumentEngine documentEngine,
        IDetectionEngine detectionEngine,
        IRenderer renderer,
        IRedactionPlanner redactionPlanner,
        IVerificationEngine verificationEngine,
        IFileSystem fileSystem,
        IDocumentSecurityValidator securityValidator)
    {
        _documentEngine = documentEngine ?? throw new ArgumentNullException(nameof(documentEngine));
        _detectionEngine = detectionEngine ?? throw new ArgumentNullException(nameof(detectionEngine));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _redactionPlanner = redactionPlanner ?? throw new ArgumentNullException(nameof(redactionPlanner));
        _verificationEngine = verificationEngine ?? throw new ArgumentNullException(nameof(verificationEngine));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
    }

    public Result<BatchResult> Process(BatchRequest request, IProgress<BatchItem>? progress = null, CancellationToken cancellationToken = default)
    {
        return ProcessAsync(request, progress, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<Result<BatchResult>> ProcessAsync(BatchRequest request, IProgress<BatchItem>? progress = null, CancellationToken cancellationToken = default)
    {
        var totalSw = Stopwatch.StartNew();

        if (request == null)
            return Result<BatchResult>.Failure(Error.Validation("Batch request cannot be null"));
        if (request.InputPaths == null || request.InputPaths.Count == 0)
            return Result<BatchResult>.Failure(Error.Validation("Batch request must contain at least one input path"));
        if (request.MaxDegreeOfParallelism < 1)
            return Result<BatchResult>.Failure(Error.Validation("MaxDegreeOfParallelism must be >= 1"));

        // Deduplicate deterministically, preserve order, Windows paths case-insensitive
        var distinctPaths = request.InputPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctPaths.Count == 0)
            return Result<BatchResult>.Failure(Error.Validation("Batch request contains no valid paths"));

        var options = request.Options ?? new RenderOptions();
        var maxDop = request.MaxDegreeOfParallelism;
        var items = new BatchItem[distinctPaths.Count];
        var isCancelledGlobal = false;

        if (maxDop == 1)
        {
            for (int i = 0; i < distinctPaths.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    isCancelledGlobal = true;
                    // Mark remaining as Cancelled
                    for (int j = i; j < distinctPaths.Count; j++)
                    {
                        var cancelledItem = CreateCancelledItem(distinctPaths[j]);
                        items[j] = cancelledItem;
                        progress?.Report(cancelledItem);
                    }
                    break;
                }

                var path = distinctPaths[i];
                // Initial queued progress
                var queued = new BatchItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    InputPath = path,
                    FileName = Path.GetFileName(path),
                    DetectedFormat = DocumentFormat.Unknown,
                    State = BatchItemState.Queued,
                    StatusMessage = "Queued",
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow
                };
                progress?.Report(queued);

                var result = await ProcessSingleFileAsync(path, options, progress, cancellationToken).ConfigureAwait(false);
                items[i] = result;

                if (!request.ContinueOnError && result.State == BatchItemState.Failed)
                {
                    // Mark remaining as Skipped
                    for (int j = i + 1; j < distinctPaths.Count; j++)
                    {
                        var skipped = new BatchItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            InputPath = distinctPaths[j],
                            FileName = Path.GetFileName(distinctPaths[j]),
                            DetectedFormat = DocumentFormat.Unknown,
                            State = BatchItemState.Skipped,
                            StatusMessage = "Skipped due to previous failure (ContinueOnError=false)",
                            Error = Error.Validation("Skipped due to previous failure"),
                            Detections = Array.Empty<Detection>(),
                            CreatedAt = DateTime.UtcNow
                        };
                        items[j] = skipped;
                        progress?.Report(skipped);
                    }
                    break;
                }

                if (result.State == BatchItemState.Cancelled)
                    isCancelledGlobal = true;
            }
        }
        else
        {
            using var semaphore = new SemaphoreSlim(maxDop, maxDop);
            var tasks = new Task<BatchItem>[distinctPaths.Count];
            for (int i = 0; i < distinctPaths.Count; i++)
            {
                var idx = i;
                var path = distinctPaths[idx];
                tasks[idx] = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return CreateCancelledItem(path);

                        var queued = new BatchItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            InputPath = path,
                            FileName = Path.GetFileName(path),
                            DetectedFormat = DocumentFormat.Unknown,
                            State = BatchItemState.Queued,
                            StatusMessage = "Queued",
                            Detections = Array.Empty<Detection>(),
                            CreatedAt = DateTime.UtcNow
                        };
                        progress?.Report(queued);
                        return await ProcessSingleFileAsync(path, options, progress, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
            }

            try
            {
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                for (int i = 0; i < results.Length; i++) items[i] = results[i];
            }
            catch (OperationCanceledException)
            {
                isCancelledGlobal = true;
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null)
                        items[i] = CreateCancelledItem(distinctPaths[i]);
                }
            }
        }

        totalSw.Stop();

        // Ensure all slots filled
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null)
                items[i] = CreateCancelledItem(distinctPaths[i]);
        }

        var success = items.Count(x => x.State == BatchItemState.Success);
        var failed = items.Count(x => x.State == BatchItemState.Failed);
        var unsupported = items.Count(x => x.State == BatchItemState.Unsupported);
        var cancelled = items.Count(x => x.State == BatchItemState.Cancelled);

        var resultObj = new BatchResult
        {
            Items = items.ToList().AsReadOnly(),
            SuccessCount = success,
            FailedCount = failed,
            UnsupportedCount = unsupported,
            CancelledCount = cancelled,
            TotalDuration = totalSw.Elapsed,
            IsCancelled = isCancelledGlobal || cancelled > 0 || cancellationToken.IsCancellationRequested
        };

        return Result<BatchResult>.Success(resultObj);
    }

    private static BatchItem CreateCancelledItem(string path)
    {
        return new BatchItem
        {
            Id = Guid.NewGuid().ToString("N"),
            InputPath = path,
            FileName = Path.GetFileName(path),
            DetectedFormat = DocumentFormat.Unknown,
            State = BatchItemState.Cancelled,
            StatusMessage = "Cancelled",
            Error = Error.Cancelled("Batch processing was cancelled"),
            Detections = Array.Empty<Detection>(),
            CreatedAt = DateTime.UtcNow,
            Duration = TimeSpan.Zero
        };
    }

    private async Task<BatchItem> ProcessSingleFileAsync(string inputPath, RenderOptions options, IProgress<BatchItem>? progress, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var id = Guid.NewGuid().ToString("N");
        string? originalHash = null;
        string? outputHash = null;
        string? outputPath = null;
        DocumentFormat detectedFormat = DocumentFormat.Unknown;
        IReadOnlyList<Detection> detections = Array.Empty<Detection>();
        VerificationResult? verification = null;
        SecureTempWorkspace? tempWorkspace = null;

void Report(BatchItemState state, string message, Error? err = null, string? outPath = null, VerificationResult? ver = null, IReadOnlyList<Detection>? dets = null, DocumentFormat? fmt = null, TimeSpan? dur = null)
        {
            var item = new BatchItem
            {
                Id = id,
                InputPath = inputPath,
                FileName = Path.GetFileName(inputPath),
                DetectedFormat = fmt ?? detectedFormat,
                State = state,
                StatusMessage = message,
                Error = err,
                OutputPath = outPath ?? outputPath,
                VerificationResult = ver ?? verification,
                Detections = dets ?? detections,
                OriginalHash = originalHash,
                OutputHash = outputHash,
                CreatedAt = DateTime.UtcNow,
                Duration = dur ?? sw.Elapsed
            };

            BatchDiagLog.Write($"Report: State={state}, Msg={message}, OutputPath={item.OutputPath ?? "null"}, Detections={item.Detections?.Count ?? 0}");
            progress?.Report(item);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(BatchItemState.Processing, "Doğrulanıyor...");

            // 1. Validate input exists
            if (!File.Exists(inputPath))
            {
                sw.Stop();
                var err = Error.NotFound($"File not found: {inputPath}");
                Report(BatchItemState.Failed, err.Message, err, dur: sw.Elapsed);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = DocumentFormat.Unknown,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            // 2. Detect format via DocumentEngine.DetectFormat
            var formatResult = _documentEngine.DetectFormat(inputPath);
            if (formatResult.IsFailure)
            {
                sw.Stop();
                var err = Error.FormatError($"Format detection failed: {formatResult.Error.Message}");
                Report(BatchItemState.Failed, err.Message, err, dur: sw.Elapsed);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = DocumentFormat.Unknown,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }
            detectedFormat = formatResult.Value;

            // 3. Compute OriginalHash via IFileSystem.ComputeHash(SHA256)
            var hashResult = await Task.Run(() => _fileSystem.ComputeHash(inputPath, CoreHashAlgorithm.SHA256), cancellationToken).ConfigureAwait(false);
            if (hashResult.IsSuccess)
                originalHash = hashResult.Value;
            else
            {
                var alt = _securityValidator.ComputeFileHash(inputPath, CoreHashAlgorithm.SHA256);
                if (alt.IsSuccess) originalHash = alt.Value;
            }

            // 4. Create per-file temp workspace for isolated temp output
            tempWorkspace = new SecureTempWorkspace(_fileSystem);

            // 5. IDocumentEngine.Load with cancellation
            var loadResult = await Task.Run(() => _documentEngine.Load(inputPath, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (loadResult.IsFailure)
            {
                sw.Stop();
                // Check cancellation
                if (loadResult.Error.Code == "CANCELLED" || cancellationToken.IsCancellationRequested)
                {
                    Report(BatchItemState.Cancelled, "Cancelled during load", Error.Cancelled("Cancelled"), dur: sw.Elapsed);
                    CleanupTempWorkspace(tempWorkspace);
                    return new BatchItem
                    {
                        Id = id,
                        InputPath = inputPath,
                        FileName = Path.GetFileName(inputPath),
                        DetectedFormat = detectedFormat,
                        State = BatchItemState.Cancelled,
                        StatusMessage = "Cancelled",
                        Error = Error.Cancelled("Cancelled during load"),
                        OriginalHash = originalHash,
                        Detections = Array.Empty<Detection>(),
                        CreatedAt = DateTime.UtcNow,
                        Duration = sw.Elapsed
                    };
                }
                var err = loadResult.Error;
                Report(BatchItemState.Failed, $"Load failed: {err.Message}", err, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    OriginalHash = originalHash,
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var document = loadResult.Value;

            // 6. IDetectionEngine.Detect
            Report(BatchItemState.Processing, "PII taranıyor...", dets: detections);
            var detectResult = await Task.Run(() => _detectionEngine.Detect(document, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (detectResult.IsFailure)
            {
                if (detectResult.Error.Code == "CANCELLED" || cancellationToken.IsCancellationRequested)
                {
                    sw.Stop();
                    Report(BatchItemState.Cancelled, "Cancelled during detection", Error.Cancelled("Cancelled"), dur: sw.Elapsed);
                    CleanupTempWorkspace(tempWorkspace);
                    return new BatchItem
                    {
                        Id = id,
                        InputPath = inputPath,
                        FileName = Path.GetFileName(inputPath),
                        DetectedFormat = detectedFormat,
                        State = BatchItemState.Cancelled,
                        StatusMessage = "Cancelled",
                        Error = Error.Cancelled("Cancelled during detection"),
                        OriginalHash = originalHash,
                        Detections = Array.Empty<Detection>(),
                        CreatedAt = DateTime.UtcNow,
                        Duration = sw.Elapsed
                    };
                }
                sw.Stop();
                var err = detectResult.Error;
                Report(BatchItemState.Failed, $"Detection failed: {err.Message}", err, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    OriginalHash = originalHash,
                    Detections = Array.Empty<Detection>(),
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }
            detections = detectResult.Value;

            // 7. PDF/UDF secure-failure: if Pdf/Udf with detections.Any() => mark Unsupported
            if ((detectedFormat == DocumentFormat.Pdf || detectedFormat == DocumentFormat.Udf) && detections.Any())
            {
                sw.Stop();
                var err = Error.SecurityError($"{detectedFormat} redaction is not securely supported. No output generated.");
                Report(BatchItemState.Unsupported, err.Message, err, dets: detections, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Unsupported,
                    StatusMessage = err.Message,
                    Error = err,
                    Detections = detections,
                    OriginalHash = originalHash,
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            // If no detections, we still want to verify original preservation and produce output? Spec says per-file pipeline includes redaction→verification.
            // For zero detections, consider success with no redaction needed - but Renderer would fail on empty plan.
            // Handle zero detections as Success with verification but no output file needed? Instead create copy sanitized?
            // For security determinism, we will produce output only if detections exist and are redacted, otherwise copy file as SafeCopy with verification.
            // However to keep pipeline strict: if no detections, mark Success with no output? Let's create output as copy + sanitize metadata via renderer path.
            // Simplest: if no detections, skip render and consider Success with no output? But spec says Success with OutputPath.
            // We'll treat zero detections as Success with output being copy of original as SafeCopy (sanitized via renderer when possible, else fallback copy).

            // Determine output path: inputDir/input_SafeCopy.ext
            var dir = Path.GetDirectoryName(inputPath);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
            var ext = Path.GetExtension(inputPath);
            var baseOutputName = $"{nameWithoutExt}_SafeCopy{ext}";
            outputPath = Path.Combine(dir, baseOutputName);
            // Ensure not overwriting: if exists, create unique suffix
            outputPath = GetUniqueOutputPath(outputPath);

            // Use temp workspace file for atomic write then move
            var tempOutput = Path.Combine(tempWorkspace.OutputPath, Path.GetFileName(outputPath));

            // 8. Redaction: plan + render
            if (detections.Any())
            {
                Report(BatchItemState.Processing, "Maskeleme uygulanıyor...", dets: detections);
                var planResult = await Task.Run(() => _redactionPlanner.CreatePlan(document, detections, options, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (planResult.IsFailure)
                {
                    sw.Stop();
                    var err = planResult.Error;
                    Report(BatchItemState.Failed, $"Plan failed: {err.Message}", err, dets: detections, dur: sw.Elapsed);
                    CleanupTempWorkspace(tempWorkspace);
                    return new BatchItem
                    {
                        Id = id,
                        InputPath = inputPath,
                        FileName = Path.GetFileName(inputPath),
                        DetectedFormat = detectedFormat,
                        State = BatchItemState.Failed,
                        StatusMessage = err.Message,
                        Error = err,
                        Detections = detections,
                        OriginalHash = originalHash,
                        CreatedAt = DateTime.UtcNow,
                        Duration = sw.Elapsed
                    };
                }

                cancellationToken.ThrowIfCancellationRequested();

                // RenderToFile with cancellation - use tempOutput
                // IRenderer.RenderToFile expects document, detections, options, outputPath
                var renderResult = await Task.Run(() => _renderer.RenderToFile(document, detections, options, tempOutput, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (renderResult.IsFailure)
                {
                    // Check if security error
                    if (renderResult.Error.Code == "SECURITY_ERROR")
                    {
                        sw.Stop();
                        Report(BatchItemState.Unsupported, renderResult.Error.Message, renderResult.Error, dets: detections, dur: sw.Elapsed);
                        TryDeleteFile(tempOutput);
                        CleanupTempWorkspace(tempWorkspace);
                        return new BatchItem
                        {
                            Id = id,
                            InputPath = inputPath,
                            FileName = Path.GetFileName(inputPath),
                            DetectedFormat = detectedFormat,
                            State = BatchItemState.Unsupported,
                            StatusMessage = renderResult.Error.Message,
                            Error = renderResult.Error,
                            Detections = detections,
                            OriginalHash = originalHash,
                            CreatedAt = DateTime.UtcNow,
                            Duration = sw.Elapsed
                        };
                    }
                    sw.Stop();
                    Report(BatchItemState.Failed, $"Render failed: {renderResult.Error.Message}", renderResult.Error, dets: detections, dur: sw.Elapsed);
                    TryDeleteFile(tempOutput);
                    CleanupTempWorkspace(tempWorkspace);
                    return new BatchItem
                    {
                        Id = id,
                        InputPath = inputPath,
                        FileName = Path.GetFileName(inputPath),
                        DetectedFormat = detectedFormat,
                        State = BatchItemState.Failed,
                        StatusMessage = renderResult.Error.Message,
                        Error = renderResult.Error,
                        Detections = detections,
                        OriginalHash = originalHash,
                        CreatedAt = DateTime.UtcNow,
                        Duration = sw.Elapsed
                    };
                }

                // Move tempOutput to final outputPath atomically
                File.Move(tempOutput, outputPath, true);
            }
            else
            {
                // No detections: sanitize copy path - just copy file to outputPath and sanitize metadata if needed
                // Use renderer path for metadata sanitization where possible, else direct copy
                Report(BatchItemState.Processing, "Doğrulama öncesi kopya oluşturuluyor...");
                // Try to use renderer for sanitization, but if no ops, renderer returns FormatError. Fallback to file copy.
                // We'll directly copy with File.Copy via filesystem temp then move
                var planResult = await Task.Run(() => _redactionPlanner.CreatePlan(document, detections, options, cancellationToken), cancellationToken).ConfigureAwait(false);
                // plan will have 0 ops, renderer would fail, so we handle copy manually
                // Copy original to tempOutput then try to sanitize via renderer if possible? For now just copy.
                File.Copy(inputPath, tempOutput, true);
                // Attempt render even with empty detections for sanitization if redactor supports it
                // But to avoid failure, we just move tempOutput to outputPath
                File.Move(tempOutput, outputPath, true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 9. Verify OriginalHash unchanged after ingestion/redaction
            var postHash = await Task.Run(() => _fileSystem.ComputeHash(inputPath, CoreHashAlgorithm.SHA256), cancellationToken).ConfigureAwait(false);
            if (postHash.IsSuccess && originalHash != null && postHash.Value != originalHash)
            {
                sw.Stop();
                var err = Error.SecurityError("Original file was modified during processing");
                TryDeleteFile(outputPath);
                TryDeleteFile(tempOutput);
                Report(BatchItemState.Failed, err.Message, err, dets: detections, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    Detections = detections,
                    OriginalHash = originalHash,
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            // 10. IVerificationEngine.Verify on output
            BatchDiagLog.Write($"ProcessSingleFileAsync: Starting verification on {outputPath}");
            Report(BatchItemState.Processing, "Doğrulama yapılıyor...", outPath: outputPath, dets: detections);
            var verifyResult = await Task.Run(() => _verificationEngine.Verify(outputPath, detectedFormat, cancellationToken), cancellationToken).ConfigureAwait(false);
            BatchDiagLog.Write($"ProcessSingleFileAsync: Verification returned IsSuccess={verifyResult.IsSuccess}, Passed={verifyResult.Value?.Passed}, Residual={verifyResult.Value?.TotalResidualCount}");
            if (verifyResult.IsFailure)
            {
                sw.Stop();
                var err = verifyResult.Error;
                // Verification failure is not success - cleanup output
                TryDeleteFile(outputPath);
                TryDeleteFile(tempOutput);
                Report(BatchItemState.Failed, $"Verification failed: {err.Message}", err, dets: detections, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    Detections = detections,
                    OriginalHash = originalHash,
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            verification = verifyResult.Value;
            if (verification == null || !verification.Passed)
            {
                sw.Stop();
                var residualMsg = verification?.TotalResidualCount > 0 ? $" {verification.TotalResidualCount} residual PII found" : "verification returned null or failed";
                var err = Error.SecurityError($"Verification failed:{residualMsg}");
                TryDeleteFile(outputPath);
                TryDeleteFile(tempOutput);
                Report(BatchItemState.Failed, err.Message, err, ver: verification, dets: detections, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = err.Message,
                    Error = err,
                    VerificationResult = verification,
                    Detections = detections,
                    OriginalHash = originalHash,
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            // Compute OutputHash and enforce 10 SUCCESS invariants
            var outHashResult = await Task.Run(() => _fileSystem.ComputeHash(outputPath, CoreHashAlgorithm.SHA256), cancellationToken).ConfigureAwait(false);
            if (outHashResult.IsSuccess) outputHash = outHashResult.Value;

            // Enforce SUCCESS invariants
            bool outputExists = File.Exists(outputPath);
            bool outputDifferentFromInput = !string.Equals(outputPath, inputPath, StringComparison.OrdinalIgnoreCase) && outputExists;
            bool hashPreserved = postHash.IsSuccess && originalHash != null && postHash.Value == originalHash;
            bool verificationPassed = verification.Passed;
            bool zeroResidual = verification.TotalResidualCount == 0;
            bool zeroCritical = verification.CriticalResidualCount == 0;
            bool noMetadataIssues = verification.MetadataIssues.Count == 0;
            bool noHiddenIssues = verification.HiddenContentIssues.Count == 0;
            bool outputHashExists = !string.IsNullOrEmpty(outputHash);

            BatchDiagLog.Write($"ProcessSingleFileAsync: OutputHash={outputHash ?? "null"}, outputExists={outputExists}, different={outputDifferentFromInput}, hashPreserved={hashPreserved}, passed={verificationPassed}, residual={verification.TotalResidualCount}");

            bool allInvariants = outputExists && outputDifferentFromInput && hashPreserved && verificationPassed && zeroResidual && zeroCritical && noMetadataIssues && noHiddenIssues && outputHashExists;

            if (!allInvariants)
            {
                // Invariant failure → treat as Failed, delete output, do not present as success
                var invariantError = Error.SecurityError(
                    $"SUCCESS invariant failure: outputExists={outputExists}, different={outputDifferentFromInput}, hashPreserved={hashPreserved}, passed={verificationPassed}, residual={verification.TotalResidualCount}, critical={verification.CriticalResidualCount}, metadata={verification.MetadataIssues.Count}, hidden={verification.HiddenContentIssues.Count}, outputHashExists={outputHashExists}");
                TryDeleteFile(outputPath);
                TryDeleteFile(tempOutput);
                sw.Stop();
                BatchDiagLog.Write($"ProcessSingleFileAsync: INVARIANT FAILURE - {invariantError.Message}");
                Report(BatchItemState.Failed, invariantError.Message, invariantError, ver: verification, dets: detections, dur: sw.Elapsed);
                CleanupTempWorkspace(tempWorkspace);
                return new BatchItem
                {
                    Id = id,
                    InputPath = inputPath,
                    FileName = Path.GetFileName(inputPath),
                    DetectedFormat = detectedFormat,
                    State = BatchItemState.Failed,
                    StatusMessage = invariantError.Message,
                    Error = invariantError,
                    VerificationResult = verification,
                    Detections = detections,
                    OriginalHash = originalHash,
                    CreatedAt = DateTime.UtcNow,
                    Duration = sw.Elapsed
                };
            }

            sw.Stop();
            BatchDiagLog.Write($"ProcessSingleFileAsync: ALL INVARIANTS PASSED - Reporting SUCCESS, OutputPath={outputPath}");
            Report(BatchItemState.Success, "Tamamlandı", outPath: outputPath, ver: verification, dets: detections, dur: sw.Elapsed);
            CleanupTempWorkspace(tempWorkspace);

            return new BatchItem
            {
                Id = id,
                InputPath = inputPath,
                FileName = Path.GetFileName(inputPath),
                DetectedFormat = detectedFormat,
                State = BatchItemState.Success,
                StatusMessage = "Tamamlandı",
                OutputPath = outputPath,
                VerificationResult = verification,
                Detections = detections,
                OriginalHash = originalHash,
                OutputHash = outputHash,
                CreatedAt = DateTime.UtcNow,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            if (outputPath != null) TryDeleteFile(outputPath);
            CleanupTempWorkspace(tempWorkspace);
            var err = Error.Cancelled("Processing cancelled");
            Report(BatchItemState.Cancelled, "Cancelled", err, dur: sw.Elapsed);
            return new BatchItem
            {
                Id = id,
                InputPath = inputPath,
                FileName = Path.GetFileName(inputPath),
                DetectedFormat = detectedFormat,
                State = BatchItemState.Cancelled,
                StatusMessage = "Cancelled",
                Error = err,
                Detections = detections,
                OriginalHash = originalHash,
                CreatedAt = DateTime.UtcNow,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (outputPath != null) TryDeleteFile(outputPath);
            CleanupTempWorkspace(tempWorkspace);
            var err = Error.Internal($"Unexpected error: {ex.Message}", ex);
            Report(BatchItemState.Failed, err.Message, err, dur: sw.Elapsed);
            return new BatchItem
            {
                Id = id,
                InputPath = inputPath,
                FileName = Path.GetFileName(inputPath),
                DetectedFormat = detectedFormat,
                State = BatchItemState.Failed,
                StatusMessage = err.Message,
                Error = err,
                Detections = detections,
                OriginalHash = originalHash,
                CreatedAt = DateTime.UtcNow,
                Duration = sw.Elapsed
            };
        }
    }

    private static string GetUniqueOutputPath(string initialPath)
    {
        if (!File.Exists(initialPath)) return initialPath;
        var dir = Path.GetDirectoryName(initialPath)!;
        var name = Path.GetFileNameWithoutExtension(initialPath);
        var ext = Path.GetExtension(initialPath);
        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return initialPath + "_" + Guid.NewGuid().ToString("N")[..6];
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void CleanupTempWorkspace(SecureTempWorkspace? ws)
    {
        try { ws?.Cleanup(); ws?.Dispose(); } catch { }
    }
}
