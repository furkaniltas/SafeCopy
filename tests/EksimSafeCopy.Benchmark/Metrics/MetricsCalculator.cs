using EksimSafeCopy.Benchmark.Models;
using EksimSafeCopy.Core.Models;

namespace EksimSafeCopy.Benchmark.Metrics;

public static class MetricsCalculator
{
    public static BenchmarkMetrics ComputeOverall(List<EntityMatch> allMatches, bool strict = false)
    {
        int tp = strict
            ? allMatches.Count(m => m.Kind == MatchKind.Exact)
            : allMatches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.BoundaryMismatch);
        int fn = allMatches.Count(m => m.Kind == MatchKind.Missed) + (strict ? allMatches.Count(m => m.Kind == MatchKind.BoundaryMismatch) : 0);
        // For strict, boundary mismatches are FN, not TP
        if (strict)
        {
            // Boundary mismatches become FN for strict, and the detection part becomes FP? Actually for strict, the detection that was boundary mismatch is not exact, so the GT is FN and detection is FP
            // But our current matching already has 1-1, so a boundary mismatch is a TP in overlap view, but for strict we need to split it into 1 FN + 1 FP
            // Simpler: strict TP = exact only, FN = missed + boundary, FP = falsePositive + boundary
            fn = allMatches.Count(m => m.Kind == MatchKind.Missed) + allMatches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            int fpStrict = allMatches.Count(m => m.Kind == MatchKind.FalsePositive) + allMatches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            return new BenchmarkMetrics { TP = tp, FN = fn, FP = fpStrict };
        }
        int fp = allMatches.Count(m => m.Kind == MatchKind.FalsePositive);
        return new BenchmarkMetrics { TP = tp, FN = fn, FP = fp };
    }

    public static List<PerTypeMetrics> ComputePerType(List<BenchmarkRecord> records, List<EntityMatch> allMatches, bool strict = false)
    {
        // Group by SafeCopyType for supported, fallback to dataset label for unsupported
        var groups = new Dictionary<string, List<EntityMatch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMatches)
        {
            string key;
            if (m.GroundTruth != null)
            {
                // Use mapped type if available, else dataset label
                if (m.GroundTruth.MappedType != null)
                    key = m.GroundTruth.MappedType.ToString()!;
                else
                    key = m.GroundTruth.Label;
            }
            else if (m.Detection != null)
            {
                // For FP, group by detection type
                key = m.Detection.Type.ToString();
            }
            else
                key = "unknown";

            if (!groups.ContainsKey(key)) groups[key] = new List<EntityMatch>();
            groups[key].Add(m);
        }

        var result = new List<PerTypeMetrics>();
        foreach (var kv in groups)
        {
            var matches = kv.Value;
            // Determine representative label and mapping
            string datasetLabel = kv.Key;
            // Try to find a ground truth example to get original label and mapping
            var exampleGt = matches.FirstOrDefault(m => m.GroundTruth != null)?.GroundTruth;
            string label = exampleGt?.Label ?? kv.Key;
            var mapping = LabelMappingTable.Resolve(label);
            // If key is a DetectionType string, try to resolve that too
            if (exampleGt == null && Enum.TryParse<DetectionType>(kv.Key, out var dt))
            {
                mapping = new LabelMapping { DatasetLabel = kv.Key, SafeCopyType = dt, Status = MappingStatus.DIRECT, Reason = "Detection type FP" };
                label = kv.Key;
            }

            int tp, fn, fp, boundary;
            if (strict)
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact);
                boundary = matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
                fn = matches.Count(m => m.Kind == MatchKind.Missed) + boundary;
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive) + boundary;
            }
            else
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.BoundaryMismatch);
                fn = matches.Count(m => m.Kind == MatchKind.Missed);
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive);
                boundary = matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            }
            int support = tp + fn;
            // For strict, support is still tp+fn where tp is exact only, but fn includes boundary
            if (strict) support = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.Missed || m.Kind == MatchKind.BoundaryMismatch);
            double precision = tp + fp == 0 ? (tp == 0 ? 1.0 : 0) : (double)tp / (tp + fp);
            double recall = support == 0 ? 1.0 : (double)tp / support;
            double f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            result.Add(new PerTypeMetrics
            {
                DatasetLabel = label,
                SafeCopyType = mapping.SafeCopyType,
                MappingStatus = mapping.Status,
                Support = support,
                TP = tp,
                FN = fn,
                FP = fp,
                Precision = precision,
                Recall = recall,
                F1 = f1,
                BoundaryMismatches = boundary
            });
        }
        return result.OrderByDescending(r => r.Support).ThenBy(r => r.DatasetLabel).ToList();
    }

    public static List<PerScenarioMetrics> ComputePerScenario(List<BenchmarkRecord> records, List<EntityMatch> allMatches, bool strict = false)
    {
        var byScenario = new Dictionary<string, List<EntityMatch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMatches)
        {
            string scenario = "unknown";
            if (m.GroundTruth != null && !string.IsNullOrWhiteSpace(m.GroundTruth.Scenario))
                scenario = m.GroundTruth.Scenario;
            else
            {
                var rec = records.FirstOrDefault(r => r.Id == m.RecordId);
                if (rec != null) scenario = rec.Scenario;
            }
            if (!byScenario.ContainsKey(scenario)) byScenario[scenario] = new List<EntityMatch>();
            byScenario[scenario].Add(m);
        }
        var result = new List<PerScenarioMetrics>();
        foreach (var kv in byScenario)
        {
            var matches = kv.Value;
            int tp, fn, fp;
            if (strict)
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact);
                fn = matches.Count(m => m.Kind == MatchKind.Missed) + matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive) + matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            }
            else
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.BoundaryMismatch);
                fn = matches.Count(m => m.Kind == MatchKind.Missed);
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive);
            }
            int support = tp + fn;
            double precision = tp + fp == 0 ? (tp == 0 ? 1.0 : 0) : (double)tp / (tp + fp);
            double recall = support == 0 ? 1.0 : (double)tp / support;
            double f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            result.Add(new PerScenarioMetrics { Scenario = kv.Key, Support = support, TP = tp, FN = fn, FP = fp, Precision = precision, Recall = recall, F1 = f1 });
        }
        return result.OrderByDescending(r => r.Support).ToList();
    }

    public static List<PerScenarioMetrics> ComputePerDifficulty(List<BenchmarkRecord> records, List<EntityMatch> allMatches, bool strict = false)
    {
        var byDiff = new Dictionary<string, List<EntityMatch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMatches)
        {
            string diff = "unknown";
            if (m.GroundTruth != null && !string.IsNullOrWhiteSpace(m.GroundTruth.Difficulty))
                diff = m.GroundTruth.Difficulty;
            else
            {
                var rec = records.FirstOrDefault(r => r.Id == m.RecordId);
                if (rec != null) diff = rec.Difficulty;
            }
            if (!byDiff.ContainsKey(diff)) byDiff[diff] = new List<EntityMatch>();
            byDiff[diff].Add(m);
        }
        var result = new List<PerScenarioMetrics>();
        foreach (var kv in byDiff)
        {
            var matches = kv.Value;
            int tp, fn, fp;
            if (strict)
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact);
                fn = matches.Count(m => m.Kind == MatchKind.Missed) + matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive) + matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            }
            else
            {
                tp = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.BoundaryMismatch);
                fn = matches.Count(m => m.Kind == MatchKind.Missed);
                fp = matches.Count(m => m.Kind == MatchKind.FalsePositive);
            }
            int support = tp + fn;
            double precision = tp + fp == 0 ? (tp == 0 ? 1.0 : 0) : (double)tp / (tp + fp);
            double recall = support == 0 ? 1.0 : (double)tp / support;
            double f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            result.Add(new PerScenarioMetrics { Scenario = kv.Key, Support = support, TP = tp, FN = fn, FP = fp, Precision = precision, Recall = recall, F1 = f1 });
        }
        return result.OrderByDescending(r => r.Support).ToList();
    }
}
