namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;
using EksimSafeCopy.Detectors;

public sealed class IbanDetector : BaseDetector, IIbanDetector
{
    public override DetectionType Type => DetectionType.Iban;
    public override string Name => "IBAN Detector";
    public override string Description => "Detects Turkish IBAN with MOD-97 checksum validation";

    // TR + 2 check digits + 22 BBAN digits = 24 digits after TR, 26 total, with optional spaces
    // Matches both spaced (TR33 0006 1005 ...) and unspaced (TR330006...)
    // Use \b to ensure word boundary, IgnoreCase for TR/tr
    private static readonly Regex IbanPattern = new(
        @"\bTR\d{2}(?:\s*\d){22}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var matches = IbanPattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateRaw = match.Value;
            var stripped = Regex.Replace(candidateRaw, @"\s", "").ToUpperInvariant();

            // Validate format: TR + 24 digits = 26 chars
            if (stripped.Length != 26) continue;
            if (!stripped.StartsWith("TR", StringComparison.OrdinalIgnoreCase)) continue;
            var digitsPart = stripped.Substring(2);
            if (!digitsPart.All(char.IsDigit)) continue;

            if (!IsValidIban(stripped)) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);

            // IBAN is distinctive; no strong label required, but boost confidence if label present
            double confidence = 0.95;
            if (contextFeatures.HasStrongLabel)
                confidence = Math.Min(1.0, confidence + 0.05);
            if (contextFeatures.HasNegativeLabel)
                confidence = Math.Max(0.2, confidence - 0.3);

            var originalStart = normalizedText.MapToOriginalPosition(match.Index);
            var originalEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);

            var textSpan = new TextSpan
            {
                StartIndex = originalStart,
                Length = originalEnd - originalStart,
                Text = candidateRaw,
                BoundingBox = BoundingBox.Empty
            };

            var detection = CreateDetection(
                value: candidateRaw,
                confidence: confidence,
                pageNumber: page.PageNumber,
                textSpan: textSpan,
                context: contextWindow.FullText,
                properties: new Dictionary<string, object>
                {
                    ["iban_normalized"] = stripped,
                    ["checksum_valid"] = true,
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore
                });

            detections.Add(detection);
        }

        return detections;
    }

    private static bool IsValidIban(string iban)
    {
        // IBAN validation: move first 4 chars to end, convert letters A=10..Z=35, mod97 ==1
        if (string.IsNullOrWhiteSpace(iban)) return false;
        var stripped = iban.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (stripped.Length < 4) return false;

        var rearranged = stripped.Substring(4) + stripped.Substring(0, 4);
        var numericString = "";
        foreach (var c in rearranged)
        {
            if (char.IsDigit(c))
                numericString += c;
            else if (char.IsLetter(c))
                numericString += (c - 55).ToString(); // A=10 (65-55=10)
            else
                return false;
        }

        // Compute mod 97 iteratively to avoid overflow
        int remainder = 0;
        foreach (var ch in numericString)
        {
            remainder = (remainder * 10 + (ch - '0')) % 97;
        }

        return remainder == 1;
    }
}
