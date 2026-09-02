namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Context;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class BirthDateDetector : BaseDetector, IBirthDateDetector
{
    public override DetectionType Type => DetectionType.Date;
    public override string Name => "Birth Date Detector";
    public override string Description => "Detects birth dates in Turkish document formats";

    private static readonly Regex DotDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\.(?:0[1-9]|1[0-2])\.(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex SlashDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\/(?:0[1-9]|1[0-2])\/(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex DashDatePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])\-(?:0[1-9]|1[0-2])\-(?:19[0-9]{2}|20[0-2]\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex IsoDatePattern = new(
        @"\b(?:19[0-9]{2}|20[0-2]\d)\-(?:0[1-9]|1[0-2])\-(?:0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    private static readonly Regex IsoDateTimePattern = new(
        @"\b(?:19[0-9]{2}|20[0-2]\d)\-(?:0[1-9]|1[0-2])\-(?:0[1-9]|[12]\d|3[01])T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d+)?(?:Z|[+-](?:0\d|1\d|2[0-3]):[0-5]\d)?\b",
        RegexOptions.Compiled);

    private static readonly Regex TextualDatePattern = new(
        @"\b(?:0?[1-9]|[12]\d|3[01])\s+(?:Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık|Oca|Şub|Mar|Nis|May|Haz|Tem|Ağu|Eyl|Eki|Kas|Ara)\s+(?:19[0-9]{2}|20[0-2]\d)(?:\s+saat\s+(?:[01]?\d|2[0-3])[:\.][0-5]\d(?::[0-5]\d)?)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumericWithTimePattern = new(
        @"\b(?:0[1-9]|[12]\d|3[01])[\.\-\/](?:0[1-9]|1[0-2])[\.\-\/](?:19[0-9]{2}|20[0-2]\d)\s+(?:[01]?\d|2[0-3])[:\.][0-5]\d(?::[0-5]\d)?\b",
        RegexOptions.Compiled);

    private static readonly Regex YmdWithTimePattern = new(
        @"\b(?:19[0-9]{2}|20[0-2]\d)[\/\-\.](?:0[1-9]|1[0-2])[\/\-\.](?:0[1-9]|[12]\d|3[01])\s+saat\s+(?:[01]?\d|2[0-3])[\.:\-][0-5]\d(?:[\.:\-][0-5]\d)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        var text = normalizedText.Text;

        var strictPatterns = new[]
        {
            (DotDatePattern, "dot", true),
            (SlashDatePattern, "slash", true),
            (DashDatePattern, "dash", true),
            (IsoDatePattern, "iso", true)
        };
        var freePatterns = new[]
        {
            (IsoDateTimePattern, "iso_datetime", false),
            (TextualDatePattern, "textual", false),
            (NumericWithTimePattern, "numeric_time", false),
            (YmdWithTimePattern, "ymd_time", false)
        };

        foreach (var (pattern, format, requireContext) in strictPatterns.Concat(freePatterns))
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var candidate = match.Value;
                if (!IsValidDate(candidate, format)) continue;

                var contextWindow = GetContextWindow(normalizedText, match.Index, match.Length);
                var contextFeatures = AnalyzeContext(contextWindow);
                
                if (requireContext && !contextFeatures.HasStrongLabel && !HasBirthDateContext(contextWindow))
                    continue;

                double confidence = CalculateConfidence(candidate, contextFeatures);
                
                var originalStart = normalizedText.MapToOriginalPosition(match.Index);
                var originalEnd = normalizedText.MapToOriginalPosition(match.Index + match.Length);
                
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
                        ["format"] = format,
                        ["has_birth_label"] = contextFeatures.HasStrongLabel,
                        ["label_score"] = contextFeatures.LabelScore
                    });

                detections.Add(detection);
            }
        }

        return detections;
    }

    private static bool IsValidDate(string dateStr, string format)
    {
        try
        {
            DateTime date;
            if (format == "iso")
            {
                date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (format == "iso_datetime")
            {
                // Extract date part before T
                var datePart = dateStr.Split('T')[0];
                date = DateTime.ParseExact(datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (format == "textual")
            {
                // Format: d MMMM yyyy [saat HH:mm] - e.g., "7 Şubat 2028" or "5 Nisan 2026 saat 06:52"
                var lower = dateStr.ToLowerInvariant();
                // Remove "saat ..." suffix for validation
                var dateOnly = lower.Split(new[] { " saat " }, StringSplitOptions.None)[0];
                var parts = dateOnly.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return false;
                int day = int.Parse(parts[0]);
                string monthName = parts[1].ToLowerInvariant();
                int year = int.Parse(parts[2]);
                int month = TurkishMonthToNumber(monthName);
                if (month == 0) return false;
                date = new DateTime(year, month, day);
            }
            else if (format == "numeric_time" || format == "ymd_time")
            {
                // Extract date part before space/saat
                string datePart;
                if (dateStr.Contains(" saat "))
                    datePart = dateStr.Split(new[] { " saat " }, StringSplitOptions.None)[0];
                else
                    datePart = dateStr.Split(' ')[0];
                // datePart is like "12.07.2029" or "2026/02/02"
                var parts = datePart.Split('.', '/', '-');
                if (parts.Length != 3) return false;
                // For ymd_time, parts are yyyy/mm/dd, for numeric_time it's dd/mm/yyyy
                if (format == "ymd_time")
                {
                    int year = int.Parse(parts[0]);
                    int month = int.Parse(parts[1]);
                    int day = int.Parse(parts[2]);
                    date = new DateTime(year, month, day);
                }
                else
                {
                    int day = int.Parse(parts[0]);
                    int month = int.Parse(parts[1]);
                    int year = int.Parse(parts[2]);
                    date = new DateTime(year, month, day);
                }
            }
            else
            {
                var parts = dateStr.Split('.', '/', '-');
                if (parts.Length != 3) return false;
                
                int day = int.Parse(parts[0]);
                int month = int.Parse(parts[1]);
                int year = int.Parse(parts[2]);
                
                date = new DateTime(year, month, day);
            }

            // For birth-specific strict patterns, reject future dates (birth cannot be future)
            // For general date patterns with time/textual, allow future (delivery, appointment)
            bool isBirthStrict = format == "dot" || format == "slash" || format == "dash" || format == "iso";
            if (isBirthStrict && date > DateTime.Now) return false;
            if (date.Year < 1900) return false;
            if (date.Year > 2100) return false;
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int TurkishMonthToNumber(string monthName)
    {
        return monthName.ToLowerInvariant() switch
        {
            "ocak" or "oca" => 1,
            "şubat" or "subat" or "şub" or "sub" => 2,
            "mart" or "mar" => 3,
            "nisan" or "nis" => 4,
            "mayıs" or "mayis" or "may" => 5,
            "haziran" or "haz" => 6,
            "temmuz" or "tem" => 7,
            "ağustos" or "agustos" or "ağu" or "agu" => 8,
            "eylül" or "eylul" or "eyl" => 9,
            "ekim" or "eki" => 10,
            "kasım" or "kasim" or "kas" => 11,
            "aralık" or "aralik" or "ara" => 12,
            _ => 0
        };
    }

    private static bool HasBirthDateContext(ContextWindow window)
    {
        var beforeContext = window.GetBeforeContext(30).ToLowerInvariant();
        var afterContext = window.GetAfterContext(30).ToLowerInvariant();
        
        var birthKeywords = new[] { 
            "doğum", "dogum", "d.tarihi", "d.t", "d.tarih", "dtarih",
            "anne", "baba", "babaanne", "anneanne", "büyükbaba", "büyükanne", "buyukbaba", "buyukanne"
        };
        
        return birthKeywords.Any(k => beforeContext.Contains(k) || afterContext.Contains(k));
    }

    private double CalculateConfidence(string date, ContextFeatures context)
    {
        double confidence = 0.6;

        if (context.HasStrongLabel)
            confidence = Math.Min(1.0, confidence + 0.3);

        if (context.HasNegativeLabel)
            confidence = Math.Max(0.1, confidence - 0.4);

        return confidence;
    }
}