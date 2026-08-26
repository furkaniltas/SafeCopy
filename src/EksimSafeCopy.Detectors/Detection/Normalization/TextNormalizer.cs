namespace EksimSafeCopy.Detectors.Detection.Normalization;

using EksimSafeCopy.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

public static class TextNormalizer
{
    private static readonly Regex MultipleWhitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TurkishWhitespace = new(@"[\u00A0\u2000-\u200F\u2028\u2029\u202F\u205F\u3000]", RegexOptions.Compiled);

    public static NormalizedText Normalize(string text, NormalizationOptions? options = null)
    {
        options ??= new NormalizationOptions();
        
        var mapping = new List<NormalizationMapping>();
        var currentOffset = 0;
        var normalizedBuilder = new StringBuilder(text.Length);

        if (options.FixTurkishWhitespace)
        {
            text = TurkishWhitespace.Replace(text, " ");
        }

        if (options.NormalizeNewlines)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        if (options.CollapseWhitespace)
        {
            var result = MultipleWhitespace.Replace(text, match =>
            {
                var start = match.Index;
                var length = match.Length;
                mapping.Add(new NormalizationMapping
                {
                    OriginalStart = start,
                    OriginalLength = length,
                    NormalizedStart = currentOffset,
                    NormalizedLength = 1,
                    Type = NormalizationMappingType.WhitespaceCollapsed
                });
                currentOffset += 1;
                return " ";
            });
            text = result;
        }
        else
        {
            currentOffset = text.Length;
        }

        if (options.CollapseNewlines)
        {
            text = MultipleNewlines.Replace(text, "\n\n");
        }

        if (options.TrimEdges)
        {
            var trimStart = text.Length - text.TrimStart().Length;
            var trimEnd = text.Length - text.TrimEnd().Length;
            if (trimStart > 0 || trimEnd > 0)
            {
                text = text.Trim();
                mapping.Add(new NormalizationMapping
                {
                    OriginalStart = 0,
                    OriginalLength = trimStart,
                    NormalizedStart = 0,
                    NormalizedLength = 0,
                    Type = NormalizationMappingType.Trimmed
                });
                currentOffset = text.Length;
            }
        }

        return new NormalizedText
        {
            Text = text,
            Mappings = mapping,
            OriginalLength = text.Length
        };
    }

    public static NormalizedText NormalizeWithPositionTracking(string text, NormalizationOptions? options = null)
    {
        options ??= new NormalizationOptions();
        
        var mapping = new List<NormalizationMapping>();
        var normalizedBuilder = new StringBuilder();
        int originalIndex = 0;
        int normalizedIndex = 0;

        while (originalIndex < text.Length)
        {
            char c = text[originalIndex];

            if (options.FixTurkishWhitespace && IsTurkishWhitespace(c))
            {
                mapping.Add(new NormalizationMapping
                {
                    OriginalStart = originalIndex,
                    OriginalLength = 1,
                    NormalizedStart = normalizedIndex,
                    NormalizedLength = 1,
                    Type = NormalizationMappingType.TurkishWhitespaceFixed
                });
                normalizedBuilder.Append(' ');
                originalIndex++;
                normalizedIndex++;
                continue;
            }

            if (options.NormalizeNewlines && c == '\r')
            {
                if (originalIndex + 1 < text.Length && text[originalIndex + 1] == '\n')
                {
                    mapping.Add(new NormalizationMapping
                    {
                        OriginalStart = originalIndex,
                        OriginalLength = 2,
                        NormalizedStart = normalizedIndex,
                        NormalizedLength = 1,
                        Type = NormalizationMappingType.NewlineNormalized
                    });
                    normalizedBuilder.Append('\n');
                    originalIndex += 2;
                    normalizedIndex++;
                    continue;
                }
                else
                {
                    mapping.Add(new NormalizationMapping
                    {
                        OriginalStart = originalIndex,
                        OriginalLength = 1,
                        NormalizedStart = normalizedIndex,
                        NormalizedLength = 1,
                        Type = NormalizationMappingType.NewlineNormalized
                    });
                    normalizedBuilder.Append('\n');
                    originalIndex++;
                    normalizedIndex++;
                    continue;
                }
            }

            if (options.CollapseWhitespace && char.IsWhiteSpace(c))
            {
                int whitespaceStart = originalIndex;
                int whitespaceCount = 0;
                while (originalIndex < text.Length && char.IsWhiteSpace(text[originalIndex]))
                {
                    whitespaceCount++;
                    originalIndex++;
                }
                
                mapping.Add(new NormalizationMapping
                {
                    OriginalStart = whitespaceStart,
                    OriginalLength = whitespaceCount,
                    NormalizedStart = normalizedIndex,
                    NormalizedLength = 1,
                    Type = NormalizationMappingType.WhitespaceCollapsed
                });
                normalizedBuilder.Append(' ');
                normalizedIndex++;
                continue;
            }

            if (options.CollapseNewlines && c == '\n')
            {
                int newlineStart = originalIndex;
                int newlineCount = 0;
                while (originalIndex < text.Length && text[originalIndex] == '\n')
                {
                    newlineCount++;
                    originalIndex++;
                }
                
                if (newlineCount > 2)
                {
                    mapping.Add(new NormalizationMapping
                    {
                        OriginalStart = newlineStart,
                        OriginalLength = newlineCount,
                        NormalizedStart = normalizedIndex,
                        NormalizedLength = 2,
                        Type = NormalizationMappingType.NewlineCollapsed
                    });
                    normalizedBuilder.Append("\n\n");
                    normalizedIndex += 2;
                }
                else
                {
                    for (int i = 0; i < newlineCount; i++)
                    {
                        mapping.Add(new NormalizationMapping
                        {
                            OriginalStart = newlineStart + i,
                            OriginalLength = 1,
                            NormalizedStart = normalizedIndex + i,
                            NormalizedLength = 1,
                            Type = NormalizationMappingType.Preserved
                        });
                    }
                    normalizedBuilder.Append(new string('\n', newlineCount));
                    normalizedIndex += newlineCount;
                }
                continue;
            }

            mapping.Add(new NormalizationMapping
            {
                OriginalStart = originalIndex,
                OriginalLength = 1,
                NormalizedStart = normalizedIndex,
                NormalizedLength = 1,
                Type = NormalizationMappingType.Preserved
            });
            normalizedBuilder.Append(c);
            originalIndex++;
            normalizedIndex++;
        }

        return new NormalizedText
        {
            Text = normalizedBuilder.ToString(),
            Mappings = mapping,
            OriginalLength = text.Length
        };
    }

    private static bool IsTurkishWhitespace(char c)
    {
        return c is '\u00A0' or >= '\u2000' and <= '\u200F' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000';
    }

    public static int MapToOriginalPosition(this NormalizedText normalized, int normalizedPosition)
    {
        if (normalizedPosition < 0) return 0;
        if (normalizedPosition >= normalized.Text.Length) return normalized.OriginalLength;

        var mapping = normalized.Mappings
            .Where(m => m.NormalizedStart <= normalizedPosition && normalizedPosition < m.NormalizedStart + m.NormalizedLength)
            .OrderBy(m => m.NormalizedStart)
            .FirstOrDefault();

        if (mapping == null) return Math.Min(normalizedPosition, normalized.OriginalLength);

        if (mapping.Type == NormalizationMappingType.Preserved)
        {
            return mapping.OriginalStart + (normalizedPosition - mapping.NormalizedStart);
        }

        return mapping.OriginalStart;
    }

    public static int MapToNormalizedPosition(this NormalizedText normalized, int originalPosition)
    {
        if (originalPosition < 0) return 0;
        if (originalPosition >= normalized.OriginalLength) return normalized.Text.Length;

        var mapping = normalized.Mappings
            .Where(m => m.OriginalStart <= originalPosition && originalPosition < m.OriginalStart + m.OriginalLength)
            .OrderBy(m => m.OriginalStart)
            .FirstOrDefault();

        if (mapping == null) return Math.Min(originalPosition, normalized.Text.Length);

        if (mapping.Type == NormalizationMappingType.Preserved)
        {
            return mapping.NormalizedStart + (originalPosition - mapping.OriginalStart);
        }

        return mapping.NormalizedStart;
    }
}

public sealed class NormalizedText
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<NormalizationMapping> Mappings { get; init; } = Array.Empty<NormalizationMapping>();
    public int OriginalLength { get; init; }
}

public sealed class NormalizationMapping
{
    public int OriginalStart { get; init; }
    public int OriginalLength { get; init; }
    public int NormalizedStart { get; init; }
    public int NormalizedLength { get; init; }
    public NormalizationMappingType Type { get; init; }
}

public enum NormalizationMappingType
{
    Preserved,
    WhitespaceCollapsed,
    NewlineNormalized,
    NewlineCollapsed,
    Trimmed,
    TurkishWhitespaceFixed
}

public sealed class NormalizationOptions
{
    public bool NormalizeNewlines { get; init; } = true;
    public bool CollapseWhitespace { get; init; } = true;
    public bool CollapseNewlines { get; init; } = true;
    public bool FixTurkishWhitespace { get; init; } = true;
    public bool TrimEdges { get; init; } = true;
}