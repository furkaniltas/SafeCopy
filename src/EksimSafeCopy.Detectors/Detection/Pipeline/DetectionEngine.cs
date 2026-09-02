namespace EksimSafeCopy.Detectors.Detection.Pipeline;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.Detectors.Detection.PossiblePersonalData;

public sealed class DetectionEngine : IDetectionEngine
{
    private readonly IReadOnlyList<IDetector> _detectors;
    private readonly IPossiblePersonalDataAnalyzer? _possibleAnalyzer;

    private static readonly HashSet<string> TurkishTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "temsilci", "müdür", "müdürü", "mudur", "muduru", "şef", "sefi", "sef",
        "uzman", "danışman", "danisman", "avukat", "avukatı", "avukati",
        "doktor", "doktoru", "doktorun", "hoca", "hocam", "öğretmen", "ogretmen",
        "mühendis", "muhendis", "mimar", "mimarını", "mimarini",
        "bey", "hanım", "hanim", "bay", "bayan", "sn", "sayın", "sayin"
    };

    private static readonly HashSet<string> TurkishCities = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly HashSet<string> NegativeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "şirket", "firma", "kurum", "kuruluş", "müdürlük", "birim", "bölge", "bölüm",
        "departman", "üniversite", "okul", "hastane", "belediye", "valilik",
        "kaymakamlık", "mahkeme", "polis", "elektrik", "su", "doğalgaz",
        "internet", "telekom", "dağıtım", "tedarik", "hizmet", "destek",
        "anonym", "anonim", "misafir", "müşteri", "müşteriler", "üye", "üyeler"
    };

    public DetectionEngine(IEnumerable<IDetector> detectors, IPossiblePersonalDataAnalyzer? possibleAnalyzer = null)
    {
        _detectors = detectors
            .Where(d => d.IsEnabled)
            .OrderBy(d => d.Type)
            .ToList()
            .AsReadOnly();
        _possibleAnalyzer = possibleAnalyzer;
    }

    public Result<IReadOnlyList<Detection>> Detect(Document document, CancellationToken cancellationToken = default)
    {
        try
        {
            var allDetections = new List<Detection>();

            foreach (var detector in _detectors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = detector.Detect(document, cancellationToken);
                if (result.IsFailure)
                {
                    return Result<IReadOnlyList<Detection>>.Failure(result.Error);
                }

                allDetections.AddRange(result.Value);
            }

            var processedDetections = PostProcessDetections(allDetections, document);
            
            // PossiblePersonalData analyzer (after kesin detections, no duplicate)
            if (_possibleAnalyzer != null)
            {
                var possibleDetections = new List<Detection>();
                foreach (var page in document.Pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pageText = page.Text;
                    if (string.IsNullOrWhiteSpace(pageText)) continue;
                    var normalized = EksimSafeCopy.Detectors.Detection.Normalization.TextNormalizer.NormalizeWithPositionTracking(pageText, new EksimSafeCopy.Detectors.Detection.Normalization.NormalizationOptions
                    {
                        NormalizeNewlines = true,
                        CollapseWhitespace = true,
                        CollapseNewlines = true,
                        FixTurkishWhitespace = true,
                        TrimEdges = true
                    });
                    var pageExisting = processedDetections.Where(d => d.PageNumber == page.PageNumber).ToList();
                    var possible = _possibleAnalyzer.Analyze(document, normalized, pageExisting, cancellationToken);
                    foreach (var p in possible)
                    {
                        if (p.TextSpan == null) continue;
                        if (pageExisting.Any(e => e.TextSpan != null && SpansOverlap(e.TextSpan!, p.TextSpan!))) continue;
                        if (possibleDetections.Any(e => e.TextSpan != null && SpansOverlap(e.TextSpan!, p.TextSpan!))) continue;
                        possibleDetections.Add(p);
                    }
                }
                if (possibleDetections.Count > 0)
                {
                    var combined = processedDetections.Concat(possibleDetections).ToList();
                    processedDetections = PostProcessDetections(combined, document);
                }
            }

            return Result<IReadOnlyList<Detection>>.Success(processedDetections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<IReadOnlyList<Detection>>.Failure(Error.Cancelled("Detection was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<Detection>>.Failure(Error.Internal($"Detection failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<IReadOnlyList<Detection>>> DetectAsync(Document document, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Detect(document, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<IReadOnlyList<IDetector>> GetDetectors()
    {
        return Result<IReadOnlyList<IDetector>>.Success(_detectors);
    }

    public Result<IDetector?> GetDetector(DetectionType type)
    {
        var detector = _detectors.FirstOrDefault(d => d.Type == type);
        return Result<IDetector?>.Success(detector);
    }

    private IReadOnlyList<Detection> PostProcessDetections(IReadOnlyList<Detection> detections, Document document)
    {
        var result = detections
            .Where(d => d.Confidence > 0)
            .ToList();

        result = ResolveOverlaps(result);
        result = ResolveDuplicates(result);
        result = ReconcileCrossBlockDetections(result, document);
        result = PostProcessNameDetections(result);
        
        return result
            .OrderBy(d => d.PageNumber)
            .ThenBy(d => d.TextSpan?.StartIndex ?? 0)
            .ThenBy(d => d.Type)
            .ToList()
            .AsReadOnly();
    }

    private List<Detection> PostProcessNameDetections(List<Detection> detections)
    {
        var result = new List<Detection>();
        
        foreach (var detection in detections.OrderBy(d => d.TextSpan?.StartIndex ?? 0))
        {
            if (detection.Type != DetectionType.FullName)
            {
                result.Add(detection);
                continue;
            }
            
            var splitDetections = SplitNameWithTitle(detection);
            result.AddRange(splitDetections);
        }
        
        return result;
    }

    private List<Detection> SplitNameWithTitle(Detection detection)
    {
        var words = detection.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return new List<Detection> { detection };
        
        var lastWord = words[^1].ToLowerInvariant();
        if (!TurkishTitles.Contains(lastWord))
            return new List<Detection> { detection };
        
        // Split into name without title
        var nameWithoutTitle = string.Join(" ", words.Take(words.Length - 1));
        if (!IsValidNameCandidate(nameWithoutTitle))
            return new List<Detection> { detection };
        
        var nameDetection = new Detection
        {
            Type = detection.Type,
            Value = nameWithoutTitle,
            Confidence = detection.Confidence,
            ConfidenceLevel = detection.ConfidenceLevel,
            PageNumber = detection.PageNumber,
            TextSpan = detection.TextSpan,
            Location = detection.Location,
            DetectionSource = detection.DetectionSource,
            Context = detection.Context,
            State = detection.State,
            Properties = new Dictionary<string, object>(detection.Properties)
            {
                ["split_from_title"] = true,
                ["original_with_title"] = detection.Value
            }
        };
        
        return new List<Detection> { nameDetection };
    }

    private bool IsValidNameCandidate(string candidate)
    {
        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 4) return false;

        foreach (var word in words)
        {
            if (word.Length < 2) return false;
            if (TurkishCities.Contains(word.ToLowerInvariant())) return false;
            if (NegativeKeywords.Contains(word.ToLowerInvariant())) return false;
        }

        var lowerCandidate = candidate.ToLowerInvariant();
        
        // Allow Turkish titles ONLY at the end of the name (e.g., "Ahmet Yılmaz Temsilci")
        // Reject if a title appears in the middle of the name
        var splitWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < splitWords.Length - 1; i++)
        {
            if (TurkishTitles.Contains(splitWords[i].ToLowerInvariant()))
                return false;
        }
        
        // Reject if negative keywords appear anywhere
        if (NegativeKeywords.Any(k => lowerCandidate.Contains(k.ToLowerInvariant()))) return false;

        return true;
    }

    private static bool SpansOverlap(TextSpan a, TextSpan b)
    {
        return a.StartIndex < b.EndIndex && b.StartIndex < a.EndIndex;
    }

    private List<Detection> ResolveOverlaps(List<Detection> detections)
    {
        var result = new List<Detection>();
        var sorted = detections
            .OrderBy(d => d.PageNumber)
            .ThenBy(d => d.TextSpan?.StartIndex ?? 0)
            .ThenByDescending(d => d.Confidence)
            .ToList();

        foreach (var detection in sorted)
        {
            var overlaps = result
                .Where(r => r.PageNumber == detection.PageNumber)
                .Where(r => r.TextSpan != null && detection.TextSpan != null)
                .Where(r => SpansOverlap(r.TextSpan!, detection.TextSpan!))
                .ToList();

            if (overlaps.Count == 0)
            {
                result.Add(detection);
                continue;
            }

            var highestConfidenceOverlap = overlaps.MaxBy(o => o.Confidence);
            if (highestConfidenceOverlap != null && detection.Confidence > highestConfidenceOverlap.Confidence)
            {
                result.Remove(highestConfidenceOverlap);
                result.Add(detection);
            }
        }

        return result;
    }

    private List<Detection> ResolveDuplicates(List<Detection> detections)
    {
        var result = new List<Detection>();
        var groups = detections
            .GroupBy(d => new { d.Type, d.Value, d.PageNumber, d.TextSpan?.StartIndex })
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var best = group.MaxBy(d => d.Confidence);
            if (best != null)
            {
                result.Add(best);
            }
        }

        var singles = detections
            .GroupBy(d => new { d.Type, d.Value, d.PageNumber, d.TextSpan?.StartIndex })
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .ToList();

        result.AddRange(singles);
        return result;
    }

    private List<Detection> ReconcileCrossBlockDetections(List<Detection> detections, Document document)
    {
        var result = new List<Detection>(detections);

        var tcKimlikDetections = result.Where(d => d.Type == DetectionType.TcKimlikNo).ToList();
        var nameDetections = result.Where(d => d.Type == DetectionType.FullName).ToList();
        var phoneDetections = result.Where(d => d.Type == DetectionType.Phone).ToList();

        foreach (var tc in tcKimlikDetections)
        {
            var nearbyName = nameDetections
                .Where(n => n.PageNumber == tc.PageNumber)
                .Where(n => n.TextSpan != null && tc.TextSpan != null)
                .Where(n => Math.Abs(n.TextSpan!.StartIndex - tc.TextSpan!.StartIndex) < 200)
                .MaxBy(n => n.Confidence);

            if (nearbyName != null)
            {
                tc.Properties["associated_name"] = nearbyName.Value;
                tc.Properties["name_confidence"] = nearbyName.Confidence;
            }
        }

        foreach (var phone in phoneDetections)
        {
            var nearbyName = nameDetections
                .Where(n => n.PageNumber == phone.PageNumber)
                .Where(n => n.TextSpan != null && phone.TextSpan != null)
                .Where(n => Math.Abs(n.TextSpan!.StartIndex - phone.TextSpan!.StartIndex) < 300)
                .MaxBy(n => n.Confidence);

            if (nearbyName != null)
            {
                phone.Properties["associated_name"] = nearbyName.Value;
                phone.Properties["name_confidence"] = nearbyName.Confidence;
            }
        }

        return result;
    }
}