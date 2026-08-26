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

            var operationsByPage = operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .GroupBy(o => o.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            int currentPage = 1;
            int charOffset = 0;

            foreach (var paragraph in body.Elements<Paragraph>())
            {
                foreach (var run in paragraph.Elements<Run>())
                {
                    var textElements = run.Elements<Text>().ToList();
                    foreach (var text in textElements)
                    {
                        var textContent = text.Text;
                        var textStart = charOffset;
                        var textEnd = charOffset + textContent.Length;

                        var pageOps = operationsByPage.TryGetValue(currentPage, out var pageOpsList) ? pageOpsList : new List<RedactionOperation>();
                        var matchingOps = pageOps.Where(o => o.TextSpan != null && 
                            o.TextSpan.StartIndex >= textStart && 
                            o.TextSpan.EndIndex <= textEnd).ToList();

                        foreach (var op in matchingOps.OrderByDescending(o => o.TextSpan!.StartIndex))
                        {
                            var span = op.TextSpan!;
                            var relativeStart = span.StartIndex - textStart;
                            var spanLength = span.Length;

                            if (relativeStart >= 0 && relativeStart + spanLength <= textContent.Length)
                            {
                                var replacement = op.Strategy == RedactionStrategy.FullRedaction
                                    ? new string('█', spanLength)
                                    : op.ReplacementText ?? string.Empty;

                                text.Text = textContent.Remove(relativeStart, spanLength).Insert(relativeStart, replacement);
                                textContent = text.Text;
                            }
                        }

                        charOffset += textContent.Length;
                    }
                }

                // Handle page breaks
                if (paragraph.Elements<Break>().Any(b => b.Type?.Value == BreakValues.Page))
                {
                    currentPage++;
                    charOffset = 0;
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

            mainPart.Document.Save();
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
        foreach (var paragraph in element.Elements<Paragraph>())
        {
            foreach (var run in paragraph.Elements<Run>())
            {
                var textElements = run.Elements<Text>().ToList();
                foreach (var text in textElements)
                {
                    var textContent = text.Text;
                    
                    var ops = operations.Where(o => o.TextSpan != null && 
                        o.TextSpan.StartIndex >= 0 && 
                        o.TextSpan.EndIndex <= textContent.Length).ToList();

                    foreach (var op in ops.OrderByDescending(o => o.TextSpan!.StartIndex))
                    {
                        var span = op.TextSpan!;
                        var replacement = op.Strategy == RedactionStrategy.FullRedaction
                            ? new string('█', span.Length)
                            : op.ReplacementText ?? string.Empty;

                        text.Text = textContent.Remove(span.StartIndex, span.Length).Insert(span.StartIndex, replacement);
                        textContent = text.Text;
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