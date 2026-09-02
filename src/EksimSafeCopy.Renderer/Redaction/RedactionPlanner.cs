namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;

public sealed class RedactionPlanner : IRedactionPlanner
{
    private readonly IRedactionStrategy _strategy;
    private readonly IEnumerable<IRedactionStrategy> _allStrategies;

    public RedactionPlanner(IRedactionStrategy strategy, IEnumerable<IRedactionStrategy>? allStrategies = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _allStrategies = allStrategies ?? Array.Empty<IRedactionStrategy>();
    }

    private IRedactionStrategy ResolveStrategy(RenderOptions options)
    {
        // For FullRedaction with UseTypePlaceholder, use TypeLabel to get "[TC_KIMLIK_NO]" style placeholders
        // This preserves existing test expectations where new RenderOptions() should produce placeholders, not block chars
        if (options.Mode == MaskingMode.FullRedaction && options.UseTypePlaceholder)
            return _allStrategies.FirstOrDefault(s => s.Type == RedactionStrategy.TypeLabel) ?? _strategy;
        var desired = options.Mode switch
        {
            MaskingMode.FullRedaction => RedactionStrategy.FullRedaction,
            MaskingMode.PartialMask => RedactionStrategy.PartialMask,
            MaskingMode.Placeholder => RedactionStrategy.Placeholder,
            _ => _strategy.Type
        };
        var found = _allStrategies.FirstOrDefault(s => s.Type == desired);
        return found ?? _strategy;
    }

    public Result<RedactionPlan> CreatePlan(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = new List<RedactionOperation>();

            // Sort definitive first to prioritize overlap deduplication
            var sorted = detections
                .Where(d => d.State != DetectionState.Deselected && d.State != DetectionState.FalsePositive)
                .Where(d => d.Confidence >= options.ConfidenceThreshold)
                .OrderBy(d => d.Type == DetectionType.PossiblePersonalData ? 1 : 0)
                .ThenBy(d => d.PageNumber)
                .ThenBy(d => d.TextSpan?.StartIndex ?? 0)
                .ToList();

            foreach (var detection in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (detection.State == DetectionState.Deselected || detection.State == DetectionState.FalsePositive)
                    continue;

                if (detection.Confidence < options.ConfidenceThreshold)
                    continue;

                // Overlap deduplication: only PossiblePersonalData that overlaps with already-planned definitive is skipped (definitive prioritized)
                if (detection.Type == DetectionType.PossiblePersonalData && detection.TextSpan != null && operations.Any(o => o.PageNumber == detection.PageNumber && o.TextSpan != null && SpansOverlap(o.TextSpan, detection.TextSpan)))
                    continue;

                string replacementText;
                RedactionStrategy strategyType;
                if (detection.Type == DetectionType.PossiblePersonalData)
                {
                    // PossiblePersonalData: fail-secure, always [OLASI_KİŞİSEL_VERİ] when selected, for both Full and Partial
                    replacementText = "[OLASI_KİŞİSEL_VERİ]";
                    strategyType = RedactionStrategy.TypeLabel;
                }
                else
                {
                    var strategy = ResolveStrategy(options);
                    replacementText = strategy.GetReplacementText(detection.Type, detection.Value ?? string.Empty, options);
                    strategyType = strategy.Type;
                }

                var operation = new RedactionOperation
                {
                    DetectionId = detection.Id,
                    DetectionType = detection.Type,
                    PageNumber = detection.PageNumber,
                    TextSpan = detection.TextSpan,
                    BoundingBox = detection.Location,
                    CoordinateSystem = detection.Source?.Format != null ? null : null,
                    Strategy = strategyType,
                    ReplacementText = replacementText,
                    Confidence = detection.Confidence,
                    State = RedactionOperationState.Pending
                };

                operations.Add(operation);
            }

            var plan = new RedactionPlan
            {
                DocumentId = document.Id,
                Operations = operations.OrderBy(o => o.PageNumber).ThenBy(o => o.TextSpan?.StartIndex ?? 0).ToList().AsReadOnly(),
                Format = document.Format
            };

            return Result<RedactionPlan>.Success(plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<RedactionPlan>.Failure(Error.Cancelled("Redaction planning was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<RedactionPlan>.Failure(Error.Internal($"Redaction planning failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<RedactionPlan>> CreatePlanAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => CreatePlan(document, detections, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static bool SpansOverlap(TextSpan a, TextSpan b)
    {
        return a.StartIndex < b.EndIndex && b.StartIndex < a.EndIndex;
    }
}