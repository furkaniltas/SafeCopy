namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class TurkishIdentityNumberDetector : BaseDetector, ITurkishIdentityNumberDetector
{
    public override DetectionType Type => DetectionType.TcKimlikNo;
    public override string Name => "Turkish Identity Number Detector";
    public override string Description => "Detects Turkish Republic Identity Numbers (T.C. Kimlik No) with checksum validation";

    private static readonly Regex CandidatePattern = new(@"\b[1-9]\d{10}\b", RegexOptions.Compiled);
    private static readonly Regex SeparatedPattern = new(@"\b[1-9]\d{2}[\s\-\.]?\d{3}[\s\-\.]?\d{3}[\s\-\.]?\d{2}\b", RegexOptions.Compiled);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var matches = CandidatePattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var candidate = match.Value;
            if (!IsValidTurkishIdentityNumber(candidate)) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);
            
            double confidence = CalculateConfidence(candidate, contextFeatures, match.Value.Length == candidate.Length);
            
            var originalStart = normalizedText.MapToOriginalPosition(match.Index);
            var originalEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);
            
            var textSpan = new TextSpan
            {
                StartIndex = originalStart,
                Length = originalEnd - originalStart,
                Text = candidate,
                BoundingBox = BoundingBox.Empty
            };

            var detection = CreateDetection(
                value: candidate,
                confidence: confidence,
                pageNumber: page.PageNumber,
                textSpan: textSpan,
                context: contextWindow.FullText,
                properties: new Dictionary<string, object>
                {
                    ["checksum_valid"] = true,
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore,
                    ["format_type"] = "continuous"
                });

            detections.Add(detection);
        }

        var separatedMatches = SeparatedPattern.Matches(text);
        foreach (Match match in separatedMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var candidate = Regex.Replace(match.Value, @"[\s\-\.]", "");
            if (!IsValidTurkishIdentityNumber(candidate)) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);
            
            double confidence = CalculateConfidence(candidate, contextFeatures, false);
            
            var originalStart = normalizedText.MapToOriginalPosition(match.Index);
            var originalEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);
            
            var textSpan = new TextSpan
            {
                StartIndex = originalStart,
                Length = originalEnd - originalStart,
                Text = match.Value,
                BoundingBox = BoundingBox.Empty
            };

            var detection = CreateDetection(
                value: candidate,
                confidence: confidence,
                pageNumber: page.PageNumber,
                textSpan: textSpan,
                context: contextWindow.FullText,
                properties: new Dictionary<string, object>
                {
                    ["checksum_valid"] = true,
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore,
                    ["format_type"] = "separated"
                });

            detections.Add(detection);
        }

        return detections;
    }

    private static bool IsValidTurkishIdentityNumber(string number)
    {
        if (number.Length != 11) return false;
        if (!long.TryParse(number, out _)) return false;
        if (number[0] == '0') return false;

        int[] digits = number.Select(c => c - '0').ToArray();

        int oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        int evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        int check10 = (oddSum * 7 - evenSum) % 10;
        if (check10 < 0) check10 += 10;
        if (check10 != digits[9]) return false;

        int totalSum = digits.Take(10).Sum();
        int check11 = totalSum % 10;
        if (check11 != digits[10]) return false;

        return true;
    }

    private double CalculateConfidence(string number, ContextFeatures context, bool isContinuous)
    {
        double confidence = 0.85;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.1);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.4);

        if (!isContinuous)
            confidence = Math.Max(0.5, confidence - 0.1);

        return confidence;
    }
}