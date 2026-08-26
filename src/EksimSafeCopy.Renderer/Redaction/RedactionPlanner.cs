namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;

public sealed class RedactionPlanner : IRedactionPlanner
{
    private readonly IRedactionStrategy _strategy;

    public RedactionPlanner(IRedactionStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }

    public Result<RedactionPlan> CreatePlan(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = new List<RedactionOperation>();

            foreach (var detection in detections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (detection.State == DetectionState.Deselected || detection.State == DetectionState.FalsePositive)
                    continue;

                if (detection.Confidence < options.ConfidenceThreshold)
                    continue;

                var strategy = _strategy;
                var replacementText = strategy.GetReplacementText(detection.Type, options);

                var operation = new RedactionOperation
                {
                    DetectionId = detection.Id,
                    DetectionType = detection.Type,
                    PageNumber = detection.PageNumber,
                    TextSpan = detection.TextSpan,
                    BoundingBox = detection.Location,
                    CoordinateSystem = detection.Source?.Format != null ? null : null,
                    Strategy = _strategy.Type,
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
}