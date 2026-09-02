using System.Text;
using System.Text.Json;
using EksimSafeCopy.Benchmark.Models;

namespace EksimSafeCopy.Benchmark.Parser;

public sealed class BenchmarkParser
{
    public static List<BenchmarkRecord> ParseFile(string path, out List<BenchmarkRecord> malformed)
    {
        malformed = new List<BenchmarkRecord>();
        var records = new List<BenchmarkRecord>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var rec = ParseLine(line, i + 1, path);
            if (rec.IsMalformed) malformed.Add(rec);
            records.Add(rec);
        }
        return records;
    }

    public static BenchmarkRecord ParseLine(string jsonLine, int lineNumber, string sourcePath = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            // Text field - try multiple keys
            string text = GetStringField(root, new[] { "text", "content", "document", "sentence", "input" }) ?? string.Empty;

            // Id field
            string id = GetStringField(root, new[] { "id", "record_id", "doc_id" }) ?? $"line_{lineNumber}";

            // Scenario, difficulty, source
            string scenario = GetStringField(root, new[] { "scenario", "context", "type" }) ?? "unknown";
            string difficulty = GetStringField(root, new[] { "difficulty", "level" }) ?? "unknown";
            string source = GetStringField(root, new[] { "source", "info", "metadata" }) ?? string.Empty;
            if (root.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.Object)
                source = srcEl.GetRawText();

            // Spans / entities / label
            var entities = new List<GroundTruthEntity>();
            JsonElement spansEl = default;
            bool hasSpans = false;
            string[] spanKeys = new[] { "spans", "entities", "label", "labels", "annotations", "ground_truth" };
            foreach (var k in spanKeys)
            {
                if (root.TryGetProperty(k, out spansEl) && spansEl.ValueKind == JsonValueKind.Array)
                {
                    hasSpans = true;
                    break;
                }
            }

            if (hasSpans)
            {
                foreach (var el in spansEl.EnumerateArray())
                {
                    try
                    {
                        string label = GetStringField(el, new[] { "label", "category", "type", "entity", "tag" }) ?? "unknown";
                        int start = GetIntField(el, new[] { "start", "start_idx", "begin", "offset" });
                        int end = GetIntField(el, new[] { "end", "end_idx", "stop" });
                        // Handle case where spans have "start" and "end" but text is not directly provided, we can extract from text
                        string entityText = string.Empty;
                        if (el.TryGetProperty("text", out var txtEl) && txtEl.ValueKind == JsonValueKind.String)
                            entityText = txtEl.GetString() ?? string.Empty;
                        else if (!string.IsNullOrEmpty(text) && start >= 0 && end <= text.Length && end > start)
                            entityText = text.Substring(start, end - start);

                        var mapping = LabelMappingTable.Resolve(label);
                        entities.Add(new GroundTruthEntity
                        {
                            Label = label,
                            Start = start,
                            End = end,
                            Text = entityText,
                            Scenario = scenario,
                            Difficulty = difficulty,
                            MappedType = mapping.SafeCopyType,
                            MappingStatus = mapping.Status,
                            MappingReason = mapping.Reason
                        });
                    }
                    catch (Exception ex)
                    {
                        // Malformed entity, skip but record as malformed entity?
                        // For now, skip this entity and continue
                        // We could log but not fail entire record
                        entities.Add(new GroundTruthEntity
                        {
                            Label = "malformed",
                            Start = 0,
                            End = 0,
                            Text = $"parse_error: {ex.Message}",
                            Scenario = scenario,
                            Difficulty = difficulty,
                            MappedType = null,
                            MappingStatus = MappingStatus.UNSUPPORTED,
                            MappingReason = ex.Message
                        });
                    }
                }
            }

            return new BenchmarkRecord
            {
                Id = id,
                Text = text,
                Entities = entities,
                Scenario = scenario,
                Difficulty = difficulty,
                Source = source,
                RawJson = jsonLine,
                LineNumber = lineNumber,
                IsMalformed = false
            };
        }
        catch (Exception ex)
        {
            return new BenchmarkRecord
            {
                Id = $"malformed_{lineNumber}",
                Text = string.Empty,
                Entities = new List<GroundTruthEntity>(),
                Scenario = "unknown",
                Difficulty = "unknown",
                Source = sourcePath,
                RawJson = jsonLine,
                LineNumber = lineNumber,
                IsMalformed = true,
                ParseError = ex.Message
            };
        }
    }

    private static string? GetStringField(JsonElement el, string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            // Also try case-insensitive
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.NameEquals(k) && prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString();
            }
        }
        // Case-insensitive search
        foreach (var prop in el.EnumerateObject())
        {
            foreach (var k in keys)
            {
                if (string.Equals(prop.Name, k, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString();
            }
        }
        return null;
    }

    private static int GetIntField(JsonElement el, string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var iv))
                return iv;
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.NameEquals(k) && prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var iv2))
                    return iv2;
            }
        }
        foreach (var prop in el.EnumerateObject())
        {
            foreach (var k in keys)
            {
                if (string.Equals(prop.Name, k, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var iv3))
                    return iv3;
            }
        }
        return 0;
    }

    public static (Dictionary<string,int> labelCounts, Dictionary<string,int> scenarioCounts, Dictionary<string,int> difficultyCounts) GetStats(List<BenchmarkRecord> records)
    {
        var labelCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var scenarioCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var difficultyCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records.Where(r=>!r.IsMalformed))
        {
            scenarioCounts[r.Scenario] = scenarioCounts.GetValueOrDefault(r.Scenario) + 1;
            difficultyCounts[r.Difficulty] = difficultyCounts.GetValueOrDefault(r.Difficulty) + 1;
            foreach (var e in r.Entities)
            {
                labelCounts[e.Label] = labelCounts.GetValueOrDefault(e.Label) + 1;
            }
        }
        return (labelCounts, scenarioCounts, difficultyCounts);
    }
}
