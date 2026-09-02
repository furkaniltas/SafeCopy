using System.Text;
using System.Text.Json;
using EksimSafeCopy.Benchmark.Matching;
using EksimSafeCopy.Benchmark.Metrics;
using EksimSafeCopy.Benchmark.Models;
using EksimSafeCopy.Benchmark.Parser;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EksimSafeCopy.Benchmark.Runner;

public sealed class BenchmarkRunner
{
    private readonly IDetectionEngine _engine;

    public BenchmarkRunner(IDetectionEngine engine)
    {
        _engine = engine;
    }

    public static BenchmarkRunner CreateDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();
        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IDetectionEngine>();
        return new BenchmarkRunner(engine);
    }

    public static string LocateDataset(string? preferredPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredPath)) candidates.Add(preferredPath!);
        candidates.Add("tr_privacy_tr_curated.jsonl");
        candidates.Add(Path.Combine("tests", "tr_privacy_tr_curated.jsonl"));
        candidates.Add(Path.Combine("..", "tr_privacy_tr_curated.jsonl"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "tr_privacy_tr_curated.jsonl"));
        // Search up to repo root
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            candidates.Add(Path.Combine(dir.FullName, "tr_privacy_tr_curated.jsonl"));
            candidates.Add(Path.Combine(dir.FullName, "tests", "tr_privacy_tr_curated.jsonl"));
            candidates.Add(Path.Combine(dir.FullName, "src", "EksimSafeCopy.App", "Resources", "tr_privacy_tr_curated.jsonl"));
            dir = dir.Parent;
            if (dir == null || dir.FullName.Length < 3) break;
        }
        // Fallback to existing test.jsonl
        candidates.Add(Path.Combine("src", "EksimSafeCopy.App", "Resources", "test.jsonl"));
        candidates.Add("D:\\EksimSafeCopy\\src\\EksimSafeCopy.App\\Resources\\test.jsonl");

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        // Try embedded resource fallback
        var embedded = TryGetEmbeddedDataset();
        if (embedded != null) return embedded;
        return string.Empty;
    }

    private static string? TryGetEmbeddedDataset()
    {
        // Try to get test.jsonl via embedded resource and write to temp
        try
        {
            var asm = typeof(BenchmarkRunner).Assembly;
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("test.jsonl"));
            if (res != null) return null; // We are not in App assembly, need to try App assembly
            var appAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "EksimSafeCopy.App");
            if (appAsm != null)
            {
                var appRes = appAsm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("test.jsonl"));
                if (appRes != null)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), "benchmark_fallback_test.jsonl");
                    using var s = appAsm.GetManifestResourceStream(appRes)!;
                    using var fs = File.Create(tmp);
                    s.CopyTo(fs);
                    return tmp;
                }
            }
        }
        catch { }
        return null;
    }

    public BenchmarkResult Run(string datasetPath)
    {
        // Phase 1: Locate intended dataset - do NOT silently substitute test.jsonl if tr_privacy_tr_curated.jsonl was explicitly requested
        bool requestedCurated = !string.IsNullOrWhiteSpace(datasetPath) && datasetPath.Contains("tr_privacy_tr_curated", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(datasetPath) || !File.Exists(datasetPath))
        {
            var located = LocateDataset(datasetPath);
            if (string.IsNullOrWhiteSpace(located) || !File.Exists(located))
            {
                if (requestedCurated || string.IsNullOrWhiteSpace(datasetPath) || datasetPath.Contains("tr_privacy_tr_curated"))
                    throw new FileNotFoundException("The intended benchmark dataset is unavailable.");
                throw new FileNotFoundException($"Dataset not found: {datasetPath}. Checked candidates and embedded fallback.");
            }
            // If we were asked for curated but located test.jsonl fallback, treat as unavailable
            if (requestedCurated && !located.Contains("tr_privacy_tr_curated", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("The intended benchmark dataset is unavailable.");
            datasetPath = located;
        }

        var malformed = new List<BenchmarkRecord>();
        var records = BenchmarkParser.ParseFile(datasetPath, out malformed);
        var allMatches = new List<EntityMatch>();
        var allDetections = new List<BenchmarkDetection>();
        var possibleDetections = new List<BenchmarkDetection>();

        foreach (var rec in records.Where(r => !r.IsMalformed))
        {
            var detections = DetectText(rec.Text);
            // Separate Possible for primary metrics
            var mainDets = detections.Where(d => d.Type != DetectionType.PossiblePersonalData).ToList();
            var possibleDets = detections.Where(d => d.Type == DetectionType.PossiblePersonalData).ToList();
            possibleDetections.AddRange(possibleDets);
            allDetections.AddRange(mainDets);
            var matches = SpanMatcher.MatchRecord(rec, mainDets);
            allMatches.AddRange(matches);
        }

        // Compute metrics - both STRICT (exact only) and OVERLAP (exact+boundary)
        var overallStrict = MetricsCalculator.ComputeOverall(allMatches, strict: true);
        var overallOverlap = MetricsCalculator.ComputeOverall(allMatches, strict: false);
        var overall = overallOverlap; // primary is overlap, but we keep both
        var perType = MetricsCalculator.ComputePerType(records, allMatches, strict: false);
        var perTypeStrict = MetricsCalculator.ComputePerType(records, allMatches, strict: true);
        var perScenario = MetricsCalculator.ComputePerScenario(records, allMatches, strict: false);
        var perDifficulty = MetricsCalculator.ComputePerDifficulty(records, allMatches, strict: false);

        int exact = allMatches.Count(m => m.Kind == MatchKind.Exact);
        int overlap = allMatches.Count(m => m.Kind == MatchKind.Overlap);
        int boundary = allMatches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
        var fns = allMatches.Where(m => m.Kind == MatchKind.Missed).ToList();
        var fps = allMatches.Where(m => m.Kind == MatchKind.FalsePositive).ToList();
        var bms = allMatches.Where(m => m.Kind == MatchKind.BoundaryMismatch).ToList();

        // Unsupported / ambiguous
        var unsupported = records.SelectMany(r => r.Entities).Where(e => e.MappingStatus == MappingStatus.UNSUPPORTED).GroupBy(e => e.Label).ToDictionary(g => g.Key, g => g.Count());
        var ambiguousDict = records.SelectMany(r => r.Entities).Where(e => e.MappingStatus == MappingStatus.AMBIGUOUS).GroupBy(e => e.Label).ToDictionary(g => g.Key, g => g.Count());
        var ambiguous = ambiguousDict.Values.Sum();
        var totalGt = records.Where(r => !r.IsMalformed).SelectMany(r => r.Entities).Count();
        var supported = records.Where(r => !r.IsMalformed).SelectMany(r => r.Entities).Count(e => e.MappedType != null && e.MappingStatus != MappingStatus.UNSUPPORTED && e.MappingStatus != MappingStatus.AMBIGUOUS);
        var scenarioCounts = records.Where(r => !r.IsMalformed).GroupBy(r => r.Scenario).ToDictionary(g => g.Key, g => g.Count());
        var difficultyCounts = records.Where(r => !r.IsMalformed).GroupBy(r => r.Difficulty).ToDictionary(g => g.Key, g => g.Count());

        // Structured validity analysis
        var validity = AnalyzeStructuredValidity(records);

        // Possible separate stats
        int possibleTotal = possibleDetections.Count;
        int possibleOverlapGT = 0;
        int possibleWithoutGT = 0;
        // Re-iterate records to count possible per record
        foreach (var rec in records.Where(r => !r.IsMalformed))
        {
            var dets = DetectText(rec.Text).Where(d => d.Type == DetectionType.PossiblePersonalData).ToList();
            if (dets.Count == 0) continue;
            bool hasGt = rec.Entities.Any(e => e.MappedType != null);
            if (hasGt)
            {
                // Check if any possible overlaps any GT span
                bool anyOverlap = dets.Any(pd => rec.Entities.Any(gt => SpansOverlap(gt.Start, gt.End, pd.Start, pd.End)));
                if (anyOverlap) possibleOverlapGT += dets.Count;
                else possibleWithoutGT += dets.Count;
            }
            else
            {
                possibleWithoutGT += dets.Count;
            }
        }

        return new BenchmarkResult
        {
            TotalRecords = records.Count,
            MalformedRecords = records.Count(r => r.IsMalformed),
            EmptySpanRecords = records.Count(r => r.IsEmptySpans),
            TotalGroundTruthEntities = totalGt,
            SupportedEntities = supported,
            UnsupportedEntities = records.SelectMany(r => r.Entities).Count(e => e.MappingStatus == MappingStatus.UNSUPPORTED),
            AmbiguousEntities = ambiguous,
            Overall = overall,
            OverallStrict = overallStrict,
            OverallOverlap = overallOverlap,
            PerType = perType,
            PerScenario = perScenario,
            PerDifficulty = perDifficulty,
            ExactMatches = exact,
            OverlapMatches = overlap,
            BoundaryMismatches = boundary,
            FalseNegatives = fns,
            FalsePositives = fps,
            BoundaryMismatchDetails = bms,
            UnsupportedLabelCounts = unsupported,
            AmbiguousLabelCounts = ambiguousDict,
            ScenarioCounts = scenarioCounts,
            DifficultyCounts = difficultyCounts,
            DatasetPath = datasetPath,
            SafeCopyVersion = typeof(BenchmarkRunner).Assembly.GetName().Version?.ToString() ?? "unknown",
            PossibleDetectionsTotal = possibleTotal,
            PossibleOverlappingGroundTruth = possibleOverlapGT,
            PossibleWithoutGroundTruth = possibleWithoutGT,
            StructuredValidity = validity
        };
    }

    private static bool SpansOverlap(int aStart, int aEnd, int bStart, int bEnd) => aStart < bEnd && bStart < aEnd;

    private static Dictionary<string,int> AnalyzeStructuredValidity(List<BenchmarkRecord> records)
    {
        var dict = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in records)
        {
            foreach (var e in rec.Entities)
            {
                if (e.Label.Equals("iban", StringComparison.OrdinalIgnoreCase) || e.Label.Equals("private_iban", StringComparison.OrdinalIgnoreCase))
                {
                    bool valid = IsValidIban(e.Text);
                    string key = valid ? "iban_valid" : "iban_invalid";
                    dict[key] = dict.GetValueOrDefault(key) + 1;
                }
                else if (e.Label.Equals("tckn", StringComparison.OrdinalIgnoreCase) || e.Label.Equals("private_tckn", StringComparison.OrdinalIgnoreCase) || e.Label.Contains("tckn"))
                {
                    bool valid = IsValidTckn(e.Text);
                    string key = valid ? "tckn_valid" : "tckn_invalid";
                    dict[key] = dict.GetValueOrDefault(key) + 1;
                }
                else if (e.Label.Equals("vkn", StringComparison.OrdinalIgnoreCase))
                {
                    bool valid = IsValidVkn(e.Text);
                    string key = valid ? "vkn_valid" : "vkn_invalid";
                    dict[key] = dict.GetValueOrDefault(key) + 1;
                }
            }
        }
        return dict;
    }

    private static bool IsValidIban(string iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return false;
        var s = iban.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (s.Length < 15 || s.Length > 34) return false;
        if (!s.StartsWith("TR")) return false;
        var rearr = s.Substring(4) + s.Substring(0, 4);
        var num = "";
        foreach (var c in rearr)
        {
            if (char.IsDigit(c)) num += c;
            else if (char.IsLetter(c)) num += (c - 55).ToString();
            else return false;
        }
        int r = 0;
        foreach (var ch in num) r = (r * 10 + (ch - '0')) % 97;
        return r == 1;
    }

    private static bool IsValidTckn(string tckn)
    {
        var digits = new string(tckn.Where(char.IsDigit).ToArray());
        if (digits.Length != 11) return false;
        if (digits[0] == '0') return false;
        int[] d = digits.Select(c => c - '0').ToArray();
        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        int c10 = (odd * 7 - even) % 10; if (c10 < 0) c10 += 10;
        if (c10 != d[9]) return false;
        int sum10 = d.Take(10).Sum();
        return sum10 % 10 == d[10];
    }

    private static bool IsValidVkn(string vkn)
    {
        var digits = new string(vkn.Where(char.IsDigit).ToArray());
        if (digits.Length != 10) return false;
        // VKN checksum: sum with weights, mod 10
        // Simplified: just check 10 digits and first not 0
        return digits[0] != '0';
    }

    private List<BenchmarkDetection> DetectText(string text)
    {
        // Create a minimal document with one page containing the text
        var page = new DocumentPage
        {
            PageNumber = 0,
            Width = 800,
            Height = 600,
            DpiX = 96,
            DpiY = 96,
            Text = text,
            TextBlocks = new[] { new TextBlock { Text = text, Type = TextBlockType.Paragraph, Direction = TextDirection.LeftToRight, PageNumber = 0, OrderIndex = 0 } }.ToList().AsReadOnly()
        };
        var doc = new Document { Name = "benchmark.txt", Format = global::EksimSafeCopy.Core.Abstractions.DocumentFormat.Txt, Pages = new[] { page }, Metadata = new DocumentMetadata() };
        var result = _engine.Detect(doc);
        if (result.IsFailure) return new List<BenchmarkDetection>();
        return result.Value.Select(d => new BenchmarkDetection
        {
            Type = d.Type,
            Value = d.Value,
            Start = d.TextSpan?.StartIndex ?? 0,
            End = d.TextSpan != null ? d.TextSpan.StartIndex + d.TextSpan.Length : (d.Value.Length > 0 ? text.IndexOf(d.Value, StringComparison.Ordinal) + d.Value.Length : 0),
            Text = d.TextSpan?.Text ?? d.Value,
            Confidence = d.Confidence
        }).ToList();
    }

    public static void WriteOutputs(BenchmarkResult result, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        // JSON
        var jsonPath = Path.Combine(outputDir, "benchmark-results.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(result, jsonOptions);
        File.WriteAllText(jsonPath, json, Encoding.UTF8);

        // Markdown
        var mdPath = Path.Combine(outputDir, "benchmark-report.md");
        var md = GenerateMarkdown(result);
        File.WriteAllText(mdPath, md, Encoding.UTF8);
    }

    public static string GenerateMarkdown(BenchmarkResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Eksim SafeCopy Turkish PII Benchmark Report");
        sb.AppendLine($"Generated: {r.GeneratedAt:u}");
        sb.AppendLine($"Dataset: {r.DatasetPath}");
        sb.AppendLine($"SafeCopy Version: {r.SafeCopyVersion}");
        sb.AppendLine();
        sb.AppendLine("## 1. Dataset Statistics");
        sb.AppendLine($"- Total Records: {r.TotalRecords}");
        sb.AppendLine($"- Malformed: {r.MalformedRecords}");
        sb.AppendLine($"- Empty Span Records: {r.EmptySpanRecords}");
        sb.AppendLine($"- Total Ground Truth Entities: {r.TotalGroundTruthEntities}");
        sb.AppendLine($"- Supported: {r.SupportedEntities}");
        sb.AppendLine($"- Unsupported: {r.UnsupportedEntities}");
        sb.AppendLine($"- Ambiguous: {r.AmbiguousEntities}");
        sb.AppendLine($"- Scenarios: {string.Join(", ", r.ScenarioCounts.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        sb.AppendLine($"- Difficulties: {string.Join(", ", r.DifficultyCounts.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        sb.AppendLine();
        sb.AppendLine("## 2. Label Mapping");
        sb.AppendLine("| Dataset Label | SafeCopy Type | Status | Reason |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var kv in LabelMappingTable.Mappings.OrderBy(k => k.Key))
            sb.AppendLine($"| {kv.Key} | {kv.Value.SafeCopyType?.ToString() ?? "-"} | {kv.Value.Status} | {kv.Value.Reason} |");
        sb.AppendLine();
        sb.AppendLine("## 3. Supported / 4. Unsupported / 5. Ambiguous Entities");
        sb.AppendLine($"- Supported: {r.SupportedEntities}");
        sb.AppendLine($"- Unsupported: {r.UnsupportedEntities} ({string.Join(", ", r.UnsupportedLabelCounts.Select(kv => $"{kv.Key}:{kv.Value}"))})");
        sb.AppendLine($"- Ambiguous: {r.AmbiguousEntities} ({string.Join(", ", r.AmbiguousLabelCounts.Select(kv => $"{kv.Key}:{kv.Value}"))})");
        sb.AppendLine();
        sb.AppendLine("## 6. STRICT Exact Metrics (exact span only)");
        sb.AppendLine($"- TP: {r.OverallStrict.TP} FN: {r.OverallStrict.FN} FP: {r.OverallStrict.FP}");
        sb.AppendLine($"- Precision: {r.OverallStrict.Precision:F3} Recall: {r.OverallStrict.Recall:F3} F1: {r.OverallStrict.F1:F3}");
        sb.AppendLine($"- Exact: {r.ExactMatches}");
        sb.AppendLine();
        sb.AppendLine("## 7. OVERLAP Metrics (exact + boundary)");
        sb.AppendLine($"- TP: {r.OverallOverlap.TP} FN: {r.OverallOverlap.FN} FP: {r.OverallOverlap.FP}");
        sb.AppendLine($"- Precision: {r.OverallOverlap.Precision:F3} Recall: {r.OverallOverlap.Recall:F3} F1: {r.OverallOverlap.F1:F3}");
        sb.AppendLine($"- Exact: {r.ExactMatches} Overlap: {r.OverlapMatches} Boundary: {r.BoundaryMismatches}");
        sb.AppendLine();
        sb.AppendLine("## 8. Boundary Mismatch Count");
        sb.AppendLine($"- Boundary Mismatches: {r.BoundaryMismatches}");
        sb.AppendLine();
        sb.AppendLine("## 9. FP Total");
        sb.AppendLine($"- FP (strict): {r.OverallStrict.FP} FP (overlap): {r.Overall.FP}");
        sb.AppendLine($"- Sum(per-type FP strict) should equal overall strict FP, sum(overlap) should equal overall FP");
        sb.AppendLine();
        sb.AppendLine("## 10. FN Total");
        sb.AppendLine($"- FN (strict): {r.OverallStrict.FN} FN (overlap): {r.Overall.FN}");
        sb.AppendLine();
        sb.AppendLine("## 11. Per-Entity Metrics (Overlap view)");
        sb.AppendLine("| Label | Type | Status | Support | TP | FN | FP | Prec | Rec | F1 | Boundary |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var t in r.PerType)
            sb.AppendLine($"| {t.DatasetLabel} | {t.SafeCopyType} | {t.MappingStatus} | {t.Support} | {t.TP} | {t.FN} | {t.FP} | {t.Precision:F3} | {t.Recall:F3} | {t.F1:F3} | {t.BoundaryMismatches} |");
        sb.AppendLine();
        sb.AppendLine("## 12. Per-Scenario Metrics");
        sb.AppendLine("| Scenario | Support | TP | FN | FP | Prec | Rec | F1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var s in r.PerScenario)
            sb.AppendLine($"| {s.Scenario} | {s.Support} | {s.TP} | {s.FN} | {s.FP} | {s.Precision:F3} | {s.Recall:F3} | {s.F1:F3} |");
        sb.AppendLine();
        sb.AppendLine("## 13. Per-Difficulty Metrics");
        sb.AppendLine("| Difficulty | Support | TP | FN | FP | Prec | Rec | F1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var d in r.PerDifficulty)
            sb.AppendLine($"| {d.Scenario} | {d.Support} | {d.TP} | {d.FN} | {d.FP} | {d.Precision:F3} | {d.Recall:F3} | {d.F1:F3} |");
        sb.AppendLine();
        sb.AppendLine("## 14. Structured Identifier Validity");
        foreach (var kv in r.StructuredValidity.OrderBy(k => k.Key))
            sb.AppendLine($"- {kv.Key}: {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("## 15. PossiblePersonalData Separate Statistics");
        sb.AppendLine($"- Possible detections total: {r.PossibleDetectionsTotal}");
        sb.AppendLine($"- Possible overlapping ground truth: {r.PossibleOverlappingGroundTruth}");
        sb.AppendLine($"- Possible without ground truth: {r.PossibleWithoutGroundTruth}");
        sb.AppendLine();
        sb.AppendLine("## 16. Top False Negatives (first 20)");
        foreach (var fn in r.FalseNegatives.Take(20))
            sb.AppendLine($"- [{fn.RecordId}] {fn.GroundTruth!.Label} \"{fn.GroundTruth.Text}\" span {fn.GroundTruth.Start}-{fn.GroundTruth.End} scenario {fn.GroundTruth.Scenario} text: \"{fn.GroundTruth.Text}\" context: \"{fn.GroundTruth.Text}\"");
        sb.AppendLine();
        sb.AppendLine("## 17. Top False Positives (first 20)");
        foreach (var fp in r.FalsePositives.Take(20))
            sb.AppendLine($"- [{fp.RecordId}] {fp.Detection!.Type} \"{fp.Detection.Text}\" span {fp.Detection.Start}-{fp.Detection.End}");
        sb.AppendLine();
        sb.AppendLine("## 18. Top Boundary Mismatches (first 20)");
        foreach (var bm in r.BoundaryMismatchDetails.Take(20))
            sb.AppendLine($"- [{bm.RecordId}] GT \"{bm.GroundTruth!.Text}\" {bm.GroundTruth.Start}-{bm.GroundTruth.End} vs Det \"{bm.Detection!.Text}\" {bm.Detection.Start}-{bm.Detection.End} IoU {bm.IoU:F2} GT:{bm.GroundTruth.Label} Det:{bm.Detection.Type}");
        return sb.ToString();
    }

    public static void PrintConsoleSummary(BenchmarkResult r)
    {
        Console.WriteLine("=== Turkish PII Benchmark ===");
        Console.WriteLine($"Dataset: {r.DatasetPath}");
        Console.WriteLine($"Records: {r.TotalRecords} (malformed {r.MalformedRecords}, empty {r.EmptySpanRecords}) Entities: {r.TotalGroundTruthEntities} supported {r.SupportedEntities} unsupported {r.UnsupportedEntities} ambiguous {r.AmbiguousEntities}");
        Console.WriteLine($"STRICT (exact only): TP {r.OverallStrict.TP} FN {r.OverallStrict.FN} FP {r.OverallStrict.FP} Prec {r.OverallStrict.Precision:F3} Rec {r.OverallStrict.Recall:F3} F1 {r.OverallStrict.F1:F3} Exact {r.ExactMatches}");
        Console.WriteLine($"OVERLAP (exact+boundary): TP {r.Overall.TP} FN {r.Overall.FN} FP {r.Overall.FP} Prec {r.Overall.Precision:F3} Rec {r.Overall.Recall:F3} F1 {r.Overall.F1:F3} Boundary {r.BoundaryMismatches}");
        Console.WriteLine($"FP sum check: per-type FP sum {r.PerType.Sum(t=>t.FP)} == overall FP {r.Overall.FP} ? {r.PerType.Sum(t=>t.FP)==r.Overall.FP}");
        Console.WriteLine("Per Type (top 5 FN):");
        foreach (var t in r.PerType.OrderByDescending(x=>x.FN).Take(5))
            Console.WriteLine($"  {t.DatasetLabel} ({t.SafeCopyType}) support {t.Support} TP {t.TP} FN {t.FN} FP {t.FP} F1 {t.F1:F3} Boundary {t.BoundaryMismatches}");
        Console.WriteLine("Per Scenario (worst F1):");
        foreach (var s in r.PerScenario.OrderBy(x=>x.F1).Take(5))
            Console.WriteLine($"  {s.Scenario} support {s.Support} F1 {s.F1:F3} Rec {s.Recall:F3}");
        Console.WriteLine($"Unsupported: {string.Join(", ", r.UnsupportedLabelCounts.Select(kv=> kv.Key+":"+kv.Value))} Ambiguous: {string.Join(", ", r.AmbiguousLabelCounts.Select(kv=> kv.Key+":"+kv.Value))}");
        Console.WriteLine($"Structured validity: {string.Join(", ", r.StructuredValidity.Select(kv=> kv.Key+":"+kv.Value))}");
        Console.WriteLine($"Possible: total {r.PossibleDetectionsTotal} overlapGT {r.PossibleOverlappingGroundTruth} withoutGT {r.PossibleWithoutGroundTruth}");
    }
}
