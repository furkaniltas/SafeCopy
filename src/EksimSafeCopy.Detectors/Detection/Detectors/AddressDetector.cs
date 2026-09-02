namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class AddressDetector : BaseDetector, IAddressDetector
{
    public override DetectionType Type => DetectionType.Address;
    public override string Name => "Turkish Address Detector";
    public override string Description => "Detects Turkish addresses with structural components";

    private static readonly Regex AddressPattern = new(
        @"\b(?:[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)*)\s+(?:mahalle|mah|mh|mah\.)\s*(?:[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)*)\s+(?:caddesi|cadde|cad|caddesi\s*|cad\.)\s*(?:no|numara|numarası)?\s*[:]?\s*\d+[A-Za-z]?(?:\s*(?:daire|dair|d|kat|k)\s*:?\s*\d+)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SimpleAddressPattern = new(
        @"\b(?:[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)\s+(?:mahalle|mah|mh|mah\.)\s*(?:[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)\s+(?:caddesi|cadde|cad|sokak|sokak\.|sok|sok\.)\s*(?:no|numara)?\s*[:]?\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ResidencyPattern = new(
        @"\b(?:[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)*)\s*(?:'|\s+)(?:da|de|ta|te)\s+yaşıyor\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Curated dataset pattern: handles Mah./Mh./Mahallesi + Sk./S k./Sok. + No:/No./No= + D:/D= + optional suffixes (Bahçe, İç Kapı, Blok, İlçe/İl, Kat)
    private static readonly Regex CuratedAddressPattern = new(
        @"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)*\s+(?:Mahalle|Mahallesi|Mah\.|Mh\.|Mh)\s*\.?\s*[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+)*\s+(?:Sokak|Sok\.|Sk\.|S\s*k\.)\s*(?:No|Numara|Numarası)?\s*[:=\.]?\s*\d+[A-Za-z]?(?:\s*(?:Daire|D)\s*[:=]?\s*\d+)?(?:\s*,\s*(?:\d+\.\s*)?(?:Bahçe Kapısı|Arka Giriş|İç Kapı(?:\s+No)?\s*[A-Z0-9]+|Dış Kapı|Blok|Apt\.?|Kat|E\s*Blok|B\s*Blok|A\s*Blok|C\s*Blok|D\s*Blok|\d+\.\s*Kat))*(?:\s*,\s*[A-ZÇĞİÖŞÜa-zçğıöşü]+\/[A-ZÇĞİÖŞÜa-zçğıöşü]+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> NegativeAddressKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "şirket adresi", "firma adresi", "kurum adresi", "resmi adres",
        "kayıtlı adres", "merkez adresi", "şube adresi", "ofis adresi",
        "fabrika adresi", "depo adresi", "santral adresi"
    };

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var patterns = new[] { CuratedAddressPattern, AddressPattern, SimpleAddressPattern, ResidencyPattern };

        foreach (var pattern in patterns)
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var candidate = match.Value.Trim();
                if (!IsValidAddressCandidate(candidate)) continue;

                var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
                var contextFeatures = AnalyzeContext(contextWindow);
                
                if (contextFeatures.HasNegativeLabel)
                    continue;

                double confidence = CalculateConfidence(candidate, contextFeatures);
                
                if (confidence < ConfidenceThreshold) continue;
                
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
                        ["component_count"] = CountAddressComponents(candidate)
                    });

                detections.Add(detection);
            }
        }

        return detections;
    }

    private bool IsValidAddressCandidate(string candidate)
    {
        var lower = candidate.ToLowerInvariant();
        // Safe normalization for checking variants like Mh.Akın -> Mh. Akın, S k. -> Sk., D= -> D:
        var norm = lower.Replace("mha", "mh a").Replace("mh.", "mh ").Replace("s k.", "sk.").Replace("s k", "sk ").Replace("d=", "d:").Replace("d :", "d:");
        
        if (NegativeAddressKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
            return false;

        var hasMahalle = norm.Contains("mahalle") || norm.Contains("mah ") || norm.Contains("mh ") || norm.Contains("mah.");
        var hasStreet = norm.Contains("caddesi") || norm.Contains("cadde") || norm.Contains("cad ") || norm.Contains("cad.") ||
                       norm.Contains("sokak") || norm.Contains("sok ") || norm.Contains("sok.") || norm.Contains("sk ") || norm.Contains("sk.");
        var hasNumber = Regex.IsMatch(candidate, @"\bno\s*[:=\.]?\s*\d+|\bnumara\s*[:=\.]?\s*\d+|\b:\s*\d+|\s\d{1,4}[A-Za-z]?\b", RegexOptions.IgnoreCase);

        return hasMahalle && hasStreet && hasNumber;
    }

    private int CountAddressComponents(string address)
    {
        int count = 0;
        var lower = address.ToLowerInvariant();
        var norm = lower.Replace("mha", "mh a").Replace("mh.", "mh ").Replace("s k.", "sk.").Replace("s k", "sk ").Replace("d=", "d:").Replace("d :", "d:");
        
        if (norm.Contains("mahalle") || norm.Contains("mah ") || norm.Contains("mh ") || norm.Contains("mah.")) count++;
        if (norm.Contains("caddesi") || norm.Contains("cadde") || norm.Contains("cad ") || norm.Contains("cad.")) count++;
        if (norm.Contains("sokak") || norm.Contains("sok ") || norm.Contains("sk ")) count++;
        if (Regex.IsMatch(lower, @"\bno\s*[:=\.]?\s*\d+|\bnumara\s*[:=\.]?\s*\d+|\b:\s*\d+", RegexOptions.IgnoreCase)) count++;
        if (norm.Contains("daire") || norm.Contains("dair") || norm.Contains("d ") || norm.Contains("kat") || norm.Contains("k ")) count++;
        if (lower.Contains("ilçe") || lower.Contains("ilce") || lower.Contains("il ") || Regex.IsMatch(lower, @"\b\d{5}\b")) count++;
        if (norm.Contains("bahçe kapısı") || norm.Contains("bahce kapisi") || norm.Contains("iç kapı") || norm.Contains("ic kapi") || norm.Contains("arka giriş") || norm.Contains("arka giris") || norm.Contains("blok")) count++;
        
        return count;
    }

    private double CalculateConfidence(string address, ContextFeatures context)
    {
        double confidence = 0.7;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.2);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.4);

        var components = CountAddressComponents(address);
        if (components >= 4)
            confidence = Math.Min(1.0, confidence + 0.15);
        else if (components >= 3)
            confidence = Math.Min(1.0, confidence + 0.1);

        return confidence;
    }
}