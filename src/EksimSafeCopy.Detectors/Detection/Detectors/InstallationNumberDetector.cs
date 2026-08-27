namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class InstallationNumberDetector : BaseDetector, IInstallationNumberDetector
{
    public override DetectionType Type => DetectionType.TesisatNo;
    public override string Name => "Installation Number Detector";
    public override string Description => "Detects installation/subscription numbers in Turkish utility documents";

private static readonly Regex InstallationPattern = new(
        @"(?:tesisat|abone|sayac|sayaç)\s*(?:no|numaras[iı]|numara|#)?\s*[:\-]?\s*[A-Z0-9]{6,20}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> TurkishInstallationLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "tesisat no", "tesisat numarası", "tesisat numara", "tesisat",
        "sayaç no", "sayaç numarası", "sayac no", "sayac numarası",
        "abone no", "abone numarası", "abone numara",
        "sözleşme no", "müşteri no", "dosya no", "başvuru no"
    };

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var matches = InstallationPattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Extract just the number part from the match (last alphanumeric sequence of 6+ chars)
            var candidate = ExtractNumberFromMatch(match.Value);
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < 6) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);
            
            // Also check for labels WITHIN the match text itself
            var matchContextFeatures = AnalyzeMatchContext(match.Value);
            
            // Combine context features - if either has strong label, it's valid
            var combinedHasStrongLabel = contextFeatures.HasStrongLabel || matchContextFeatures.HasStrongLabel;
            var combinedHasNegativeLabel = contextFeatures.HasNegativeLabel || matchContextFeatures.HasNegativeLabel;
            
            if (!combinedHasStrongLabel)
                continue;

            double confidence = CalculateConfidence(candidate, contextFeatures, matchContextFeatures);
            
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
                    ["has_label_context"] = combinedHasStrongLabel,
                    ["label_score"] = Math.Max(contextFeatures.LabelScore, matchContextFeatures.LabelScore),
                    ["raw_format"] = match.Value
                });

            detections.Add(detection);
        }

        // Fallback for XLSX where header and data are in separate TextBlocks (e.g., A1: Tesisat Numarası and A2: 12222222)
        // Check each TextBlock that looks like a header, and find data blocks in the same column
        var headerBlocks = page.TextBlocks.Where(b => b.Text.Contains("Tesisat", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Abone", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Sayaç", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Sayac", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var header in headerBlocks)
        {
            if (!header.Properties.TryGetValue("CellReference", out var headerRefObj) || headerRefObj is not string headerRef) continue;
            var headerCol = new string(headerRef.Where(char.IsLetter).ToArray());
            if (string.IsNullOrEmpty(headerCol)) continue;

            // Find data blocks in the same column (e.g., A2 for header A1)
            var columnDataBlocks = page.TextBlocks.Where(b =>
                b.Properties.TryGetValue("CellReference", out var refObj) && refObj is string r && r.StartsWith(headerCol, StringComparison.OrdinalIgnoreCase) && r != headerRef
                && !string.IsNullOrWhiteSpace(b.Text) && b.Text.Contains(":")
            ).ToList();

            foreach (var dataBlock in columnDataBlocks)
            {
                // Extract value part after ": "
                var colonIdx = dataBlock.Text.IndexOf(": ");
                var value = colonIdx >= 0 ? dataBlock.Text.Substring(colonIdx + 2).Trim() : dataBlock.Text.Trim();
                if (string.IsNullOrWhiteSpace(value) || value.Length < 6) continue;
                // Check if value is alphanumeric 6-20 and not already detected
                if (!Regex.IsMatch(value, @"^[A-Z0-9]{6,20}$", RegexOptions.IgnoreCase)) continue;
                if (detections.Any(d => d.Value == value)) continue;

                // Find the span for this value within the block
                var valueSpan = dataBlock.Spans.FirstOrDefault(s => s.Text == value);
                if (valueSpan == null) continue;

                var detection = CreateDetection(
                    value: value,
                    confidence: 0.75,
                    pageNumber: page.PageNumber,
                    textSpan: valueSpan,
                    context: header.Text + " " + dataBlock.Text,
                    properties: new Dictionary<string, object> { ["header_cell"] = headerRef, ["data_cell"] = dataBlock.Properties["CellReference"] });

                detections.Add(detection);
            }
        }

        return detections;
    }

    private ContextFeatures AnalyzeMatchContext(string matchText)
    {
        var features = new ContextFeatures();
        var lowerText = matchText.ToLowerInvariant();
        
        // Check for installation labels within the match text itself
        foreach (var label in TurkishInstallationLabels)
        {
            if (lowerText.Contains(label.ToLowerInvariant()))
            {
                features.HasStrongLabel = true;
                features.LabelScore = Math.Max(features.LabelScore, 1.0);
                break;
            }
        }
        
        // Check for negative keywords
        var negativeKeywords = new[] { "test", "örnek", "example", "sample", "fake", "sahte" };
        foreach (var keyword in negativeKeywords)
        {
            if (lowerText.Contains(keyword.ToLowerInvariant()))
            {
                features.HasNegativeLabel = true;
                break;
            }
        }
        
        return features;
    }

    private static string ExtractNumberFromMatch(string match)
    {
        var numberMatch = Regex.Match(match, @"[A-Z0-9]{6,20}");
        return numberMatch.Success ? numberMatch.Value : string.Empty;
    }

    private double CalculateConfidence(string number, ContextFeatures context, ContextFeatures matchContext)
    {
        double confidence = 0.7;

        if (context.HasStrongLabel || matchContext.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.2);

        if (context.HasNegativeLabel || matchContext.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.3);

        if (number.Length >= 10)
            confidence = Math.Min(1.0, confidence + 0.1);

        return confidence;
    }
}