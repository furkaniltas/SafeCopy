namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;

public abstract class BaseDetector : IDetector
{
    public abstract DetectionType Type { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public bool IsEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.5;

    protected readonly NormalizationOptions NormalizationOptions = new()
    {
        NormalizeNewlines = true,
        CollapseWhitespace = true,
        CollapseNewlines = true,
        FixTurkishWhitespace = true,
        TrimEdges = true
    };

    protected readonly ContextAnalyzer ContextAnalyzer = new();

    public virtual Result<IReadOnlyList<Detection>> Detect(Document document, CancellationToken cancellationToken = default)
    {
        try
        {
            var detections = new List<Detection>();

            foreach (var page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageText = page.Text;
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                var normalized = TextNormalizer.NormalizeWithPositionTracking(pageText, NormalizationOptions);
                
                var pageDetections = DetectOnPage(page, normalized, cancellationToken);
                detections.AddRange(pageDetections);
            }

            var postProcessed = PostProcessDetections(detections, document);
            
            return Result<IReadOnlyList<Detection>>.Success(postProcessed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<IReadOnlyList<Detection>>.Failure(Error.Cancelled("Detection was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<Detection>>.Failure(Error.Internal($"Detection failed: {ex.Message}", ex));
        }
    }

    public virtual async Task<Result<IReadOnlyList<Detection>>> DetectAsync(Document document, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Detect(document, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    protected abstract IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken);

    protected virtual IReadOnlyList<Detection> PostProcessDetections(IReadOnlyList<Detection> detections, Document document)
    {
        return detections
            .Where(d => d.Confidence >= ConfidenceThreshold)
            .OrderBy(d => d.PageNumber)
            .ThenBy(d => d.TextSpan?.StartIndex ?? 0)
            .ToList();
    }

    protected Detection CreateDetection(
        string value,
        double confidence,
        int pageNumber,
        TextSpan? textSpan = null,
        BoundingBox? location = null,
        string? context = null,
        DetectionSource source = DetectionSource.NativeText,
        Dictionary<string, object>? properties = null)
    {
        var detection = new Detection
        {
            Type = Type,
            Value = value,
            Confidence = Math.Clamp(confidence, 0, 1),
            ConfidenceLevel = ConfidenceToLevel(Math.Clamp(confidence, 0, 1)),
            PageNumber = pageNumber,
            TextSpan = textSpan,
            Location = location ?? BoundingBox.Empty,
            DetectionSource = source,
            Context = context ?? string.Empty,
            State = DetectionState.Detected,
            Properties = properties ?? new Dictionary<string, object>()
        };

        return detection;
    }

    protected static ConfidenceLevel ConfidenceToLevel(double confidence)
    {
        return confidence switch
        {
            >= 0.9 => ConfidenceLevel.Critical,
            >= 0.7 => ConfidenceLevel.High,
            >= 0.4 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };
    }

    protected ContextWindow GetContextWindow(NormalizedText normalized, int matchStart, int matchLength, int windowSize = 100)
    {
        return ContextWindow.Create(normalized.Text, matchStart, matchLength, windowSize);
    }

    protected ContextFeatures AnalyzeContext(ContextWindow window)
    {
        return ContextAnalyzer.Analyze(window, Type);
    }

    protected double CalculateBaseConfidence(double patternConfidence, ContextFeatures context)
    {
        var confidence = patternConfidence;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.2);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.0, confidence - 0.3);

        return confidence;
    }

    protected static bool SpansOverlap(TextSpan a, TextSpan b)
    {
        return a.StartIndex < b.EndIndex && b.StartIndex < a.EndIndex;
    }
}