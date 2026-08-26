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
                foreach (var run in paragraph.Elements<Run>())
                {
                    var textElements = run.Elements<Text>().ToList();
                    foreach (var text in textElements)
                    {
                        var textContent = text.Text;
                        if (string.IsNullOrEmpty(textContent)) continue;

                        var newContent = textContent;
                        foreach (var op in sortedOps)
                        {
                            var spanText = op.TextSpan!.Text;
                            if (string.IsNullOrEmpty(spanText)) continue;
                            if (!newContent.Contains(spanText)) continue;

                            var replacement = op.Strategy == RedactionStrategy.FullRedaction
                                ? new string('█', spanText.Length)
                                : op.ReplacementText ?? string.Empty;

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

            // Sanitize custom properties (docProps/custom.xml) - clear if contains PII-like
            // Producer/Creator handled via extended properties sanitization in Verification

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
        var sortedOps = operations
            .Where(o => o.State == RedactionOperationState.Pending && o.TextSpan != null)
            .OrderByDescending(o => o.TextSpan!.Text.Length)
            .ToList();

        foreach (var paragraph in element.Descendants<Paragraph>())
        {
            foreach (var run in paragraph.Elements<Run>())
            {
                var textElements = run.Elements<Text>().ToList();
                foreach (var text in textElements)
                {
                    var textContent = text.Text;
                    if (string.IsNullOrEmpty(textContent)) continue;

                    var newContent = textContent;
                    foreach (var op in sortedOps)
                    {
                        var spanText = op.TextSpan!.Text;
                        if (string.IsNullOrEmpty(spanText)) continue;
                        if (!newContent.Contains(spanText)) continue;

                        var replacement = op.Strategy == RedactionStrategy.FullRedaction
                            ? new string('█', spanText.Length)
                            : op.ReplacementText ?? string.Empty;

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
