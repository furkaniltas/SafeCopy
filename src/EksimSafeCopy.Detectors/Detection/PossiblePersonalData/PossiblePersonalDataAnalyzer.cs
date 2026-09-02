using System.Text.RegularExpressions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using DetectionModel = EksimSafeCopy.Core.Models.Detection;

namespace EksimSafeCopy.Detectors.Detection.PossiblePersonalData;

public sealed class PossiblePersonalDataAnalyzer : IPossiblePersonalDataAnalyzer
{
    private static readonly Regex NameLikePattern = new(
        @"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+){1}\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> PossibleLabels = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "başvuru sahibi", "basvuru sahibi",
        "ilgili kişi", "ilgili kisi",
        "yetkili",
        "müşteri adı", "musteri adi",
        "adı soyadı", "adi soyadi", "ad soyad",
        "yakını", "yakini",
        "baba adı", "baba adi",
        "anne adı", "anne adi",
        "başvuran", "basvuran",
        "vekil", "temsilci", "ilgili"
    };

    private static readonly HashSet<string> NegativeKeywords = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "diyar", "diyarbakır", "diyarbakir", "icra", "dairesi", "dairesine", "daire", "esas", "talep", "evrakı", "evraki", "evrak",
        "takibin", "kesinleştirilmesini", "kesinlestirilmesini", "dava", "dosya", "talebi", "talebin", "ne", "esas",
        "ankara", "bölge", "bolge", "müdürlüğü", "mudurlugu", "müşteri", "musteri", "başvuru", "basvuru", "formu", "form",
        "doğum", "dogum", "tarihi", "kimlik", "no", "ad", "soyad",
        "şirket", "sirket", "kurum", "mahkeme", "belediye", "valilik", "müdürlük", "mudurluk", "kurul", "belge", "teknik", "departman"
    };

    private readonly ContextAnalyzer _contextAnalyzer = new();

    public IReadOnlyList<DetectionModel> Analyze(Document document, NormalizedText normalizedText, IReadOnlyList<DetectionModel> existingDetections, CancellationToken cancellationToken = default)
    {
        var results = new List<DetectionModel>();
        var text = normalizedText.Text;
        var matches = NameLikePattern.Matches(text);
        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = match.Value.Trim();
            var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2) continue;
            if (words.Any(w => w.Length < 2)) continue;
            var candidateStart = normalizedText.MapToOriginalPosition(match.Index);
            var candidateEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);
            var candidateSpan = new TextSpan { StartIndex = candidateStart, Length = candidateEnd - candidateStart, Text = candidate };
            if (existingDetections.Any(d => d.Type == DetectionType.PossiblePersonalData && d.TextSpan != null && SpansOverlap(d.TextSpan!, candidateSpan)))
                continue;
            if (existingDetections.Any(d => d.Type == DetectionType.FullName && d.TextSpan != null && SpansOverlap(d.TextSpan!, candidateSpan)))
            {
                var hasStrongForOverlap = HasStrongContext(ContextWindow.Create(normalizedText.Text, match.Index, match.Length, 120));
                if (!hasStrongForOverlap) continue;
            }
            var lowerCandidate = candidate.ToLowerInvariant();
            if (NegativeKeywords.Any(k => lowerCandidate.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (words.Any(w => NegativeKeywords.Contains(w))) continue;
            if (IsFieldLabel(candidate)) continue;
            var contextWindow = ContextWindow.Create(normalizedText.Text, match.Index, match.Length, 120);
            var hasStrongContext = HasStrongContext(contextWindow);
            var hasProximity = HasPiiProximity(normalizedText, match.Index, match.Length, existingDetections);
            if (!(hasStrongContext || hasProximity)) continue;
            var confidence = 0.55;
            if (hasStrongContext) confidence += 0.05;
            if (hasProximity) confidence += 0.05;
            confidence = Math.Clamp(confidence, 0, 1);
            var originalStart = normalizedText.MapToOriginalPosition(match.Index);
            var originalEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);
            var textSpan = new TextSpan
            {
                StartIndex = originalStart,
                Length = originalEnd - originalStart,
                Text = candidate,
                BoundingBox = BoundingBox.Empty
            };
            var reason = hasStrongContext ? $"Yakin etiket: \"{GetMatchedLabel(contextWindow)}\" + isim benzeri ifade" : "Yakin kesin PII + isim benzeri ifade";
            var detection = new DetectionModel
            {
                Type = DetectionType.PossiblePersonalData,
                Value = candidate,
                Confidence = confidence,
                ConfidenceLevel = ConfidenceLevel.Medium,
                PageNumber = 1,
                TextSpan = textSpan,
                Location = BoundingBox.Empty,
                Context = contextWindow.FullText,
                State = DetectionState.Detected,
                Properties = new Dictionary<string, object>
                {
                    ["possible_reason"] = reason,
                    ["has_strong_context"] = hasStrongContext,
                    ["has_proximity"] = hasProximity
                }
            };
            results.Add(detection);
        }
        return results;
    }

    private bool IsFieldLabel(string candidate)
    {
        var lower = candidate.ToLowerInvariant();
        return lower == "doğum tarihi" || lower == "dogum tarihi" || lower == "kimlik no" || lower == "ad soyad" || lower == "ad soyadı" || lower == "adi soyadi";
    }

    private bool HasStrongContext(ContextWindow window)
    {
        var text = window.FullText.ToLowerInvariant();
        return PossibleLabels.Any(l => text.Contains(l, StringComparison.OrdinalIgnoreCase));
    }

    private string GetMatchedLabel(ContextWindow window)
    {
        var text = window.FullText;
        foreach (var label in PossibleLabels)
        {
            if (text.Contains(label, StringComparison.OrdinalIgnoreCase))
                return label;
        }
        return PossibleLabels.FirstOrDefault(l => window.FullText.Contains(l, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private bool HasPiiProximity(NormalizedText normalizedText, int matchIndex, int matchLength, IReadOnlyList<DetectionModel> existingDetections)
    {
        var candidateStart = normalizedText.MapToOriginalPosition(matchIndex);
        var candidateEnd = normalizedText.MapToOriginalPosition(matchIndex + matchLength);
        foreach (var det in existingDetections)
        {
            if (det.TextSpan == null) continue;
            var detStart = det.TextSpan.StartIndex;
            var detEnd = detStart + det.TextSpan.Length;
            if (Math.Abs(candidateStart - detStart) <= 200 || Math.Abs(candidateEnd - detEnd) <= 200)
                return true;
            if (Math.Abs(matchIndex - normalizedText.Text.IndexOf(det.Value, StringComparison.OrdinalIgnoreCase)) <= 200)
                return true;
        }
        return false;
    }

    private static bool SpansOverlap(TextSpan a, TextSpan b)
    {
        return a.StartIndex < b.EndIndex && b.StartIndex < a.EndIndex;
    }
}
