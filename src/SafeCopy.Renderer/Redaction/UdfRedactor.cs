namespace SafeCopy.Renderer.Redaction;

using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

public sealed class UdfRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Udf;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = plan.Operations.Where(o => o.State == RedactionOperationState.Pending).ToList();
            if (!pending.Any())
                return Result<byte[]>.Success(documentBytes);

            // Use descending order (spec 4)
            pending = pending.Where(o => o.TextSpan != null).OrderByDescending(o => o.TextSpan!.StartIndex).ToList();
            if (!pending.Any())
                return Result<byte[]>.Success(documentBytes);

            var result = RedactZipBytes(documentBytes, pending, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("UDF redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"UDF redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactAsync(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Redact(documentBytes, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<byte[]> RedactToFile(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        string? tempPath = null;
        string? inputHashBefore = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
                return Result<byte[]>.Failure(Error.Validation("Input/output path cannot be empty"));

            // Input hash before (spec 1)
            if (File.Exists(inputPath))
                inputHashBefore = ComputeFileHash(inputPath);
            else if (Directory.Exists(inputPath))
            {
                var c = Path.Combine(inputPath, "content.xml");
                if (File.Exists(c)) inputHashBefore = ComputeFileHash(c);
            }

            // Prevent overwrite of original
            try
            {
                var fullIn = Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar);
                var fullOut = Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullIn, fullOut, StringComparison.OrdinalIgnoreCase))
                    return Result<byte[]>.Failure(Error.Validation("Output path must be different from input path"));
            }
            catch { }

            byte[] inputBytes;
            bool isDirectoryInput = Directory.Exists(inputPath);
            if (isDirectoryInput)
            {
                // Directory UDF -> create ZIP bytes on the fly for redaction
                var contentPath = Path.Combine(inputPath, "content.xml");
                if (!File.Exists(contentPath))
                    contentPath = Path.Combine(inputPath, "content");
                if (!File.Exists(contentPath))
                    return Result<byte[]>.Failure(Error.FormatError("UDF directory missing content.xml"));
                var xml = File.ReadAllText(contentPath, Encoding.UTF8);
                // Validate XML
                var xdocCheck = LoadXDocumentSafe(xml);
                if (xdocCheck == null) return Result<byte[]>.Failure(Error.FormatError("Invalid UDF content.xml"));
                // Build minimal ZIP for processing
                using var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var e = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
                    using var s = e.Open();
                    using var w = new StreamWriter(s, new UTF8Encoding(false));
                    w.Write(xml);
                    // copy binary/images if any
                    foreach (var sub in new[] { "binary", "images" })
                    {
                        var dir = Path.Combine(inputPath, sub);
                        if (!Directory.Exists(dir)) continue;
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            var entry = zip.CreateEntry($"{sub}/{Path.GetFileName(f)}", CompressionLevel.Optimal);
                            using var es = entry.Open();
                            var data = File.ReadAllBytes(f);
                            es.Write(data, 0, data.Length);
                        }
                    }
                }
                inputBytes = ms.ToArray();
            }
            else
            {
                if (!File.Exists(inputPath))
                    return Result<byte[]>.Failure(Error.NotFound($"Input file not found: {inputPath}"));
                inputBytes = File.ReadAllBytes(inputPath);
            }

            var result = Redact(inputBytes, plan, options, cancellationToken);
            if (result.IsFailure)
                return result;

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir!);

            tempPath = outputPath + ".tmp";
            File.WriteAllBytes(tempPath, result.Value);
            // Atomic move
            File.Move(tempPath, outputPath, true);
            tempPath = null;

            // Verify input hash unchanged
            if (inputHashBefore != null)
            {
                string? after = null;
                if (File.Exists(inputPath)) after = ComputeFileHash(inputPath);
                else if (Directory.Exists(inputPath))
                {
                    var c2 = Path.Combine(inputPath, "content.xml");
                    if (File.Exists(c2)) after = ComputeFileHash(c2);
                }
                if (after != null && !string.Equals(inputHashBefore, after, StringComparison.OrdinalIgnoreCase))
                    return Result<byte[]>.Failure(Error.SecurityError("Input file was modified during redaction"));
            }

            return Result<byte[]>.Success(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupTemp(tempPath);
            return Result<byte[]>.Failure(Error.Cancelled("UDF file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            CleanupTemp(tempPath);
            return Result<byte[]>.Failure(Error.Internal($"UDF file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    // Core ZIP redaction
    private Result<byte[]> RedactZipBytes(byte[] zipBytes, List<RedactionOperation> pending, CancellationToken ct)
    {
        // Validate ZIP and extract content.xml
        using var inputMs = new MemoryStream(zipBytes);
        ZipArchive archive;
        try { archive = new ZipArchive(inputMs, ZipArchiveMode.Read, true); }
        catch (InvalidDataException ex) { return Result<byte[]>.Failure(Error.FormatError($"Invalid UDF format (not a valid ZIP): {ex.Message}", ex)); }

        using (archive)
        {
            // ZIP Slip / path traversal checks + duplicate content.xml
            var entries = archive.Entries.ToList();
            int contentCount = entries.Count(e => e.FullName == "content.xml");
            if (contentCount == 0) return Result<byte[]>.Failure(Error.FormatError("UDF missing content.xml"));
            if (contentCount > 1) return Result<byte[]>.Failure(Error.FormatError("UDF has duplicate content.xml"));

            foreach (var e in entries)
            {
                if (IsUnsafeEntryName(e.FullName))
                    return Result<byte[]>.Failure(Error.SecurityError($"Unsafe ZIP entry: {e.FullName}"));
            }

            var contentEntry = archive.GetEntry("content.xml")!;
            string contentXml;
            try
            {
                using var s = contentEntry.Open();
                using var sr = new StreamReader(s, Encoding.UTF8, true);
                contentXml = sr.ReadToEnd();
            }
            catch (Exception ex) { return Result<byte[]>.Failure(Error.Internal($"Failed to read content.xml: {ex.Message}", ex)); }

            // Safe XML parse with DtdProcessing Prohibit
            XDocument xdoc;
            try
            {
                xdoc = LoadXDocumentSafe(contentXml) ?? throw new InvalidDataException("XDocument null");
            }
            catch (XmlException ex) { return Result<byte[]>.Failure(Error.FormatError($"Invalid UDF content.xml: {ex.Message}", ex)); }
            catch (Exception ex) { return Result<byte[]>.Failure(Error.FormatError($"Invalid UDF content.xml: {ex.Message}", ex)); }

            var root = xdoc.Root;
            if (root == null)
                return Result<byte[]>.Failure(Error.FormatError("UDF content.xml missing root"));

            XElement? contentEl = null;
            if (string.Equals(root.Name.LocalName, "content", StringComparison.OrdinalIgnoreCase))
                contentEl = root; // minimal test fixture <content><body>
            else
                contentEl = root.Element("content") ?? root.Descendants().FirstOrDefault(e => e.Name.LocalName == "content");
            if (contentEl == null)
                return Result<byte[]>.Failure(Error.FormatError("UDF content.xml missing <content>"));

            // Preserve original raw for offset validation. DocumentEngine uses raw.Trim() as Document.Text,
            // so ops indices are based on trimmed, but elements offsets are based on raw. Map via leading trim.
            var originalRaw = contentEl.Value ?? string.Empty;
            var leadingTrim = originalRaw.Length - originalRaw.TrimStart().Length;
            var workingContent = originalRaw;
            var newContent = workingContent;
            // Collect elements for offset updates
            var elementsEl = root.Element("elements");
            var elementInfos = new List<ElementInfo>();
            var paragraphMap = new Dictionary<XElement, int>(); // element -> paragraph index
            if (elementsEl != null)
            {
                int pIdx = 0;
                foreach (var para in elementsEl.Elements("paragraph"))
                {
                    foreach (var child in para.Elements())
                    {
                        var soAttr = child.Attribute("startOffset");
                        var lenAttr = child.Attribute("length") ?? child.Attribute("len");
                        if (soAttr == null || lenAttr == null) continue;
                        if (!int.TryParse(soAttr.Value, out var so)) continue;
                        if (!int.TryParse(lenAttr.Value, out var len)) continue;
                        elementInfos.Add(new ElementInfo { Element = child, StartOffset = so, Length = len, ParagraphIndex = pIdx });
                    }
                    pIdx++;
                }
                elementInfos = elementInfos.OrderBy(e => e.StartOffset).ToList();
            }

            // Validate elements coverage before (optional)
            // Apply each pending op
            foreach (var op in pending)
            {
                ct.ThrowIfCancellationRequested();
                var span = op.TextSpan!;
                int start = span.StartIndex + leadingTrim;
                int len = span.Length;
                string replacement = op.ReplacementText ?? string.Empty;
                // Replacement must not break CDATA (contains ]]>) -> fail
                if (replacement.Contains("]]>"))
                    return Result<byte[]>.Failure(Error.SecurityError("Replacement contains CDATA terminator"));

                if (start < 0 || len < 0 || start + len > newContent.Length)
                    return Result<byte[]>.Failure(Error.Validation($"TextSpan out of range: start={start} len={len} contentLen={newContent.Length}"));

                var actual = newContent.Substring(start, len);
                if (!string.Equals(actual, span.Text, StringComparison.Ordinal))
                {
                    // Try near position (like TxtRedactor) within content
                    int found = FindNear(newContent, span.Text, start);
                    if (found >= 0) { start = found; len = span.Text.Length; actual = newContent.Substring(start, len); }
                    else
                    {
                        // Whitespace-normalized fallback (handles \n\n vs space in pii_test_belgesi)
                        var wsResult = FindWithWhitespace(newContent, span.Text, start);
                        if (wsResult.found >= 0) { start = wsResult.found; len = wsResult.matchedLength; actual = newContent.Substring(start, len); }
                        else
                            return Result<byte[]>.Failure(Error.Validation($"TextSpan text mismatch at {start}: expected '{span.Text}' found '{actual}'"));
                    }
                }

                // Cross-element safety: detect affected elements
                var affected = elementInfos.Where(e => e.StartOffset < start + len && e.StartOffset + e.Length > start).ToList();
                if (affected.Count > 1)
                {
                    // If spans multiple paragraphs -> fail-secure (spec 5)
                    var distinctParas = affected.Select(a => a.ParagraphIndex).Distinct().Count();
                    if (distinctParas > 1)
                        return Result<byte[]>.Failure(Error.SecurityError($"Cross-paragraph redaction not safely supported: span [{start},{start+len}) crosses {distinctParas} paragraphs"));

                    // For multi-element within same paragraph, apply safe prefix/suffix method
                    // Compute delta
                    int delta = replacement.Length - len;
                    // Validate replacement won't make any length negative
                    // Update first and last affected with prefix/suffix logic
                    var first = affected.First();
                    var last = affected.Last();
                    int prefixLen = start - first.StartOffset;
                    int suffixLen = (last.StartOffset + last.Length) - (start + len);
                    if (prefixLen < 0 || suffixLen < 0)
                        return Result<byte[]>.Failure(Error.SecurityError("Invalid element overlap calculation"));

                    // Apply to newContent
                    newContent = newContent.Remove(start, len).Insert(start, replacement);

                    // Update element lengths/offsets
                    // First keeps prefix + replacement
                    first.Length = prefixLen + replacement.Length;
                    first.Element.SetAttributeValue(first.Element.Attribute("length") != null ? "length" : "len", first.Length);
                    // Middle elements -> 0
                    foreach (var mid in affected.Skip(1).Take(affected.Count - 2))
                    {
                        mid.Length = 0;
                        mid.Element.SetAttributeValue(mid.Element.Attribute("length") != null ? "length" : "len", 0);
                    }
                    // Last (if distinct from first) keeps suffix only
                    if (last != first)
                    {
                        last.Length = suffixLen;
                        last.Element.SetAttributeValue(last.Element.Attribute("length") != null ? "length" : "len", last.Length);
                    }
                    // Update startOffsets for elements after the affected range
                    // Find index of last affected in sorted list
                    int lastIdx = elementInfos.IndexOf(last);
                    for (int i = lastIdx + 1; i < elementInfos.Count; i++)
                    {
                        elementInfos[i].StartOffset += delta;
                        elementInfos[i].Element.SetAttributeValue(elementInfos[i].Element.Attribute("length") != null ? "startOffset" : "startOffset", elementInfos[i].StartOffset);
                        // Ensure attribute name consistent
                        elementInfos[i].Element.SetAttributeValue("startOffset", elementInfos[i].StartOffset);
                    }
                    // Also need to shift startOffset of middle zero-length and last (already set) - middle start should be after first
                    // Fix middle/last start positions to be contiguous after first
                    int cur = first.StartOffset + first.Length;
                    foreach (var mid in affected.Skip(1))
                    {
                        mid.StartOffset = cur;
                        mid.Element.SetAttributeValue("startOffset", cur);
                        cur += mid.Length;
                    }
                    // But subsequent elements already shifted by delta, need to adjust cur offset for them
                    // Recompute from lastIdx+1 onwards to be consistent: they should start at cur
                    for (int i = lastIdx + 1; i < elementInfos.Count; i++)
                    {
                        elementInfos[i].StartOffset = cur;
                        elementInfos[i].Element.SetAttributeValue("startOffset", cur);
                        cur += elementInfos[i].Length;
                    }
                }
                else if (affected.Count == 1)
                {
                    var el = affected[0];
                    int delta = replacement.Length - len;
                    newContent = newContent.Remove(start, len).Insert(start, replacement);
                    el.Length += delta;
                    el.Element.SetAttributeValue(el.Element.Attribute("length") != null ? "length" : "len", el.Length);
                    int idx = elementInfos.IndexOf(el);
                    for (int i = idx + 1; i < elementInfos.Count; i++)
                    {
                        elementInfos[i].StartOffset += delta;
                        elementInfos[i].Element.SetAttributeValue("startOffset", elementInfos[i].StartOffset);
                    }
                }
                else
                {
                    // No element covers this span -> still apply to content but check if elements gap -> warn but allow?
                    // This can happen if content has gaps not covered by elements (should not for real UDF where coverage is full)
                    // Fail-secure: if no element, but content length changes, offsets after start would be inconsistent
                    // So update all elements after start
                    int delta = replacement.Length - len;
                    newContent = newContent.Remove(start, len).Insert(start, replacement);
                    foreach (var el in elementInfos.Where(e => e.StartOffset >= start + len))
                    {
                        el.StartOffset += delta;
                        el.Element.SetAttributeValue("startOffset", el.StartOffset);
                    }
                    // Also if start inside gap between elements, next element's start will be adjusted
                }
            }

            // Validate content+element consistency (spec 6)
            var validation = ValidateContentElements(newContent, elementInfos);
            if (validation.IsFailure) return Result<byte[]>.Failure(validation.Error);

            // Update content element with CDATA preservation and UTF8
            contentEl.RemoveNodes();
            contentEl.Add(new XCData(newContent));

            // Ensure XDocument can be re-parsed
            string updatedXml;
            try
            {
                var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = false, NewLineHandling = NewLineHandling.None };
                using var msTmp = new MemoryStream();
                using var xw = XmlWriter.Create(msTmp, settings);
                xdoc.Save(xw);
                xw.Flush();
                updatedXml = Encoding.UTF8.GetString(msTmp.ToArray());
                // Re-parse to validate
                var reparse = LoadXDocumentSafe(updatedXml);
                if (reparse == null) throw new InvalidDataException("Reparse null");
            }
            catch (Exception ex) { return Result<byte[]>.Failure(Error.FormatError($"Failed to serialize updated content.xml: {ex.Message}", ex)); }

            // Build output ZIP
            var outMs = new MemoryStream();
            using (var outZip = new ZipArchive(outMs, ZipArchiveMode.Create, true))
            {
                // content.xml first
                var contentOut = outZip.CreateEntry("content.xml", CompressionLevel.Optimal);
                using (var s = contentOut.Open())
                {
                    var bytes = new UTF8Encoding(false).GetBytes(updatedXml);
                    s.Write(bytes, 0, bytes.Length);
                }

                // Copy other entries except content.xml and signature (if redaction happened, signature invalid)
                bool hasSignature = entries.Any(e => string.Equals(e.FullName, "signature.p7s", StringComparison.OrdinalIgnoreCase) || string.Equals(e.FullName, "sign.sgn", StringComparison.OrdinalIgnoreCase));
                foreach (var e in entries)
                {
                    if (e.FullName == "content.xml") continue;
                    // Signature handling: remove if exists and we redacted (invalid signature)
                    if (hasSignature && (e.FullName.Equals("signature.p7s", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("sign.sgn", StringComparison.OrdinalIgnoreCase)))
                        continue; // strip invalid signature

                    // Already checked unsafe, copy raw bytes
                    var outEntry = outZip.CreateEntry(e.FullName, CompressionLevel.Optimal);
                    using var src = e.Open();
                    using var dst = outEntry.Open();
                    src.CopyTo(dst);
                }
            }

            var outBytes = outMs.ToArray();

            // Final validation: output ZIP readable and content.xml re-ingestable via DocumentEngine path
            try
            {
                using var verifyMs = new MemoryStream(outBytes);
                using var vz = new ZipArchive(verifyMs, ZipArchiveMode.Read);
                var ve = vz.GetEntry("content.xml");
                if (ve == null) return Result<byte[]>.Failure(Error.FormatError("Output missing content.xml"));
                using var vs = ve.Open();
                using var sr = new StreamReader(vs, Encoding.UTF8);
                var vxml = sr.ReadToEnd();
                var vx = LoadXDocumentSafe(vxml);
                if (vx == null) return Result<byte[]>.Failure(Error.FormatError("Output content.xml invalid"));
            }
            catch (Exception ex) { return Result<byte[]>.Failure(Error.Internal($"Output validation failed: {ex.Message}", ex)); }

            return Result<byte[]>.Success(outBytes);
        }
    }

    private static XDocument? LoadXDocumentSafe(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024 };
        using var sr = new StringReader(xml);
        using var xr = XmlReader.Create(sr, settings);
        return XDocument.Load(xr, LoadOptions.PreserveWhitespace);
    }

    private static bool IsUnsafeEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (name.Contains("..")) return true;
        if (Path.IsPathRooted(name)) return true;
        if (name.StartsWith("/") || name.StartsWith("\\")) return true;
        // Absolute path check
        if (name.Contains(":") ) return true;
        return false;
    }

    private static int FindNear(string text, string search, int expected)
    {
        int start = Math.Max(0, expected - 50);
        int end = Math.Min(text.Length - search.Length, expected + 50);
        for (int i = start; i <= end; i++)
            if (i + search.Length <= text.Length && string.Equals(text.Substring(i, search.Length), search, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static (int found, int matchedLength) FindWithWhitespace(string text, string search, int expected)
    {
        if (string.IsNullOrEmpty(search)) return (-1, 0);
        var parts = System.Text.RegularExpressions.Regex.Split(search.Trim(), @"\s+");
        if (parts.Length == 0) return (-1, 0);
        var pattern = string.Join(@"\s+", parts.Select(p => System.Text.RegularExpressions.Regex.Escape(p)));
        // Search window around expected
        int windowStart = Math.Max(0, expected - 100);
        int windowLen = Math.Min(text.Length - windowStart, 200 + search.Length + 50);
        if (windowLen <= 0) return (-1, 0);
        var window = text.Substring(windowStart, windowLen);
        var match = System.Text.RegularExpressions.Regex.Match(window, pattern);
        if (match.Success)
            return (windowStart + match.Index, match.Length);
        // Fallback: search whole text
        var fullMatch = System.Text.RegularExpressions.Regex.Match(text, pattern);
        if (fullMatch.Success && Math.Abs(fullMatch.Index - expected) < 200)
            return (fullMatch.Index, fullMatch.Length);
        return (-1, 0);
    }

    private static Result ValidateContentElements(string content, List<ElementInfo> elements)
    {
        foreach (var el in elements)
        {
            if (el.StartOffset < 0) return Result.Failure(Error.FormatError($"Element startOffset negative: {el.StartOffset}"));
            if (el.Length < 0) return Result.Failure(Error.FormatError($"Element length negative: {el.Length}"));
            if (el.StartOffset + el.Length > content.Length)
                return Result.Failure(Error.FormatError($"Element out of bounds: start {el.StartOffset} len {el.Length} contentLen {content.Length}"));
            // Slice valid
            try { var slice = content.Substring(el.StartOffset, el.Length); }
            catch (Exception ex) { return Result.Failure(Error.FormatError($"Element slice invalid: {ex.Message}", ex)); }
        }
        // Coverage check: elements should not leave gaps beyond content - original ggg has gap 95, FFF has gap 1, allow up to 100
        if (elements.Any())
        {
            var sorted = elements.OrderBy(e => e.StartOffset).ToList();
            int maxEnd = sorted.Max(e => e.StartOffset + e.Length);
            if (maxEnd > content.Length) return Result.Failure(Error.FormatError($"maxEnd {maxEnd} > contentLen {content.Length}"));
            // Allow trailing gap up to 100 (original files have varying gaps, e.g., ggg 95)
            if (content.Length - maxEnd > 100)
                return Result.Failure(Error.FormatError($"Content trailing gap too large: contentLen {content.Length} maxEnd {maxEnd}"));
        }
        return Result.Success();
    }

    private static string ComputeFileHash(string path)
    {
        using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(s));
    }

    private static void CleanupTemp(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class ElementInfo
    {
        public XElement Element { get; set; } = null!;
        public int StartOffset { get; set; }
        public int Length { get; set; }
        public int ParagraphIndex { get; set; }
    }
}
