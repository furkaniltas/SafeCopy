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

            // Handle info object for scenario/difficulty/id (new curated dataset)
            if (root.TryGetProperty("info", out var infoEl) && infoEl.ValueKind == JsonValueKind.Object)
            {
                if (infoEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    id = idEl.GetString() ?? id;
                if (infoEl.TryGetProperty("scenario", out var scEl) && scEl.ValueKind == JsonValueKind.String)
                    scenario = scEl.GetString() ?? scenario;
                if (infoEl.TryGetProperty("difficulty", out var diffEl) && diffEl.ValueKind == JsonValueKind.String)
                    difficulty = diffEl.GetString() ?? difficulty;
                if (infoEl.TryGetProperty("source", out var srcEl2))
                    source = srcEl2.ValueKind == JsonValueKind.String ? srcEl2.GetString() ?? source : srcEl2.GetRawText();
            }

            // Spans / entities / label - handle both array (old) and object (new curated: spans is dict label->[[start,end]])
            var entities = new List<GroundTruthEntity>();
            JsonElement spansEl = default;
            bool hasSpansArray = false;
            bool hasSpansObject = false;
            string[] spanKeys = new[] { "spans", "entities", "label", "labels", "annotations", "ground_truth" };
            foreach (var k in spanKeys)
            {
                if (root.TryGetProperty(k, out spansEl))
                {
                    if (spansEl.ValueKind == JsonValueKind.Array) { hasSpansArray = true; break; }
                    if (spansEl.ValueKind == JsonValueKind.Object) { hasSpansObject = true; break; }
                }
            }

            if (hasSpansArray)
            {
                foreach (var el in spansEl.EnumerateArray())
                {
                    try
                    {
                        string label = GetStringField(el, new[] { "label", "category", "type", "entity", "tag" }) ?? "unknown";
                        int start = GetIntField(el, new[] { "start", "start_idx", "begin", "offset" });
                        int end = GetIntField(el, new[] { "end", "end_idx", "stop" });
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
            else if (hasSpansObject)
            {
                // New curated: spans is object where key is "label:value" and value is list of [start,end]
                foreach (var prop in spansEl.EnumerateObject())
                {
                    string key = prop.Name;
                    string label = key.Contains(':') ? key.Substring(0, key.IndexOf(':')) : key;
                    // The value part after colon is the expected text, but we use span positions for ground truth
                    if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var spanEl in prop.Value.EnumerateArray())
                    {
                        try
                        {
                            int start = 0, end = 0;
                            if (spanEl.ValueKind == JsonValueKind.Array)
                            {
                                var arr = spanEl.EnumerateArray().ToList();
                                if (arr.Count >= 2)
                                {
                                    start = arr[0].GetInt32();
                                    end = arr[1].GetInt32();
                                }
                            }
                            else if (spanEl.ValueKind == JsonValueKind.Object)
                            {
                                start = GetIntField(spanEl, new[] { "start", "begin" });
                                end = GetIntField(spanEl, new[] { "end", "stop" });
                            }
                            string entityText = string.Empty;
                            if (!string.IsNullOrEmpty(text) && start >= 0 && end <= text.Length && end > start)
                                entityText = text.Substring(start, end - start);
                            else if (key.Contains(':'))
                                entityText = key.Substring(key.IndexOf(':') + 1);

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
