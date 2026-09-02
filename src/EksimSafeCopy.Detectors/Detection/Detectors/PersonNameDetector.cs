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
        "bey", "hanım", "hanim", "bay", "bayan", "sn", "sayın", "sayin",
        "av", "av.", "dyt", "dyt.", "uzm", "uzm.", "dr", "dr.", "prof", "prof.", "doç", "doç."
    };

    private static readonly HashSet<string> ContextualPrefixes = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "sahip", "alıcı", "alici", "danışan", "danisan", "hasta", "kişi", "kisi", "taraf"
    };

    private static readonly HashSet<string> AddressComponents = new(StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
    {
        "iç kapı", "ic kapi", "iç kapı no", "bahçe kapısı", "bahce kapisi", "arka giriş", "arka giris", "blok", "kat"
    };

    private static readonly Regex MiddleInitialPattern = new(
        @"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]+\s+[A-ZÇĞİÖŞÜ]\.\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]+\b",
        RegexOptions.Compiled);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var allPatterns = new[] { NamePattern, UpperCaseNamePattern, MiddleInitialPattern };

        foreach (var pattern in allPatterns)
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var candidate = match.Value;
                var candidateStartInMatch = 0;
                // If candidate starts with contextual prefix, must strip and only consider stripped version
                if (StartsWithContextualPrefix(candidate))
                {
                    var stripped = TryStripContextualPrefix(candidate, out var prefixLength);
                    if (stripped == null || !IsValidNameCandidate(stripped) || IsAddressComponent(stripped)) continue;
                    candidate = stripped;
                    candidateStartInMatch = prefixLength;
                }
                else
                {
                    if (!IsValidNameCandidate(candidate)) continue;
                    if (IsAddressComponent(candidate)) continue;
                }

                var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
                var contextFeatures = AnalyzeContext(contextWindow);
                
                if (contextFeatures.HasNegativeLabel)
                    continue;

                double confidence = CalculateConfidence(candidate, contextFeatures);
                
                if (confidence < ConfidenceThreshold) continue;
                
                var adjustedIndex = match.Index + candidateStartInMatch;
                var adjustedLength = candidate.Length;
                var originalStart = normalizedText.MapToOriginalPosition(adjustedIndex);
                var originalEnd = normalizedText.MapToOriginalPosition(adjustedIndex + adjustedLength);
                
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

        // Address component false positive: İç Kapı, Bahçe Kapısı etc. are not person names
        if (IsAddressComponent(candidate)) return false;

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 4) return false;

        foreach (var word in words)
        {
            // Allow single initial like "B." (e.g., Levent B. Yıldırım)
            if (System.Text.RegularExpressions.Regex.IsMatch(word, @"^[A-ZÇĞİÖŞÜ]\.$")) continue;
            if (word.Length < 2) return false;
            if (TurkishCities.Contains(word)) return false;
            if (NegativeKeywords.Contains(word)) return false;
        }

        // Allow Turkish titles ONLY at the end of the name (e.g., "Ahmet Yılmaz Temsilci")
        // Reject if a title appears in the middle of the name
        for (int i = 0; i < words.Length - 1; i++)
        {
            // Allow single initial with dot (e.g., B.) even if it's not a title
            if (System.Text.RegularExpressions.Regex.IsMatch(words[i], @"^[A-ZÇĞİÖŞÜ]\.$")) continue;
            if (TurkishTitles.Contains(words[i]))
                return false;
        }
        
        // Reject if negative keywords appear as whole token/phrase, not arbitrary substring inside another word
        // "ad" must match standalone "ad", not "Maden" (contains "ad" as substring)
        if (NegativeKeywords.Any(k => ContainsWholeWord(candidate, k))) return false;
        // Also reject if field label appears as whole phrase (e.g., "Doğum Tarihi Ahmet")
        if (FieldLabels.Any(f => ContainsWholeWord(lower, f) || ContainsWholeWord(candidate, f))) return false;

        return true;
    }

    private bool IsAddressComponent(string candidate)
    {
        var lower = candidate.ToLower(new System.Globalization.CultureInfo("tr-TR"));
        // Known address components that are falsely detected as names
        if (lower == "iç kapı" || lower.Contains("iç kapı")) return true;
        if (lower == "bahçe kapısı" || lower.Contains("bahçe kapısı")) return true;
        if (lower.Contains("arka giriş")) return true;
        if (lower == "dış kapı" || lower.Contains("dış kapı")) return true;
        // Also check for "Kapı" alone with İç/Bahçe etc. already covered
        return AddressComponents.Any(k => lower == k || lower.Contains(k));
    }

    private static bool ContainsWholeWord(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return false;
        // Use word-boundary check with Turkish-aware case-insensitive comparison
        var tr = new System.Globalization.CultureInfo("tr-TR");
        var lowerText = text.ToLower(tr);
        var lowerKeyword = keyword.ToLower(tr);
        // For single-word keyword, check as whole token
        if (!lowerKeyword.Contains(' '))
        {
            var words = lowerText.Split(new[] { ' ', '\t', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
            {
                var clean = w.TrimEnd('.', ',', ';', ':', '!', '?', '"', '\'', '’', '‘');
                if (clean == lowerKeyword) return true;
            }
            return false;
        }
        // For phrase, check with word boundaries via regex-like contains with spaces
        // Ensure keyword appears with word boundaries (space/punct or start/end)
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerKeyword)}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(lowerText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool StartsWithContextualPrefix(string candidate)
    {
        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        var first = words[0].TrimEnd('.');
        return ContextualPrefixes.Contains(first);
    }

    private string? TryStripContextualPrefix(string candidate, out int prefixLength)
    {
        prefixLength = 0;
        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return null;
        var first = words[0].TrimEnd('.');
        if (!ContextualPrefixes.Contains(first)) return null;
        // Check if remaining part after prefix is a valid name candidate (at least 2 words, or 2 with initial)
        var remainingWords = words.Skip(1).ToArray();
        if (remainingWords.Length < 2) return null;
        // Do not strip if remaining is just a title (e.g., "Danışan Uzm" -> remaining "Uzm" is title, not name)
        var remaining = string.Join(" ", remainingWords);
        var remLower = remaining.ToLower(new System.Globalization.CultureInfo("tr-TR"));
        // If remaining is exactly a title or address component, do not strip (would create FP)
        if (TurkishTitles.Contains(remainingWords[0]) && remainingWords.Length == 1) return null;
        if (IsAddressComponent(remaining)) return null;
        // If remaining is single title like "Uzm", "Av", "Dyt" - reject
        if (remainingWords.Length == 1 && TurkishTitles.Contains(remainingWords[0])) return null;
        // Check if remaining contains only titles (e.g., "Av. Uzm")
        if (remainingWords.All(w => TurkishTitles.Contains(w.TrimEnd('.')) || System.Text.RegularExpressions.Regex.IsMatch(w, @"^[A-ZÇĞİÖŞÜ]\.$"))) return null;

        // Valid prefix stripping: return remaining and prefix length (including space)
        prefixLength = words[0].Length + 1; // +1 for space
        return remaining;
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