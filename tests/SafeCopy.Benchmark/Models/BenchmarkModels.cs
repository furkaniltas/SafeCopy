using SafeCopy.Core.Models;

namespace SafeCopy.Benchmark.Models;

public enum MappingStatus
{
    DIRECT,
    MAPPED,
    UNSUPPORTED,
    AMBIGUOUS
}

public sealed class LabelMapping
{
    public string DatasetLabel { get; init; } = string.Empty;
    public DetectionType? SafeCopyType { get; init; }
    public MappingStatus Status { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public static class LabelMappingTable
{
    // Explicit mapping - do NOT assume 1:1
    public static readonly IReadOnlyDictionary<string, LabelMapping> Mappings = new Dictionary<string, LabelMapping>(StringComparer.OrdinalIgnoreCase)
    {
        // Direct mappings - clear semantic match
        ["tckn"] = new() { DatasetLabel = "tckn", SafeCopyType = DetectionType.TcKimlikNo, Status = MappingStatus.DIRECT, Reason = "TCKN direct" },
        ["tc_kimlik_no"] = new() { DatasetLabel = "tc_kimlik_no", SafeCopyType = DetectionType.TcKimlikNo, Status = MappingStatus.DIRECT, Reason = "TCKN direct" },
        ["tc kimlik no"] = new() { DatasetLabel = "tc kimlik no", SafeCopyType = DetectionType.TcKimlikNo, Status = MappingStatus.DIRECT, Reason = "TCKN direct" },
        ["phone"] = new() { DatasetLabel = "phone", SafeCopyType = DetectionType.Phone, Status = MappingStatus.DIRECT, Reason = "Phone direct" },
        ["phone_number"] = new() { DatasetLabel = "phone_number", SafeCopyType = DetectionType.Phone, Status = MappingStatus.DIRECT, Reason = "Phone direct" },
        ["telefon"] = new() { DatasetLabel = "telefon", SafeCopyType = DetectionType.Phone, Status = MappingStatus.DIRECT, Reason = "Phone direct" },
        ["email"] = new() { DatasetLabel = "email", SafeCopyType = DetectionType.Email, Status = MappingStatus.DIRECT, Reason = "Email direct" },
        ["email_address"] = new() { DatasetLabel = "email_address", SafeCopyType = DetectionType.Email, Status = MappingStatus.DIRECT, Reason = "Email direct" },
        ["e-posta"] = new() { DatasetLabel = "e-posta", SafeCopyType = DetectionType.Email, Status = MappingStatus.DIRECT, Reason = "Email direct" },
        ["iban"] = new() { DatasetLabel = "iban", SafeCopyType = DetectionType.Iban, Status = MappingStatus.DIRECT, Reason = "IBAN direct" },
        ["full_name"] = new() { DatasetLabel = "full_name", SafeCopyType = DetectionType.FullName, Status = MappingStatus.DIRECT, Reason = "FullName direct" },
        ["person_name"] = new() { DatasetLabel = "person_name", SafeCopyType = DetectionType.FullName, Status = MappingStatus.DIRECT, Reason = "FullName direct" },
        ["name"] = new() { DatasetLabel = "name", SafeCopyType = DetectionType.FullName, Status = MappingStatus.MAPPED, Reason = "Name mapped to FullName (ambiguous without context)" },
        ["address"] = new() { DatasetLabel = "address", SafeCopyType = DetectionType.Address, Status = MappingStatus.DIRECT, Reason = "Address direct" },
        ["private_address"] = new() { DatasetLabel = "private_address", SafeCopyType = DetectionType.Address, Status = MappingStatus.DIRECT, Reason = "Address direct" },
        ["date"] = new() { DatasetLabel = "date", SafeCopyType = DetectionType.Date, Status = MappingStatus.DIRECT, Reason = "Date direct" },
        ["birthdate"] = new() { DatasetLabel = "birthdate", SafeCopyType = DetectionType.Date, Status = MappingStatus.DIRECT, Reason = "Date direct" },
        ["birth_date"] = new() { DatasetLabel = "birth_date", SafeCopyType = DetectionType.Date, Status = MappingStatus.DIRECT, Reason = "Date direct" },
        ["dogum_tarihi"] = new() { DatasetLabel = "dogum_tarihi", SafeCopyType = DetectionType.Date, Status = MappingStatus.DIRECT, Reason = "Date direct" },
        ["tesisat_no"] = new() { DatasetLabel = "tesisat_no", SafeCopyType = DetectionType.TesisatNo, Status = MappingStatus.DIRECT, Reason = "TesisatNo direct" },
        ["tesisat"] = new() { DatasetLabel = "tesisat", SafeCopyType = DetectionType.TesisatNo, Status = MappingStatus.DIRECT, Reason = "TesisatNo direct" },
        ["abone_no"] = new() { DatasetLabel = "abone_no", SafeCopyType = DetectionType.AboneNo, Status = MappingStatus.DIRECT, Reason = "AboneNo direct" },
        ["sayac_no"] = new() { DatasetLabel = "sayac_no", SafeCopyType = DetectionType.SayacNo, Status = MappingStatus.DIRECT, Reason = "SayacNo direct" },
        ["musteri_no"] = new() { DatasetLabel = "musteri_no", SafeCopyType = DetectionType.MusteriNo, Status = MappingStatus.DIRECT, Reason = "MusteriNo direct" },
        ["dosya_no"] = new() { DatasetLabel = "dosya_no", SafeCopyType = DetectionType.DosyaNo, Status = MappingStatus.DIRECT, Reason = "DosyaNo direct" },
        ["tax_id"] = new() { DatasetLabel = "tax_id", SafeCopyType = DetectionType.TaxId, Status = MappingStatus.DIRECT, Reason = "TaxId direct" },
        ["vkn"] = new() { DatasetLabel = "vkn", SafeCopyType = DetectionType.TaxId, Status = MappingStatus.MAPPED, Reason = "VKN mapped to TaxId" },
        ["credit_card"] = new() { DatasetLabel = "credit_card", SafeCopyType = DetectionType.CreditCard, Status = MappingStatus.DIRECT, Reason = "CreditCard direct" },
        ["passport"] = new() { DatasetLabel = "passport", SafeCopyType = DetectionType.PassportNo, Status = MappingStatus.DIRECT, Reason = "Passport direct" },

        // Mapped / ambiguous - document semantic mismatch, do not silently treat as failure
        ["account_number"] = new() { DatasetLabel = "account_number", SafeCopyType = null, Status = MappingStatus.AMBIGUOUS, Reason = "account_number ambiguous: could be TCKN, IBAN, or generic - document mismatch, not counted as detector failure unless explicitly mapped" },
        ["customer_id"] = new() { DatasetLabel = "customer_id", SafeCopyType = null, Status = MappingStatus.AMBIGUOUS, Reason = "Ambiguous customer_id" },
        ["id_number"] = new() { DatasetLabel = "id_number", SafeCopyType = null, Status = MappingStatus.AMBIGUOUS, Reason = "Ambiguous id_number" },

        // Private prefixed variants - map to direct types
        ["private_date"] = new() { DatasetLabel = "private_date", SafeCopyType = DetectionType.Date, Status = MappingStatus.DIRECT, Reason = "Date direct (private_date)" },
        ["private_email"] = new() { DatasetLabel = "private_email", SafeCopyType = DetectionType.Email, Status = MappingStatus.DIRECT, Reason = "Email direct (private_email)" },
        ["private_phone"] = new() { DatasetLabel = "private_phone", SafeCopyType = DetectionType.Phone, Status = MappingStatus.DIRECT, Reason = "Phone direct (private_phone)" },
        ["private_person"] = new() { DatasetLabel = "private_person", SafeCopyType = DetectionType.FullName, Status = MappingStatus.DIRECT, Reason = "FullName direct (private_person)" },
        ["private_iban"] = new() { DatasetLabel = "private_iban", SafeCopyType = DetectionType.Iban, Status = MappingStatus.DIRECT, Reason = "IBAN direct (private_iban)" },
        ["private_tckn"] = new() { DatasetLabel = "private_tckn", SafeCopyType = DetectionType.TcKimlikNo, Status = MappingStatus.DIRECT, Reason = "TCKN direct (private_tckn)" },

        // Unsupported - must not be counted as detector failures
        ["private_url"] = new() { DatasetLabel = "private_url", SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = "Unsupported category private_url" },
        ["secret"] = new() { DatasetLabel = "secret", SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = "Unsupported secret" },
        ["other"] = new() { DatasetLabel = "other", SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = "Unsupported other" },
        ["unknown"] = new() { DatasetLabel = "unknown", SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = "Unsupported unknown" },
    };

    public static LabelMapping Resolve(string datasetLabel)
    {
        if (string.IsNullOrWhiteSpace(datasetLabel)) return new LabelMapping { DatasetLabel = datasetLabel, SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = "Empty label" };
        if (Mappings.TryGetValue(datasetLabel.Trim(), out var m)) return m;
        // Try normalized: lower, replace spaces/hyphens with underscore
        var norm = datasetLabel.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        if (Mappings.TryGetValue(norm, out var m2)) return m2;
        // Handle private_ prefix generically (e.g., private_person -> person)
        if (norm.StartsWith("private_"))
        {
            var suffix = norm.Substring(8);
            if (Mappings.TryGetValue(suffix, out var m3)) return new LabelMapping { DatasetLabel = datasetLabel, SafeCopyType = m3.SafeCopyType, Status = m3.Status, Reason = m3.Reason + " (via private_ prefix)" };
        }
        return new LabelMapping { DatasetLabel = datasetLabel, SafeCopyType = null, Status = MappingStatus.UNSUPPORTED, Reason = $"No mapping for '{datasetLabel}'" };
    }
}

public sealed class GroundTruthEntity
{
    public string Label { get; init; } = string.Empty;
    public int Start { get; init; }
    public int End { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Scenario { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public DetectionType? MappedType { get; init; }
    public MappingStatus MappingStatus { get; init; }
    public string MappingReason { get; init; } = string.Empty;
    public int Length => End - Start;
}

public sealed class BenchmarkRecord
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public List<GroundTruthEntity> Entities { get; init; } = new();
    public string Scenario { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string RawJson { get; init; } = string.Empty;
    public bool IsMalformed { get; init; }
    public string? ParseError { get; init; }
    public int LineNumber { get; init; }
    public bool IsEmptySpans => Entities.Count == 0 && !IsMalformed;
}

public sealed class BenchmarkDetection
{
    public DetectionType Type { get; init; }
    public string Value { get; init; } = string.Empty;
    public int Start { get; init; }
    public int End { get; init; }
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public int Length => End - Start;
}

public enum MatchKind
{
    Exact,
    Overlap,
    BoundaryMismatch,
    Missed,
    FalsePositive
}

public sealed class EntityMatch
{
    public GroundTruthEntity? GroundTruth { get; init; }
    public BenchmarkDetection? Detection { get; init; }
    public MatchKind Kind { get; init; }
    public double OverlapRatio { get; init; }
    public double IoU { get; init; }
    public string RecordId { get; init; } = string.Empty;
}

public sealed class BenchmarkMetrics
{
    public int TP { get; init; }
    public int FN { get; init; }
    public int FP { get; init; }
    public int Support => TP + FN;
    public double Precision => TP + FP == 0 ? (TP == 0 ? 1.0 : 0) : (double)TP / (TP + FP);
    public double Recall => TP + FN == 0 ? 1.0 : (double)TP / (TP + FN);
    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
}

public sealed class PerTypeMetrics
{
    public DetectionType? SafeCopyType { get; init; }
    public string DatasetLabel { get; init; } = string.Empty;
    public MappingStatus MappingStatus { get; init; }
    public int Support { get; init; }
    public int TP { get; init; }
    public int FN { get; init; }
    public int FP { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
    public int BoundaryMismatches { get; init; }
}

public sealed class PerScenarioMetrics
{
    public string Scenario { get; init; } = string.Empty;
    public int Support { get; init; }
    public int TP { get; init; }
    public int FN { get; init; }
    public int FP { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
}

public sealed class BenchmarkResult
{
    public int TotalRecords { get; init; }
    public int MalformedRecords { get; init; }
    public int EmptySpanRecords { get; init; }
    public int TotalGroundTruthEntities { get; init; }
    public int SupportedEntities { get; init; }
    public int UnsupportedEntities { get; init; }
    public int AmbiguousEntities { get; init; }
    public BenchmarkMetrics Overall { get; init; } = new();
    public BenchmarkMetrics OverallStrict { get; init; } = new();
    public BenchmarkMetrics OverallOverlap { get; init; } = new();
    public List<PerTypeMetrics> PerType { get; init; } = new();
    public List<PerScenarioMetrics> PerScenario { get; init; } = new();
    public List<PerScenarioMetrics> PerDifficulty { get; init; } = new();
    public int ExactMatches { get; init; }
    public int OverlapMatches { get; init; }
    public int BoundaryMismatches { get; init; }
    public List<EntityMatch> FalseNegatives { get; init; } = new();
    public List<EntityMatch> FalsePositives { get; init; } = new();
    public List<EntityMatch> BoundaryMismatchDetails { get; init; } = new();
    public Dictionary<string, int> UnsupportedLabelCounts { get; init; } = new();
    public Dictionary<string, int> AmbiguousLabelCounts { get; init; } = new();
    public Dictionary<string, int> ScenarioCounts { get; init; } = new();
    public Dictionary<string, int> DifficultyCounts { get; init; } = new();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public string DatasetPath { get; init; } = string.Empty;
    public string SafeCopyVersion { get; init; } = string.Empty;
    public int PossibleDetectionsTotal { get; init; }
    public int PossibleOverlappingGroundTruth { get; init; }
    public int PossibleWithoutGroundTruth { get; init; }
    public Dictionary<string, int> StructuredValidity { get; init; } = new();
}
