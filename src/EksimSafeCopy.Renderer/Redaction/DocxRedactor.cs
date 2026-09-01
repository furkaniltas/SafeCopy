namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

public sealed class DocxRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Docx;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var inputStream = new MemoryStream(documentBytes);
            using var outputStream = new MemoryStream();
            
            inputStream.CopyTo(outputStream);
            outputStream.Position = 0;

            using var document = WordprocessingDocument.Open(outputStream, true);
            var operations = plan.Operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .ToList();

            var mainPart = document.MainDocumentPart;
            if (mainPart?.Document == null)
                return Result<byte[]>.Failure(Error.FormatError("DOCX document is empty or corrupted"));

            var body = mainPart.Document.Body;
            if (body == null)
                return Result<byte[]>.Success(ToByteArray(outputStream));

            // Build replacement map sorted by original text length descending to avoid partial overlaps
            var sortedOps = operations
                .Where(o => o.State == RedactionOperationState.Pending && o.TextSpan != null)
                .OrderByDescending(o => o.TextSpan!.Text.Length)
                .ToList();

            // Cross-paragraph redaction: build paragraph list with offsets to handle spans like "DİYARBAKIR İCRA DAİRESİ" split as "DİYARBAKIR" | "İCRA DAİRESİ" across two w:p
            var paragraphs = body.Descendants<Paragraph>().ToList();
            var paraInfos = new System.Collections.Generic.List<(Paragraph para, string text, int start, System.Collections.Generic.List<Text> elems)>();
            int docOffset = 0;
            foreach (var p in paragraphs)
            {
                var elems = p.Descendants<Text>().ToList();
                var txt = string.Concat(elems.Select(t => t.Text));
                // Fallback to InnerText if no Text elements (e.g., empty para)
                if (elems.Count == 0) txt = p.InnerText;
                paraInfos.Add((p, txt, docOffset, elems));
                docOffset += txt.Length + 1; // +1 for paragraph separator "\n" as in DocumentEngine
            }
            var fullDocText = string.Join("\n", paraInfos.Select(pi => pi.text));

            foreach (var op in sortedOps)
            {
                var spanText = op.TextSpan!.Text;
                if (string.IsNullOrEmpty(spanText) || op.TextSpan == null) continue;

                // First try single-paragraph exact match (fast path for non-cross-paragraph spans like "SABRİ GÖÇLÜ")
                bool handled = false;
                for (int pi = 0; pi < paraInfos.Count; pi++)
                {
                    var (para2, text2, start2, elems2) = paraInfos[pi];
                    if (!text2.Contains(spanText, StringComparison.Ordinal) && !string.Concat(elems2.Select(t => t.Text)).Contains(spanText, StringComparison.Ordinal)) continue;
                    var combined2 = string.Concat(elems2.Select(t => t.Text));
                    if (!combined2.Contains(spanText, StringComparison.Ordinal)) continue;
                    var replacement2 = op.Strategy == RedactionStrategy.FullRedaction ? new string('█', spanText.Length) : op.ReplacementText ?? string.Empty;
                    var newCombined2 = combined2.Replace(spanText, replacement2);
                    if (newCombined2 == combined2) continue;
                    elems2[0].Text = newCombined2;
                    elems2[0].Space = SpaceProcessingModeValues.Preserve;
                    for (int i = 1; i < elems2.Count; i++) elems2[i].Text = string.Empty;
                    paraInfos[pi] = (para2, newCombined2, start2, elems2);
                    handled = true;
                }
                if (handled)
                {
                    // Recompute offsets after single-para handling
                    int newOff = paraInfos[0].start;
                    for (int j = 0; j < paraInfos.Count; j++)
                    {
                        var (p2, t2, _, e2) = paraInfos[j];
                        paraInfos[j] = (p2, t2, newOff, e2);
                        newOff += t2.Length + 1;
                    }
                    fullDocText = string.Join("\n", paraInfos.Select(pi => pi.text));
                    continue;
                }

                // Cross-paragraph: use offsets with normalized whitespace handling
                int spanStart = op.TextSpan.StartIndex;
                int spanLen = op.TextSpan.Length;
                var normalizedFull = System.Text.RegularExpressions.Regex.Replace(fullDocText, @"\s+", " ");
                var normalizedSpan = System.Text.RegularExpressions.Regex.Replace(spanText, @"\s+", " ");
                if (spanStart < 0 || spanStart + spanLen > fullDocText.Length || (spanLen <= fullDocText.Length - spanStart && fullDocText.Substring(spanStart, Math.Min(spanLen, fullDocText.Length - spanStart)) != spanText))
                {
                    spanStart = normalizedFull.IndexOf(normalizedSpan, StringComparison.Ordinal);
                    if (spanStart < 0) continue;
                    spanLen = normalizedSpan.Length;
                }

                int spanEnd = spanStart + spanLen;
                // Find paragraphs that overlap this span
                for (int pi = 0; pi < paraInfos.Count; pi++)
                {
                    var (para, text, start, elems) = paraInfos[pi];
                    int paraEnd = start + text.Length;
                    // Check overlap: span [spanStart, spanEnd) with para [start, paraEnd)
                    if (spanEnd <= start || spanStart >= paraEnd) continue;
                    // Overlap region within this paragraph
                    int overlapStartInPara = Math.Max(spanStart, start) - start;
                    int overlapEndInPara = Math.Min(spanEnd, paraEnd) - start;
                    int overlapLen = overlapEndInPara - overlapStartInPara;
                    if (overlapLen <= 0) continue;
                    // Handle newline char at paragraph boundary (start+text.Length == span position of \n)
                    if (overlapStartInPara >= text.Length) continue;

                    var overlapText = text.Substring(overlapStartInPara, Math.Min(overlapLen, text.Length - overlapStartInPara));
                    // If span includes newline, skip that char for this para
                    if (overlapText == "\n") continue;

                    if (elems.Count == 0) continue;
                    var combined = string.Concat(elems.Select(t => t.Text));
                    var replacement = op.Strategy == RedactionStrategy.FullRedaction
                        ? new string('█', overlapText.Length)
                        : op.ReplacementText ?? string.Empty;
                    // If whole paragraph text equals overlap, replace directly
                    // Otherwise replace substring within combined
                    string newCombined;
                    if (combined.Contains(overlapText))
                        newCombined = combined.Replace(overlapText, replacement);
                    else
                        newCombined = combined; // fallback

                    if (newCombined == combined) continue;
                    elems[0].Text = newCombined;
                    elems[0].Space = SpaceProcessingModeValues.Preserve;
                    for (int i = 1; i < elems.Count; i++) elems[i].Text = string.Empty;
                    // Update paraInfos text and recompute offsets for subsequent ops
                    paraInfos[pi] = (para, newCombined, start, elems);
                    // Recompute start offsets for all paras after this one due to length change
                    int newOffset = paraInfos[0].start;
                    for (int j = 0; j < paraInfos.Count; j++)
                    {
                        var (p2, t2, _, e2) = paraInfos[j];
                        paraInfos[j] = (p2, t2, newOffset, e2);
                        newOffset += t2.Length + 1;
                    }
                }
                // For cross-paragraph spans like "NE ESAS TALEP EVRAKI" that may have been split across paras with extra whitespace/empty paras,
                // also do word-level fallback for any remaining words that are still present
                var remainingWords = normalizedSpan.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var currentFullForWordCheck = string.Join("\n", paraInfos.Select(pi => pi.text));
                var normalizedCurrent = System.Text.RegularExpressions.Regex.Replace(currentFullForWordCheck, @"\s+", " ");
                foreach (var word in remainingWords)
                {
                    if (!normalizedCurrent.Contains(word, StringComparison.OrdinalIgnoreCase)) continue;
                    for (int pi2 = 0; pi2 < paraInfos.Count; pi2++)
                    {
                        var (p2, txt2, s2, elems2) = paraInfos[pi2];
                        var comb2 = elems2.Count > 0 ? string.Concat(elems2.Select(t => t.Text)) : txt2;
                        if (comb2.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var rep2 = op.Strategy == RedactionStrategy.FullRedaction ? new string('█', word.Length) : op.ReplacementText ?? string.Empty;
                        var newComb2 = System.Text.RegularExpressions.Regex.Replace(comb2, System.Text.RegularExpressions.Regex.Escape(word), rep2, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (newComb2 == comb2) continue;
                        if (elems2.Count == 0) continue;
                        elems2[0].Text = newComb2;
                        elems2[0].Space = SpaceProcessingModeValues.Preserve;
                        for (int i = 1; i < elems2.Count; i++) elems2[i].Text = string.Empty;
                        paraInfos[pi2] = (p2, newComb2, s2, elems2);
                    }
                    currentFullForWordCheck = string.Join("\n", paraInfos.Select(pi => pi.text));
                    normalizedCurrent = System.Text.RegularExpressions.Regex.Replace(currentFullForWordCheck, @"\s+", " ");
                }
                // Update fullDocText for next ops (avoid double-redacting same region with different length)
                fullDocText = string.Join("\n", paraInfos.Select(pi => pi.text));
            }

            // Also process headers and footers
            if (mainPart.HeaderParts != null)
            {
                foreach (var headerPart in mainPart.HeaderParts)
                {
                    RedactHeaderFooter(headerPart.Header, plan.Operations.Where(o => o.State == RedactionOperationState.Pending).ToList(), options);
                }
            }

            if (mainPart.FooterParts != null)
            {
                foreach (var footerPart in mainPart.FooterParts)
                {
                    RedactHeaderFooter(footerPart.Footer, plan.Operations.Where(o => o.State == RedactionOperationState.Pending).ToList(), options);
                }
            }

            // Process comments (w:comment)
            var commentsPart = mainPart.WordprocessingCommentsPart;
            if (commentsPart?.Comments != null)
            {
                foreach (var comment in commentsPart.Comments.Descendants<Comment>())
                {
                    foreach (var para in comment.Descendants<Paragraph>())
                    {
                        foreach (var run in para.Elements<Run>())
                        {
                            foreach (var text in run.Elements<Text>().ToList())
                            {
                                var textContent = text.Text;
                                if (string.IsNullOrEmpty(textContent)) continue;
                                var newContent = textContent;
                                foreach (var op in sortedOps)
                                {
                                    var spanText = op.TextSpan!.Text;
                                    if (string.IsNullOrEmpty(spanText) || !newContent.Contains(spanText)) continue;
                                    var replacement = op.Strategy == RedactionStrategy.FullRedaction ? new string('█', spanText.Length) : op.ReplacementText ?? string.Empty;
                                    newContent = newContent.Replace(spanText, replacement);
                                }
                                if (newContent != textContent)
                                {
                                    text.Text = newContent;
                                    text.Space = SpaceProcessingModeValues.Preserve;
                                }
                            }
                        }
                    }
                }
            }

            // Sanitize metadata - clear all PII-bearing properties
            try
            {
                var props = document.PackageProperties;
                props.Creator = string.Empty;
                props.LastModifiedBy = string.Empty;
                props.Title = string.Empty;
                props.Subject = string.Empty;
                props.Keywords = string.Empty;
                props.Description = string.Empty;
                props.Category = string.Empty;
                // Remove custom properties part if exists
                if (document.CustomFilePropertiesPart != null)
                    document.DeletePart(document.CustomFilePropertiesPart);
                if (document.ExtendedFilePropertiesPart != null)
                {
                    var extProps = document.ExtendedFilePropertiesPart.Properties;
                    if (extProps != null)
                    {
                        // Clear company/manager which may contain PII
                        var company = extProps.GetFirstChild<global::DocumentFormat.OpenXml.ExtendedProperties.Company>();
                        if (company != null) company.Text = string.Empty;
                        var manager = extProps.GetFirstChild<global::DocumentFormat.OpenXml.ExtendedProperties.Manager>();
                        if (manager != null) manager.Text = string.Empty;
                    }
                }
            }
            catch { /* best effort */ }

            // Also redact footnotes/endnotes if present
            if (mainPart.FootnotesPart?.Footnotes != null)
            {
                foreach (var fn in mainPart.FootnotesPart.Footnotes.Elements<Footnote>())
                    RedactCompositeElement(fn, sortedOps, options);
            }
            if (mainPart.EndnotesPart?.Endnotes != null)
            {
                foreach (var en in mainPart.EndnotesPart.Endnotes.Elements<Endnote>())
                    RedactCompositeElement(en, sortedOps, options);
            }

            mainPart.Document.Save();
            document.Save();
            outputStream.Position = 0;
            return Result<byte[]>.Success(ToByteArray(outputStream));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("DOCX redaction was cancelled"));
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<byte[]>.Failure(Error.FormatError($"Invalid DOCX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"DOCX redaction failed: {ex.Message}", ex));
        }
    }

    private void RedactHeaderFooter(OpenXmlCompositeElement element, List<RedactionOperation> operations, RenderOptions options)
    {
        RedactCompositeElement(element, operations.Where(o => o.State == RedactionOperationState.Pending && o.TextSpan != null).OrderByDescending(o => o.TextSpan!.Text.Length).ToList(), options);
    }

    private void RedactCompositeElement(OpenXmlCompositeElement element, List<RedactionOperation> sortedOps, RenderOptions options)
    {
        foreach (var paragraph in element.Descendants<Paragraph>())
        {
            var paraText = paragraph.InnerText;
            if (!sortedOps.Any(op => !string.IsNullOrEmpty(op.TextSpan!.Text) && paraText.Contains(op.TextSpan!.Text))) continue;
            var textElements = paragraph.Descendants<Text>().ToList();
            if (textElements.Count == 0) continue;
            var combined = string.Concat(textElements.Select(t => t.Text));
            var newCombined = combined;
            foreach (var op in sortedOps)
            {
                var spanText = op.TextSpan!.Text;
                if (string.IsNullOrEmpty(spanText) || !newCombined.Contains(spanText)) continue;
                var replacement = op.Strategy == RedactionStrategy.FullRedaction ? new string('█', spanText.Length) : op.ReplacementText ?? string.Empty;
                newCombined = newCombined.Replace(spanText, replacement);
            }
            if (newCombined == combined) continue;
            textElements[0].Text = newCombined;
            textElements[0].Space = SpaceProcessingModeValues.Preserve;
            for (int i = 1; i < textElements.Count; i++) textElements[i].Text = string.Empty;
        }
    }

    public async Task<Result<byte[]>> RedactAsync(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Redact(documentBytes, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<byte[]> RedactToFile(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = File.ReadAllBytes(inputPath);
            var result = Redact(bytes, plan, options, cancellationToken);
            
            if (result.IsFailure)
                return Result<byte[]>.Failure(result.Error);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir!);

            var tempPath = outputPath + ".tmp";
            File.WriteAllBytes(tempPath, result.Value);
            File.Move(tempPath, outputPath, true);

            return Result<byte[]>.Success(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("DOCX file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"DOCX file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] ToByteArray(MemoryStream stream)
    {
        stream.Position = 0;
        return stream.ToArray();
    }
}
