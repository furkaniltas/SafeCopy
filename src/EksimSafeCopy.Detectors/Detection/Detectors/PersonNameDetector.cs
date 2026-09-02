namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class PersonNameDetector : BaseDetector, IPersonNameDetector
{
    public override DetectionType Type => DetectionType.FullName;
    public override string Name => "Person Name Detector";
    public override string Description => "Detects Turkish person names with contextual validation";

    private static readonly Regex NamePattern = new(
        @"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]+(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+){1,3}\b",
        RegexOptions.Compiled);

    private static readonly Regex UpperCaseNamePattern = new(
        @"\b[A-ZÇĞİÖŞÜ]{2,}(?:\s+[A-ZÇĞİÖŞÜ]{2,}){1,3}\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> TurkishCities = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "adana", "adıyaman", "afyonkarahisar", "ağrı", "aksaray", "amasya", "ankara", "antalya", "ardahan", "artvin",
        "aydın", "balıkesir", "bartın", "batman", "bayburt", "bilecik", "bingöl", "bitlis", "bolu", "burdur",
        "bursa", "çanakkale", "çankırı", "çorum", "denizli", "diyarbakır", "düzce", "edirne", "elazığ", "erzincan",
        "erzurum", "eskişehir", "gaziantep", "giresun", "gümüşhane", "hakkari", "hatay", "ığdır", "ısparta", "istanbul",
        "izmir", "kahramanmaraş", "karabük", "karaman", "kars", "kastamonu", "kayseri", "kırıkkale", "kırklareli",
        "kırşehir", "kilis", "kocaeli", "konya", "kütahya", "malatya", "manisa", "mardin", "mersin", "muğla",
        "muş", "nevşehir", "niğde", "ordu", "osmaniye", "rize", "sakarya", "samsun", "siirt", "sinop",
        "sivas", "şanlıurfa", "şırnak", "tekirdağ", "tokat", "trabzon", "tunceli", "uşak", "van", "yalova",
        "yozgat", "zonguldak"
    };

    private static readonly HashSet<string> NegativeKeywords = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "şirket", "firma", "kurum", "kuruluş", "müdürlük", "birim", "bölge", "bölüm",
        "departman", "üniversite", "okul", "hastane", "belediye", "valilik",
        "kaymakamlık", "mahkeme", "polis", "elektrik", "su", "doğalgaz",
        "internet", "telekom", "dağıtım", "tedarik", "hizmet", "destek",
        "anonym", "anonim", "misafir", "müşteri", "müşteriler", "üye", "üyeler",
        "tesisat", "numarası", "numarasi", "sayaç", "sayac", "abone", "tesisat",
        "ad", "soyad", "telefon", "adres", "tc", "numara",
        "icra", "dairesi", "dairesine", "daire", "esas", "talep", "evrakı", "evraki", "evrak",
        "takibin", "kesinleştirilmesini", "kesinlestirilmesini", "dava", "dosya", "talebi", "talebin"
    };

    private static readonly HashSet<string> FieldLabels = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "doğum tarihi", "dogum tarihi",
        "kimlik no", "kimlik numarası",
        "ad soyad", "adı soyadı", "adi soyadi", "ad soyadı",
        "başvuru sahibi", "basvuru sahibi",
        "müşteri adı", "musteri adi",
        "ilgili kişi", "ilgili kisi",
        "yetkili", "yakını", "yakini",
        "baba adı", "baba adi",
        "anne adı", "anne adi"
    };

    private static readonly HashSet<string> TurkishTitles = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "temsilci", "müdür", "müdürü", "mudur", "muduru", "şef", "sefi", "sef",
        "uzman", "danışman", "danisman", "avukat", "avukatı", "avukati",
        "doktor", "doktoru", "doktorun", "hoca", "hocam", "öğretmen", "ogretmen",
        "mühendis", "muhendis", "mimar", "mimarını", "mimarini",
        "bey", "hanım", "hanim", "bay", "bayan", "sn", "sayın", "sayin"
    };

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var allPatterns = new[] { NamePattern, UpperCaseNamePattern };

        foreach (var pattern in allPatterns)
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var candidate = match.Value;
                if (!IsValidNameCandidate(candidate)) continue;

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
                        ["word_count"] = candidate.Split(' ').Length,
                        ["is_uppercase"] = candidate.All(char.IsUpper),
                        ["has_label_context"] = contextFeatures.HasStrongLabel,
                        ["label_score"] = contextFeatures.LabelScore
                    });

                detections.Add(detection);
            }
        }

        // Post-process: if we detected overlapping names, keep the shorter one (more likely to be just the name)
        return PostProcessNameDetections(detections);
    }

    private IReadOnlyList<Detection> PostProcessNameDetections(List<Detection> detections)
    {
        var result = new List<Detection>();
        
        foreach (var detection in detections.OrderBy(d => d.TextSpan?.StartIndex ?? 0))
        {
            var overlaps = result.Where(r => r.TextSpan != null && detection.TextSpan != null)
                .Where(r => SpansOverlap(r.TextSpan!, detection.TextSpan!))
                .ToList();
            
            if (overlaps.Count == 0)
            {
                result.Add(detection);
                continue;
            }
            
            // Keep the shorter name (more likely to be just the name without title)
            var longest = overlaps.MaxBy(o => o.Value.Length);
            if (longest != null && detection.Value.Length < longest.Value.Length)
            {
                result.Remove(longest);
                result.Add(detection);
            }
            else if (longest == null)
            {
                result.Add(detection);
            }
        }
        
        return result;
    }

    private bool IsValidNameCandidate(string candidate)
    {
        var lower = candidate.ToLower(new System.Globalization.CultureInfo("tr-TR"));
        if (FieldLabels.Contains(lower)) return false;
        if (FieldLabels.Contains(candidate)) return false;

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 4) return false;

        foreach (var word in words)
        {
            if (word.Length < 2) return false;
            if (TurkishCities.Contains(word)) return false;
            if (NegativeKeywords.Contains(word)) return false;
        }

        // Allow Turkish titles ONLY at the end of the name (e.g., "Ahmet Yılmaz Temsilci")
        // Reject if a title appears in the middle of the name
        for (int i = 0; i < words.Length - 1; i++)
        {
            if (TurkishTitles.Contains(words[i]))
                return false;
        }
        
        // Reject if negative keywords appear anywhere (covers kurum/hukuk phrases)
        if (NegativeKeywords.Any(k => candidate.Contains(k, StringComparison.OrdinalIgnoreCase))) return false;
        // Also reject if field label appears as substring (e.g., "Doğum Tarihi Ahmet" - but candidate is 2-4 words, so check exact)
        if (FieldLabels.Any(f => lower.Contains(f))) return false;

        return true;
    }

    private double CalculateConfidence(string name, ContextFeatures context)
    {
        double confidence = 0.65;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.25);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.05, confidence - 0.5);

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2)
            confidence = Math.Min(1.0, confidence + 0.1);
        else if (words.Length == 3)
            confidence = Math.Min(1.0, confidence + 0.05);

        if (name.All(char.IsUpper))
        {
            // Strong penalty for 3-4 word ALL-CAPS (kurum/belge başlıkları), but keep real 2-word names like "AHMET YILMAZ"
            if (words.Length >= 3)
                confidence = Math.Max(0.05, confidence - 0.35);
            else
                confidence = Math.Max(0.5, confidence - 0.1);
        }

        return confidence;
    }
}