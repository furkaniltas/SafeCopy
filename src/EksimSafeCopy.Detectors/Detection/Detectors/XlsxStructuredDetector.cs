namespace EksimSafeCopy.Detectors.Detection.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Normalization;
using System.Text.RegularExpressions;

public sealed class XlsxStructuredDetector : BaseDetector, IXlsxStructuredDetector
{
    public override DetectionType Type => DetectionType.Custom;
    public override string Name => "XLSX Structured Field Detector";
    public override string Description => "Detects PII in XLSX via column header semantics";

    // Header normalization: lower, trim, remove non-alphanum, handle Turkish chars
    private static string NormalizeHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        var lower = header.ToLowerInvariant().Trim();
        // Handle underscore and apikey variations before punctuation removal
        lower = lower.Replace("_", " ");
        // apikey -> api key (without space)
        lower = Regex.Replace(lower, @"\bapikey\b", "api key", RegexOptions.IgnoreCase);
        // Remove punctuation and extra spaces
        lower = Regex.Replace(lower, @"[\(\)\[\]\.\,\;\:\!\?\/\\]+", " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        // Remove common suffixes like "(sahte)" etc.
        lower = Regex.Replace(lower, @"\s*\(.*?\)\s*", " ").Trim();
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        return lower;
    }

    private static readonly Dictionary<string, DetectionType> HeaderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ad soyad"] = DetectionType.FullName,
        ["ad"] = DetectionType.FirstName,
        ["soyad"] = DetectionType.LastName,
        ["tc kimlik no"] = DetectionType.TcKimlikNo,
        ["tc kimlik"] = DetectionType.TcKimlikNo,
        ["tckn"] = DetectionType.TcKimlikNo,
        ["dogum tarihi"] = DetectionType.Date,
        ["doğum tarihi"] = DetectionType.Date,
        ["anne adi"] = DetectionType.MotherName,
        ["anne adı"] = DetectionType.MotherName,
        ["baba adi"] = DetectionType.FatherName,
        ["baba adı"] = DetectionType.FatherName,
        ["telefon"] = DetectionType.Phone,
        ["tel"] = DetectionType.Phone,
        ["e posta"] = DetectionType.Email,
        ["e-posta"] = DetectionType.Email,
        ["eposta"] = DetectionType.Email,
        ["email"] = DetectionType.Email,
        ["kullanici adi"] = DetectionType.Username,
        ["kullanıcı adı"] = DetectionType.Username,
        ["sifre"] = DetectionType.Secret,
        ["şifre"] = DetectionType.Secret,
        ["sifre sahte ornek"] = DetectionType.Secret,
        ["şifre sahte örnek"] = DetectionType.Secret,
        ["parola"] = DetectionType.Secret,
        ["gizli anahtar"] = DetectionType.Secret,
        ["gizli"] = DetectionType.Secret,
        ["api anahtarı"] = DetectionType.Secret,
        ["api anahtari"] = DetectionType.Secret,
        ["erisim anahtari"] = DetectionType.Secret,
        ["erişim anahtarı"] = DetectionType.Secret,
        ["token"] = DetectionType.Secret,
        ["client secret"] = DetectionType.Secret,
        ["password"] = DetectionType.Secret,
        ["passwd"] = DetectionType.Secret,
        ["pwd"] = DetectionType.Secret,
        ["pass"] = DetectionType.Secret,
        ["secret"] = DetectionType.Secret,
        ["secret key"] = DetectionType.Secret,
        ["api key"] = DetectionType.Secret,
        ["access token"] = DetectionType.Secret,
        ["refresh token"] = DetectionType.Secret,
        ["key"] = DetectionType.Secret,
        ["api"] = DetectionType.Secret,
        ["adres"] = DetectionType.Address,
        ["iban"] = DetectionType.Iban,
        ["kredi karti no"] = DetectionType.CreditCard,
        ["kredi kartı no"] = DetectionType.CreditCard,
        ["kart son kullanma"] = DetectionType.CardExpiry,
        ["cvv"] = DetectionType.Cvv,
        ["kan grubu"] = DetectionType.BloodType,
        ["meslek"] = DetectionType.FullName, // not sensitive per task - will be ignored
        ["sirket"] = DetectionType.FullName,
        ["şirket"] = DetectionType.FullName,
        ["arac plakasi"] = DetectionType.LicensePlate,
        ["araç plakası"] = DetectionType.LicensePlate,
        ["ip adresi"] = DetectionType.IpAddress,
        ["mac adresi"] = DetectionType.MacAddress,
        ["vergi no"] = DetectionType.TaxId,
        ["vergi no sahte"] = DetectionType.TaxId,
        ["sgk no"] = DetectionType.SgkNo,
        ["sgk no sahte"] = DetectionType.SgkNo,
        // Common variations
        ["kullanici adi"] = DetectionType.Username,
        ["kullanıcı adı"] = DetectionType.Username,
    };

    // Headers that should NOT be auto-masked (ambiguous per task)
    private static readonly HashSet<string> ExcludedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "meslek",
        "sirket",
        "şirket",
        "serbest metin notu",
        "serbest metin",
        "referans",
        "id",
        "cinsiyet",
        "il",
        "posta kodu",
        "sehir",
        "şehir",
        // Secret false-positive headers - must not be treated as Secret
        "primary key",
        "foreign key",
        "record key",
        "key id",
        "key management",
        "key information",
        "api documentation",
        "api description",
        "api endpoint",
        "token validation",
        "password policy",
        "password documentation",
        "secret management"
    };

    protected override IReadOnlyList<Detection> DetectOnPage(DocumentPage page, NormalizedText normalizedText, CancellationToken cancellationToken)
    {
        var detections = new List<Detection>();
        // Only for XLSX
        // We need to use TextBlocks to get column semantics, not just normalized text
        if (page.TextBlocks == null || page.TextBlocks.Count == 0) return detections;

        // Find header row: cells where CellReference is exactly row 1 (e.g., A1, B1, not A11)
        var headerBlocks = page.TextBlocks.Where(b =>
        {
            if (b.Properties.TryGetValue("CellReference", out var cr) && cr is string s)
                return System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z]+1$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return b.Text.StartsWith("A1:") || b.Text.Contains(": Ad") || b.Text.Contains(": Soyad");
        }).ToList();
        if (headerBlocks.Count == 0)
        {
            // Fallback: try OrderIndex 0
            headerBlocks = page.TextBlocks.Where(b => b.OrderIndex == 0).ToList();
            if (headerBlocks.Count == 0) return detections;
        }

        // Build column -> header mapping
        var columnToHeader = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var columnToDetectionType = new Dictionary<string, DetectionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in headerBlocks)
        {
            // block.Text is like "B1: Ad" — extract after colon
            var text = block.Text;
            var colonIdx = text.IndexOf(':');
            string header = colonIdx >= 0 ? text.Substring(colonIdx + 1).Trim() : text.Trim();
            // Also try to get CellReference from properties
            string cellRef = "";
            if (block.Properties.TryGetValue("CellReference", out var cr) && cr is string s) cellRef = s;
            else
            {
                // Fallback: extract from "A1: Ad" prefix
                var m = Regex.Match(text, @"^([A-Z]+[0-9]+):");
                if (m.Success) cellRef = m.Groups[1].Value;
            }
            // Extract column letters (e.g., B1 -> B)
            var colMatch = Regex.Match(cellRef, @"^([A-Z]+)");
            string col = colMatch.Success ? colMatch.Groups[1].Value : block.OrderIndex.ToString();
            var normHeader = NormalizeHeader(header);
            if (ExcludedHeaders.Contains(normHeader)) continue;
            if (HeaderMap.TryGetValue(normHeader, out var detType))
            {
                columnToHeader[col] = header;
                columnToDetectionType[col] = detType;
            }
            else
            {
                // Try partial match for known headers - but for short sensitive headers "key"/"api" use exact only
                foreach (var kv in HeaderMap)
                {
                    // For short headers "key" and "api", require exact match to avoid false positives like "primary key" -> "key"
                    if ((kv.Key == "key" || kv.Key == "api") && normHeader != kv.Key) continue;
                    if (normHeader.Contains(kv.Key) || kv.Key.Contains(normHeader))
                    {
                        // Only for exact or close, not for "kullanıcı" vs "kullanıcı adı" strict
                        if (kv.Key == "kullanici adi" && normHeader == "kullanici") continue;
                        columnToHeader[col] = header;
                        columnToDetectionType[col] = kv.Value;
                        break;
                    }
                }
            }
        }

        if (columnToDetectionType.Count == 0) return detections;

        // For each subsequent row's blocks, create detections (exclude header row)
        var dataBlocks = page.TextBlocks.Where(b =>
        {
            if (b.Properties.TryGetValue("CellReference", out var cr) && cr is string s)
                return !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z]+1$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return b.OrderIndex > 0;
        }).ToList();
        // Build page text offset map for correct TextSpan positions
        var pageText = page.Text;
        var blockOffsets = new System.Collections.Generic.Dictionary<TextBlock, int>();
        int offset = 0;
        foreach(var b in page.TextBlocks.OrderBy(b=>b.OrderIndex)){
            blockOffsets[b] = offset;
            offset += b.Text.Length + 1; // +1 for "\n" separator (as in XlsxDocumentIngestor: string.Join("\n", allText))
        }
        foreach (var block in dataBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cellRef = "";
            if (block.Properties.TryGetValue("CellReference", out var cr) && cr is string s) cellRef = s;
            else
            {
                var m = Regex.Match(block.Text, @"^([A-Z]+[0-9]+):");
                if (m.Success) cellRef = m.Groups[1].Value;
            }
            var colMatch = Regex.Match(cellRef, @"^([A-Z]+)");
            string col = colMatch.Success ? colMatch.Groups[1].Value : "";
            if (string.IsNullOrEmpty(col) || !columnToDetectionType.TryGetValue(col, out var detType)) continue;

            // Extract cell value (after colon)
            var fullText = block.Text;
            var colonIdx2 = fullText.IndexOf(':');
            string cellValue = colonIdx2 >= 0 ? fullText.Substring(colonIdx2 + 1).Trim() : fullText.Trim();
            if (string.IsNullOrWhiteSpace(cellValue)) continue;

            // Skip header-like values (e.g., "Ad" in data row which is actually header repeated? No)
            // Validate per type where needed (e.g., IP, MAC, CreditCard format)
            if (!IsValidForType(cellValue, detType)) continue;

            // Create TextSpan: find cellValue's position in page.Text
            // page.Text is joined with "\n" of all blocks' Text
            // We need to find the cellValue's start in normalizedText or page.Text
            // Use normalizedText for position, but also keep original
            var text = normalizedText.Text;
            // Find cellValue in the block's textSpan
            var blockSpan = block.Spans.FirstOrDefault(sp => !(sp.Properties.TryGetValue("IsAddress", out var isAddr) && isAddr is true));
            string spanText = cellValue;
            TextSpan? textSpan = null;
            if (blockSpan != null)
            {
                // The value span is the second span in the block (after "A2: ")
                var valueSpan = block.Spans.FirstOrDefault(sp => !(sp.Properties.TryGetValue("IsAddress", out var ia) && ia is true));
                if (valueSpan != null && blockOffsets.TryGetValue(block, out var blockOffset))
                {
                    int pageStart = blockOffset + valueSpan.StartIndex;
                    // Map to original via normalizedText if needed, but for XLSX the page.Text is already the source for detection
                    textSpan = new TextSpan
                    {
                        StartIndex = pageStart,
                        Length = valueSpan.Length,
                        Text = cellValue,
                        BoundingBox = BoundingBox.Empty
                    };
                }
            }
            // Fallback: search in page.Text
            if (textSpan == null)
            {
                int idx = text.IndexOf(cellValue, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var origStart = normalizedText.MapToOriginalPosition(idx);
                    var origEnd = normalizedText.MapToOriginalPosition(idx + cellValue.Length);
                    textSpan = new TextSpan { StartIndex = origStart, Length = origEnd - origStart, Text = cellValue, BoundingBox = BoundingBox.Empty };
                }
                else
                {
                    // Create a synthetic span
                    textSpan = new TextSpan { StartIndex = 0, Length = cellValue.Length, Text = cellValue, BoundingBox = BoundingBox.Empty };
                }
            }

            // Special handling for IP that was previously misclassified as Phone: ensure correct type
            // Already detType is correct per header, so we override

            var ctx = $"Column {col} ({columnToHeader[col]}) Cell {cellRef}";
            var detection = CreateDetection(
                value: cellValue,
                confidence: 0.95,
                pageNumber: page.PageNumber,
                textSpan: textSpan,
                context: ctx,
                properties: new Dictionary<string, object>
                {
                    ["sheet"] = page.PageNumber,
                    ["row"] = cellRef,
                    ["column"] = col,
                    ["cellReference"] = cellRef,
                    ["columnHeader"] = columnToHeader[col],
                    ["detectionSource"] = "Structured",
                    ["originalHeader"] = columnToHeader[col]
                });
            // Override Type to the structured one
            detection = new Detection
            {
                Id = detection.Id,
                Type = detType,
                Value = cellValue,
                Context = ctx,
                Confidence = 0.95,
                ConfidenceLevel = ConfidenceLevel.High,
                PageNumber = page.PageNumber,
                TextSpan = textSpan,
                Location = BoundingBox.Empty,
                DetectionSource = DetectionSource.HiddenContent,
                Properties = detection.Properties,
                State = DetectionState.Detected
            };
            detections.Add(detection);
        }

        return detections;
    }

    private static bool IsValidForType(string value, DetectionType type)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Placeholders like [FIRST_NAME], [PHONE], [REDACTED] should not be considered as valid PII
        if (value.StartsWith("[") && value.EndsWith("]")) return false;
        if (value.Contains("[") && value.Contains("]")) return false;
        return type switch
        {
            DetectionType.IpAddress => Regex.IsMatch(value, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"),
            DetectionType.MacAddress => Regex.IsMatch(value, @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"),
            DetectionType.CreditCard => Regex.IsMatch(value.Replace(" ", "").Replace("-", ""), @"^\d{13,19}$"),
            DetectionType.Cvv => Regex.IsMatch(value, @"^\d{3,4}$"),
            DetectionType.CardExpiry => Regex.IsMatch(value, @"^\d{2}[\/\-\.]\d{2,4}$") || Regex.IsMatch(value, @"^\d{2}/\d{2}$"),
            DetectionType.BloodType => Regex.IsMatch(value, @"^(A|B|AB|O)\s*Rh[\+\-]$", RegexOptions.IgnoreCase),
            DetectionType.LicensePlate => Regex.IsMatch(value, @"^\d{2}\s*[A-Z]{1,3}\s*\d{2,4}$", RegexOptions.IgnoreCase) || value.Contains("U") || value.Length >= 5,
            // For others, accept non-empty
            _ => true
        };
    }
}
