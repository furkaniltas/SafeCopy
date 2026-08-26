namespace EksimSafeCopy.Renderer;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Renderer.Redaction;
using EksimSafeCopy.Renderer.Verification;
using Microsoft.Extensions.DependencyInjection;

public sealed class DocumentRenderer : IRenderer
{
    private readonly IRedactionPlanner _planner;
    private readonly IReadOnlyList<IRedactor> _redactors;
    private readonly EksimSafeCopy.Core.Abstractions.IVerificationEngine _verificationEngine;

    public DocumentFormat TargetFormat => DocumentFormat.Unknown;

    public DocumentRenderer(
        IRedactionPlanner planner,
        IEnumerable<IRedactor> redactors,
        EksimSafeCopy.Core.Abstractions.IVerificationEngine verificationEngine)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _redactors = redactors?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(redactors));
        _verificationEngine = verificationEngine ?? throw new ArgumentNullException(nameof(verificationEngine));
    }

    public Result<byte[]> Render(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var planResult = _planner.CreatePlan(document, detections, options, cancellationToken);
            if (planResult.IsFailure)
                return Result<byte[]>.Failure(planResult.Error);

            var plan = planResult.Value;

            if (!plan.Operations.Any())
            {
                // No redactions needed, return original document bytes
                return Result<byte[]>.Failure(Error.FormatError("No redaction operations needed"));
            }

            var redactor = _redactors.FirstOrDefault(r => r.TargetFormat == document.Format);
            if (redactor == null)
            {
                return Result<byte[]>.Failure(Error.FormatError($"No redactor available for format: {document.Format}"));
            }

            // Load original document bytes
            var documentBytes = LoadDocumentBytes(document);
            if (documentBytes == null)
            {
                return Result<byte[]>.Failure(Error.Internal("Failed to load document bytes for redaction"));
            }

            var result = redactor.Redact(documentBytes, planResult.Value, options, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("Rendering was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"Rendering failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RenderAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Render(document, detections, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result RenderToFile(Document document, IReadOnlyList<Detection> detections, RenderOptions options, string outputPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var planResult = _planner.CreatePlan(document, detections, options, cancellationToken);
            if (planResult.IsFailure)
                return Result.Failure(planResult.Error);

            var plan = planResult.Value;

            var redactor = _redactors.FirstOrDefault(r => r.TargetFormat == document.Format);
            if (redactor == null)
            {
                return Result.Failure(Error.FormatError($"No redactor available for format: {document.Format}"));
            }

            var documentBytes = LoadDocumentBytes(document);
            if (documentBytes == null)
            {
                return Result.Failure(Error.Internal("Failed to load document bytes for redaction"));
            }

            var result = redactor.Redact(documentBytes, plan, options, cancellationToken);
            if (result.IsFailure)
                return Result.Failure(result.Error);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir!);

            var tempPath = outputPath + ".tmp";
            File.WriteAllBytes(tempPath, result.Value);
            File.Move(tempPath, outputPath, true);

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(Error.Cancelled("Rendering was cancelled"));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Rendering to file failed: {ex.Message}", ex));
        }
    }

    public async Task<Result> RenderToFileAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, string outputPath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RenderToFile(document, detections, options, outputPath, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private byte[]? LoadDocumentBytes(Document document)
    {
        if (string.IsNullOrEmpty(document.Source.FilePath) || !File.Exists(document.Source.FilePath))
            return null;

        return File.ReadAllBytes(document.Source.FilePath);
    }
}

public static class RendererModule
{
    public static IServiceCollection AddRenderer(this IServiceCollection services)
    {
        // Redaction strategies
        services.AddSingleton<IRedactionStrategy, Redaction.DefaultRedactionStrategy>();
        services.AddSingleton<IRedactionStrategy, Redaction.FullRedactionStrategy>();
        services.AddSingleton<IRedactionStrategy, Redaction.PlaceholderStrategy>();
        services.AddSingleton<IRedactionStrategy, Redaction.PartialMaskStrategy>();

        // Redaction planner
        services.AddSingleton<IRedactionPlanner, Redaction.RedactionPlanner>();

        // Format-specific redactors
        services.AddSingleton<IRedactor, Redaction.TxtRedactor>();
        services.AddSingleton<IRedactor, Redaction.DocxRedactor>();
        services.AddSingleton<IRedactor, Redaction.XlsxRedactor>();
        services.AddSingleton<IRedactor, Redaction.PdfRedactor>();
        services.AddSingleton<IRedactor, Redaction.ImageRedactor>();
        services.AddSingleton<IRedactor, Redaction.UdfRedactor>();

        // Verification engine
        services.AddSingleton<IVerificationEngine, Verification.VerificationEngine>();

        // Renderer
        services.AddSingleton<IRenderer>(sp =>
        {
            var planner = sp.GetRequiredService<IRedactionPlanner>();
            var redactors = sp.GetServices<IRedactor>().ToList();
            var verificationEngine = sp.GetRequiredService<IVerificationEngine>();
            
            return new DocumentRenderer(planner, redactors, verificationEngine);
        });

        return services;
    }
}