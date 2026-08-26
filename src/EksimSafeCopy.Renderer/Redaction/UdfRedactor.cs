namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using System.IO.Compression;
using System.Xml.Linq;

public sealed class UdfRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Udf;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = plan.Operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .ToList();

            if (!operations.Any())
            {
                return Result<byte[]>.Success(documentBytes);
            }

            using var inputStream = new MemoryStream(documentBytes);
            using var outputStream = new MemoryStream();
            
            inputStream.CopyTo(outputStream);
            outputStream.Position = 0;

            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Update, true);
            
            // Process content.xml
            var contentEntry = archive.GetEntry("content.xml");
            if (contentEntry != null)
            {
                using var contentStream = contentEntry.Open();
                var contentXml = new StreamReader(contentStream).ReadToEnd();
                
                var redactedXml = RedactXmlContent(contentXml, plan.Operations.Where(o => o.State == RedactionOperationState.Pending).ToList());
                
                contentEntry.Delete();
                var newEntry = archive.CreateEntry("content.xml");
                using (var entryStream = newEntry.Open())
                using (var writer = new StreamWriter(entryStream, System.Text.Encoding.UTF8))
                {
                    writer.Write(redactedXml);
                }
            }

            // Process other XML files that might contain text
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.Name != "content.xml").ToList())
            {
using var contentStream = entry.Open();
            var xmlContent = new StreamReader(contentStream).ReadToEnd();
                
                var redactedXml = RedactXmlContent(xmlContent, plan.Operations.Where(o => o.State == RedactionOperationState.Pending).ToList());
                
                if (redactedXml != xmlContent)
                {
                    entry.Delete();
                    var newEntry = archive.CreateEntry(entry.FullName);
                    using var newEntryStream = newEntry.Open();
                    using (var writer = new StreamWriter(newEntryStream, System.Text.Encoding.UTF8))
                    {
                        writer.Write(redactedXml);
                    }
                }
            }

            return Result<byte[]>.Success(outputStream.ToArray());
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

    private string RedactXmlContent(string xml, List<RedactionOperation> operations)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var textNodes = doc.DescendantNodes().OfType<XText>().ToList();
            
            foreach (var textNode in textNodes)
            {
                var text = textNode.Value;
                var matchingOps = operations
                    .Where(o => o.State == RedactionOperationState.Pending && 
                               o.TextSpan != null &&
                               o.TextSpan.StartIndex >= 0 &&
                               o.TextSpan.EndIndex <= text.Length)
                    .ToList();

                if (matchingOps.Any())
                {
                    var textBuilder = new System.Text.StringBuilder(text);
                    
                    foreach (var op in matchingOps.OrderByDescending(o => o.TextSpan!.StartIndex))
                    {
                        if (op.TextSpan == null) continue;
                        
                        var start = op.TextSpan.StartIndex;
                        var length = op.TextSpan.Length;
                        var replacement = op.Strategy == RedactionStrategy.FullRedaction
                            ? new string('█', length)
                            : op.ReplacementText ?? string.Empty;

                        if (start >= 0 && start + length <= textBuilder.Length)
                        {
                            textBuilder.Remove(start, length);
                            textBuilder.Insert(start, replacement);
                        }
                    }

                    textNode.Value = textBuilder.ToString();
                }
            }

            return doc.ToString();
        }
        catch
        {
            return xml;
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
            return Result<byte[]>.Failure(Error.Cancelled("UDF file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"UDF file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }
}