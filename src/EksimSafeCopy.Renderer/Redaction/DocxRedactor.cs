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

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                // Use paragraph-level text to handle split runs (e.g., "Ahmet " + "Yılmaz" across two w:t)
                var paraText = paragraph.InnerText;
                var needsRedaction = sortedOps.Any(op => !string.IsNullOrEmpty(op.TextSpan!.Text) && paraText.Contains(op.TextSpan!.Text));
                if (!needsRedaction) continue;

                // Collect all Text elements in this paragraph (including those inside Hyperlink/SmartTag)
                var textElements = paragraph.Descendants<Text>().ToList();
                if (textElements.Count == 0) continue;

                // Build combined text and perform replacements
                var combined = string.Concat(textElements.Select(t => t.Text));
                var newCombined = combined;
                foreach (var op in sortedOps)
                {
                    var spanText = op.TextSpan!.Text;
                    if (string.IsNullOrEmpty(spanText)) continue;
                    if (!newCombined.Contains(spanText)) continue;
                    var replacement = op.Strategy == RedactionStrategy.FullRedaction
                        ? new string('█', spanText.Length)
                        : op.ReplacementText ?? string.Empty;
                    newCombined = newCombined.Replace(spanText, replacement);
                }
                if (newCombined == combined) continue;

                // Distribute newCombined back to Text elements - simplest: first Text gets all, rest cleared
                // Preserves at least the redacted content; formatting of split runs is secondary to security
                textElements[0].Text = newCombined;
                textElements[0].Space = SpaceProcessingModeValues.Preserve;
                for (int i = 1; i < textElements.Count; i++)
                {
                    textElements[i].Text = string.Empty;
                }
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
