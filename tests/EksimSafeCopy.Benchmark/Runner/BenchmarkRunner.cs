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
        if (string.IsNullOrWhiteSpace(datasetPath) || !File.Exists(datasetPath))
        {
            // Try locate
            datasetPath = LocateDataset(datasetPath);
            if (string.IsNullOrWhiteSpace(datasetPath) || !File.Exists(datasetPath))
                throw new FileNotFoundException($"Dataset not found: {datasetPath}. Checked candidates and embedded fallback.");
        }

        var malformed = new List<BenchmarkRecord>();
        var records = BenchmarkParser.ParseFile(datasetPath, out malformed);
        var allMatches = new List<EntityMatch>();
        var allDetections = new List<BenchmarkDetection>();

        foreach (var rec in records.Where(r => !r.IsMalformed))
        {
            var detections = DetectText(rec.Text);
            allDetections.AddRange(detections);
            var matches = SpanMatcher.MatchRecord(rec, detections);
            allMatches.AddRange(matches);
        }

        // Compute metrics
        var overall = MetricsCalculator.ComputeOverall(allMatches);
        var perType = MetricsCalculator.ComputePerType(records, allMatches);
        var perScenario = MetricsCalculator.ComputePerScenario(records, allMatches);

        int exact = allMatches.Count(m => m.Kind == MatchKind.Exact);
        int overlap = allMatches.Count(m => m.Kind == MatchKind.Overlap);
        int boundary = allMatches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
        var fns = allMatches.Where(m => m.Kind == MatchKind.Missed).ToList();
        var fps = allMatches.Where(m => m.Kind == MatchKind.FalsePositive).ToList();
        var bms = allMatches.Where(m => m.Kind == MatchKind.BoundaryMismatch).ToList();

        // Unsupported counts
        var unsupported = records.SelectMany(r => r.Entities).Where(e => e.MappingStatus == MappingStatus.UNSUPPORTED).GroupBy(e => e.Label).ToDictionary(g => g.Key, g => g.Count());
        var ambiguous = records.SelectMany(r => r.Entities).Where(e => e.MappingStatus == MappingStatus.AMBIGUOUS).Count();
        var totalGt = records.Where(r => !r.IsMalformed).SelectMany(r => r.Entities).Count();
        var supported = records.Where(r => !r.IsMalformed).SelectMany(r => r.Entities).Count(e => e.MappedType != null && e.MappingStatus != MappingStatus.UNSUPPORTED && e.MappingStatus != MappingStatus.AMBIGUOUS);
        var scenarioCounts = records.Where(r => !r.IsMalformed).GroupBy(r => r.Scenario).ToDictionary(g => g.Key, g => g.Count());
        var difficultyCounts = records.Where(r => !r.IsMalformed).GroupBy(r => r.Difficulty).ToDictionary(g => g.Key, g => g.Count());

        return new BenchmarkResult
        {
            TotalRecords = records.Count,
            MalformedRecords = records.Count(r => r.IsMalformed),
            EmptySpanRecords = records.Count(r => r.IsEmptySpans),
            TotalGroundTruthEntities = totalGt,
            SupportedEntities = supported,
            UnsupportedEntities = totalGt - supported,
            AmbiguousEntities = ambiguous,
            Overall = overall,
            PerType = perType,
            PerScenario = perScenario,
            ExactMatches = exact,
            OverlapMatches = overlap,
            BoundaryMismatches = boundary,
            FalseNegatives = fns,
            FalsePositives = fps,
            BoundaryMismatchDetails = bms,
            UnsupportedLabelCounts = unsupported,
            ScenarioCounts = scenarioCounts,
            DifficultyCounts = difficultyCounts,
            DatasetPath = datasetPath,
            SafeCopyVersion = typeof(BenchmarkRunner).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
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
        sb.AppendLine("## Dataset Statistics");
        sb.AppendLine($"- Total Records: {r.TotalRecords}");
        sb.AppendLine($"- Malformed: {r.MalformedRecords}");
        sb.AppendLine($"- Empty Span Records: {r.EmptySpanRecords}");
        sb.AppendLine($"- Total Ground Truth Entities: {r.TotalGroundTruthEntities}");
        sb.AppendLine($"- Supported: {r.SupportedEntities}");
        sb.AppendLine($"- Unsupported: {r.UnsupportedEntities}");
        sb.AppendLine($"- Ambiguous: {r.AmbiguousEntities}");
        sb.AppendLine();
        sb.AppendLine("## Label Mapping");
        sb.AppendLine("| Dataset Label | SafeCopy Type | Status | Reason |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var kv in LabelMappingTable.Mappings.OrderBy(k => k.Key))
            sb.AppendLine($"| {kv.Key} | {kv.Value.SafeCopyType?.ToString() ?? "-"} | {kv.Value.Status} | {kv.Value.Reason} |");
        sb.AppendLine();
        sb.AppendLine("## Overall Metrics");
        sb.AppendLine($"- TP: {r.Overall.TP} FN: {r.Overall.FN} FP: {r.Overall.FP}");
        sb.AppendLine($"- Precision: {r.Overall.Precision:F3} Recall: {r.Overall.Recall:F3} F1: {r.Overall.F1:F3}");
        sb.AppendLine($"- Exact: {r.ExactMatches} Overlap: {r.OverlapMatches} Boundary: {r.BoundaryMismatches}");
        sb.AppendLine();
        sb.AppendLine("## Per Type");
        sb.AppendLine("| Label | Type | Status | Support | TP | FN | FP | Prec | Rec | F1 | Boundary |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var t in r.PerType)
            sb.AppendLine($"| {t.DatasetLabel} | {t.SafeCopyType} | {t.MappingStatus} | {t.Support} | {t.TP} | {t.FN} | {t.FP} | {t.Precision:F3} | {t.Recall:F3} | {t.F1:F3} | {t.BoundaryMismatches} |");
        sb.AppendLine();
        sb.AppendLine("## Per Scenario");
        sb.AppendLine("| Scenario | Support | TP | FN | FP | Prec | Rec | F1 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var s in r.PerScenario)
            sb.AppendLine($"| {s.Scenario} | {s.Support} | {s.TP} | {s.FN} | {s.FP} | {s.Precision:F3} | {s.Recall:F3} | {s.F1:F3} |");
        sb.AppendLine();
        sb.AppendLine("## Unsupported Labels");
        foreach (var u in r.UnsupportedLabelCounts) sb.AppendLine($"- {u.Key}: {u.Value}");
        sb.AppendLine();
        sb.AppendLine("## Top False Negatives (first 20)");
        foreach (var fn in r.FalseNegatives.Take(20))
            sb.AppendLine($"- [{fn.RecordId}] {fn.GroundTruth!.Label} \"{fn.GroundTruth.Text}\" span {fn.GroundTruth.Start}-{fn.GroundTruth.End} scenario {fn.GroundTruth.Scenario}");
        sb.AppendLine();
        sb.AppendLine("## Top False Positives (first 20)");
        foreach (var fp in r.FalsePositives.Take(20))
            sb.AppendLine($"- [{fp.RecordId}] {fp.Detection!.Type} \"{fp.Detection.Text}\" span {fp.Detection.Start}-{fp.Detection.End}");
        sb.AppendLine();
        sb.AppendLine("## Boundary Mismatches (first 20)");
        foreach (var bm in r.BoundaryMismatchDetails.Take(20))
            sb.AppendLine($"- [{bm.RecordId}] GT \"{bm.GroundTruth!.Text}\" {bm.GroundTruth.Start}-{bm.GroundTruth.End} vs Det \"{bm.Detection!.Text}\" {bm.Detection.Start}-{bm.Detection.End} IoU {bm.IoU:F2}");
        return sb.ToString();
    }

    public static void PrintConsoleSummary(BenchmarkResult r)
    {
        Console.WriteLine("=== Turkish PII Benchmark ===");
        Console.WriteLine($"Records: {r.TotalRecords} (malformed {r.MalformedRecords}, empty {r.EmptySpanRecords}) Entities: {r.TotalGroundTruthEntities} supported {r.SupportedEntities} unsupported {r.UnsupportedEntities}");
        Console.WriteLine($"Overall: TP {r.Overall.TP} FN {r.Overall.FN} FP {r.Overall.FP} Prec {r.Overall.Precision:F3} Rec {r.Overall.Recall:F3} F1 {r.Overall.F1:F3}");
        Console.WriteLine($"Exact {r.ExactMatches} Overlap {r.OverlapMatches} Boundary {r.BoundaryMismatches}");
        Console.WriteLine("Per Type (top 5 FN):");
        foreach (var t in r.PerType.OrderByDescending(x=>x.FN).Take(5))
            Console.WriteLine($"  {t.DatasetLabel} ({t.SafeCopyType}) support {t.Support} TP {t.TP} FN {t.FN} FP {t.FP} F1 {t.F1:F3}");
        Console.WriteLine("Per Scenario (worst F1):");
        foreach (var s in r.PerScenario.OrderBy(x=>x.F1).Take(5))
            Console.WriteLine($"  {s.Scenario} support {s.Support} F1 {s.F1:F3} Rec {s.Recall:F3}");
        Console.WriteLine($"Unsupported labels: {string.Join(", ", r.UnsupportedLabelCounts.Select(kv=> kv.Key+":"+kv.Value))}");
    }
}
