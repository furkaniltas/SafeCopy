namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class PhoneNumberDetector : BaseDetector, IPhoneNumberDetector
{
    public override DetectionType Type => DetectionType.Phone;
    public override string Name => "Turkish Phone Number Detector";
    public override string Description => "Detects Turkish phone numbers in various formats";

    private static readonly Regex MobilePattern = new(
        @"\b(?:(?:\+?90|0090)[\s\-\.]?)?(?:\(?0?5\d{2}\)?[\s\-\.]?)?\d{3}[\s\-\.]?\d{2}[\s\-\.]?\d{2}\b(?!\s*\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LandlinePattern = new(
        @"\b(?:(?:\+?90|0090)[\s\-\.]?)?(?:\(?0?\d{3}\)?[\s\-\.]?)?\d{3}[\s\-\.]?\d{2}[\s\-\.]?\d{2}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var matches = MobilePattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var candidate = NormalizePhoneNumber(match.Value);
            if (!IsValidMobileNumber(candidate)) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);
            
            double confidence = CalculateConfidence(candidate, contextFeatures, true);
            
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
                    ["phone_type"] = "mobile",
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore,
                    ["raw_format"] = match.Value
                });

            detections.Add(detection);
        }

        var landlineMatches = LandlinePattern.Matches(text);
        foreach (Match match in landlineMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var candidate = NormalizePhoneNumber(match.Value);
            if (!IsValidLandlineNumber(candidate)) continue;

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
                    ["phone_type"] = "landline",
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore,
                    ["raw_format"] = match.Value
                });

            detections.Add(detection);
        }

        return detections;
    }

    private static string NormalizePhoneNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("90")) digits = digits[2..];
        if (digits.StartsWith("0090")) digits = digits[4..];
        if (digits.StartsWith("0")) digits = digits[1..];
        return digits;
    }

    private static bool IsValidMobileNumber(string normalized)
    {
        if (normalized.Length != 10) return false;
        if (!normalized.StartsWith("5")) return false;
        return true;
    }

    private static bool IsValidLandlineNumber(string normalized)
    {
        if (normalized.Length != 10) return false;
        if (normalized.StartsWith("5")) return false;
        return true;
    }

    private double CalculateConfidence(string number, ContextFeatures context, bool isMobile)
    {
        double confidence = isMobile ? 0.9 : 0.8;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.1);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.3);

        return confidence;
    }
}