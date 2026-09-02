using EksimSafeCopy.Benchmark.Models;

namespace EksimSafeCopy.Benchmark.Metrics;

public static class MetricsCalculator
{
    public static BenchmarkMetrics ComputeOverall(List<EntityMatch> allMatches)
    {
        int tp = allMatches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.Overlap || m.Kind == MatchKind.BoundaryMismatch);
        int fn = allMatches.Count(m => m.Kind == MatchKind.Missed);
        int fp = allMatches.Count(m => m.Kind == MatchKind.FalsePositive);
        return new BenchmarkMetrics { TP = tp, FN = fn, FP = fp };
    }

    public static List<PerTypeMetrics> ComputePerType(List<BenchmarkRecord> records, List<EntityMatch> allMatches)
    {
        var result = new List<PerTypeMetrics>();
        // Group by mapped type or dataset label
        var allLabels = records.SelectMany(r => r.Entities).GroupBy(e => e.Label).Select(g => g.Key).Distinct().ToList();
        // Also include detection types that appeared as FP
        var fpTypes = allMatches.Where(m => m.Kind == MatchKind.FalsePositive && m.Detection != null).Select(m => m.Detection!.Type).Distinct().ToList();

        var typeGroups = new Dictionary<string, List<EntityMatch>>();

        // Group matches by ground truth label for supported
        foreach (var m in allMatches)
        {
            string key;
            if (m.GroundTruth != null)
                key = m.GroundTruth.Label;
            else if (m.Detection != null)
                key = m.Detection.Type.ToString();
            else
                key = "unknown";

            if (!typeGroups.ContainsKey(key)) typeGroups[key] = new List<EntityMatch>();
            typeGroups[key].Add(m);
        }

        foreach (var kv in typeGroups)
        {
            string label = kv.Key;
            var matches = kv.Value;
            int tp = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.Overlap || m.Kind == MatchKind.BoundaryMismatch);
            int fn = matches.Count(m => m.Kind == MatchKind.Missed);
            int fp = matches.Count(m => m.Kind == MatchKind.FalsePositive);
            int boundary = matches.Count(m => m.Kind == MatchKind.BoundaryMismatch);
            // Find mapping for this label
            var mapping = LabelMappingTable.Resolve(label);
            int support = tp + fn;
            double precision = tp + fp == 0 ? (tp == 0 ? 1.0 : 0) : (double)tp / (tp + fp);
            double recall = tp + fn == 0 ? 1.0 : (double)tp / (tp + fn);
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

        return result.OrderByDescending(r => r.Support).ToList();
    }

    public static List<PerScenarioMetrics> ComputePerScenario(List<BenchmarkRecord> records, List<EntityMatch> allMatches)
    {
        var scenarioGroups = allMatches.GroupBy(m => m.GroundTruth?.Scenario ?? m.Detection?.Type.ToString() ?? "unknown");
        // Better: group by record scenario
        var byRecordScenario = new Dictionary<string, List<EntityMatch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMatches)
        {
            string scenario = "unknown";
            if (m.GroundTruth != null && !string.IsNullOrWhiteSpace(m.GroundTruth.Scenario))
                scenario = m.GroundTruth.Scenario;
            else
            {
                // Find record for this match
                var rec = records.FirstOrDefault(r => r.Id == m.RecordId);
                if (rec != null) scenario = rec.Scenario;
            }
            if (!byRecordScenario.ContainsKey(scenario)) byRecordScenario[scenario] = new List<EntityMatch>();
            byRecordScenario[scenario].Add(m);
        }

        var result = new List<PerScenarioMetrics>();
        foreach (var kv in byRecordScenario)
        {
            var matches = kv.Value;
            int tp = matches.Count(m => m.Kind == MatchKind.Exact || m.Kind == MatchKind.Overlap || m.Kind == MatchKind.BoundaryMismatch);
            int fn = matches.Count(m => m.Kind == MatchKind.Missed);
            int fp = matches.Count(m => m.Kind == MatchKind.FalsePositive);
            int support = tp + fn;
            double precision = tp + fp == 0 ? (tp == 0 ? 1.0 : 0) : (double)tp / (tp + fp);
            double recall = tp + fn == 0 ? 1.0 : (double)tp / (tp + fn);
            double f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            result.Add(new PerScenarioMetrics
            {
                Scenario = kv.Key,
                Support = support,
                TP = tp,
                FN = fn,
                FP = fp,
                Precision = precision,
                Recall = recall,
                F1 = f1
            });
        }
        return result.OrderByDescending(r => r.Support).ToList();
    }
}
