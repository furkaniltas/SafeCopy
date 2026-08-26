namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class EmailDetector : BaseDetector, IEmailDetector
{
    public override DetectionType Type => DetectionType.Email;
    public override string Name => "Email Address Detector";
    public override string Description => "Detects email addresses in various formats";

    private static readonly Regex EmailPattern = new(
        @"(?<!\.)\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var matches = EmailPattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var candidate = match.Value;
            if (!IsValidEmail(candidate)) continue;

            var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
            var contextFeatures = AnalyzeContext(contextWindow);
            
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
                    ["has_label_context"] = contextFeatures.HasStrongLabel,
                    ["label_score"] = contextFeatures.LabelScore,
                    ["domain"] = GetDomain(candidate)
                });

            detections.Add(detection);
        }

        return detections;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Length > 254) return false;
        
        if (email.StartsWith(".")) return false;
        
        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1) return false;
        
        var localPart = email.Substring(0, atIndex);
        var domain = email.Substring(atIndex + 1);
        
        if (localPart.Length > 64) return false;
        if (domain.Length > 253) return false;
        
        if (localPart.StartsWith(".") || localPart.EndsWith(".")) return false;
        if (localPart.Contains("..")) return false;
        if (domain.StartsWith(".") || domain.EndsWith(".")) return false;
        if (domain.Contains("..")) return false;
        
        var domainParts = domain.Split('.');
        if (domainParts.Length < 2) return false;
        if (domainParts.Any(p => string.IsNullOrEmpty(p))) return false;
        
        var tld = domainParts[^1];
        if (tld.Length < 2) return false;
        if (!tld.All(c => char.IsLetter(c))) return false;
        
        return true;
    }

    private static string GetDomain(string email)
    {
        var atIndex = email.LastIndexOf('@');
        return atIndex >= 0 ? email.Substring(atIndex + 1) : string.Empty;
    }

    private double CalculateConfidence(string email, ContextFeatures context)
    {
        double confidence = 0.95;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.05);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.2, confidence - 0.3);

        return confidence;
    }
}