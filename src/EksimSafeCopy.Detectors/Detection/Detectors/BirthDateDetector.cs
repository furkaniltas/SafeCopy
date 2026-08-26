namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class BirthDateDetector : BaseDetector, IBirthDateDetector
{
    public override DetectionType Type => DetectionType.Date;
    public override string Name => "Birth Date Detector";
    public override string Description => "Detects birth dates in Turkish document formats";

    private static readonly Regex DotDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\.(?:0[1-9]|1[0-2])\.(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex SlashDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\/(?:0[1-9]|1[0-2])\/(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex DashDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\-(?:0[1-9]|1[0-2])\-(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex IsoDatePattern = new(
        @"\b(?:19[0-9]{2}|20[0-2]\d)\-(?:0[1-9]|1[0-2])\-(?:0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var allPatterns = new[]
        {
            (DotDatePattern, "dot"),
            (SlashDatePattern, "slash"),
            (DashDatePattern, "dash"),
            (IsoDatePattern, "iso")
        };

        foreach (var (pattern, format) in allPatterns)
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var candidate = match.Value;
                if (!IsValidDate(candidate, format)) continue;

                var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
                var contextFeatures = AnalyzeContext(contextWindow);
                
                if (!contextFeatures.HasStrongLabel && !HasBirthDateContext(contextWindow))
                    continue;

                double confidence = CalculateConfidence(candidate, contextFeatures);
                
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
                        ["format"] = format,
                        ["has_birth_label"] = contextFeatures.HasStrongLabel,
                        ["label_score"] = contextFeatures.LabelScore
                    });

                detections.Add(detection);
            }
        }

        return detections;
    }

    private static bool IsValidDate(string dateStr, string format)
    {
        try
        {
            DateTime date;
            if (format == "iso")
            {
                date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                var parts = dateStr.Split('.', '/', '-');
                if (parts.Length != 3) return false;
                
                int day = int.Parse(parts[0]);
                int month = int.Parse(parts[1]);
                int year = int.Parse(parts[2]);
                
                date = new DateTime(year, month, day);
            }

            if (date > DateTime.Now) return false;
            if (date.Year < 1900) return false;
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasBirthDateContext(ContextWindow window)
    {
        var beforeContext = window.GetBeforeContext(30).ToLowerInvariant();
        var afterContext = window.GetAfterContext(30).ToLowerInvariant();
        
        var birthKeywords = new[] { 
            "doğum", "dogum", "d.tarihi", "d.t", "d.tarih", "dtarih",
            "anne", "baba", "babaanne", "anneanne", "büyükbaba", "büyükanne", "buyukbaba", "buyukanne"
        };
        
        return birthKeywords.Any(k => beforeContext.Contains(k) || afterContext.Contains(k));
    }

    private double CalculateConfidence(string date, ContextFeatures context)
    {
        double confidence = 0.6;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.3);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.4);

        return confidence;
    }
}