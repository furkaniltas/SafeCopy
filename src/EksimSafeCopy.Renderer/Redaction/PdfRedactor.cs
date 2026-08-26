namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

public sealed class PdfRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Pdf;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        // AŞAMA 2 KARARI: PdfSharp 6.2.0 (MIT) ile production-grade true redaction
        // güvenilir şekilde yapılamamaktadır.
        // - Content stream'ler compressed (FlateDecode), multiple streams, Form XObjects,
        //   font encoding/glyph mapping, Tj/TJ operatörleri, incremental update,
        //   annotation/attachment/metadata gibi attack surface'ler tam kapsanmıyor.
        // - Sadece siyah dikdörtgen/annotation eklemek text extraction ile PII'nin
        //   tekrar elde edilmesini engellemez → güvensiz.
        // - iText + pdfSweep true redaction sağlar ancak AGPL lisansı kapalı kaynak
        //   kurumsal Eksim SafeCopy dağıtımı için uygun değil; commercial lisans
        //   maliyet/onay gerektirir ve sessizce eklenemez.
        // Bu nedenle güvenli fallback: PDF redaction unsupported → Failure.
        // Bu, güvensiz PDF üretmekten daha doğrudur (AGENTS.md: Security > all).
        var hasPending = plan.Operations.Any(o => o.State == RedactionOperationState.Pending);
        if (hasPending)
        {
            return Result<byte[]>.Failure(Error.SecurityError(
                "PDF redaction desteklenmiyor: PdfSharp 6.2.0 ile true content-stream text removal güvenilir şekilde yapılamıyor. " +
                "Sadece annotation/overlay güvenli true redaction değildir. iText/pdfSweep AGPL olduğundan kapalı kaynak kurumsal ürün için commercial lisans gerektirir. " +
                "Bu nedenle PII içeren PDF için güvenli çıktı oluşturulmadı. Lütfen PDF'i TXT/DOCX/XLSX/Image formatına dönüştürün veya manuel redaksiyon uygulayın.",
                new { format = "PDF", pendingOperations = hasPending }));
        }

        // No pending operations → return original bytes (no PII to redact)
        // Still sanitize metadata as best-effort for PII-free PDFs
        try
        {
            var tempInput = Path.GetTempFileName();
            var tempOutput = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempInput, documentBytes);
                using var document = PdfReader.Open(tempInput, PdfDocumentOpenMode.Modify);
                SanitizeMetadata(document);
                document.Save(tempOutput);
                var resultBytes = File.ReadAllBytes(tempOutput);
                return Result<byte[]>.Success(resultBytes);
            }
            finally
            {
                if (File.Exists(tempInput)) File.Delete(tempInput);
                if (File.Exists(tempOutput)) File.Delete(tempOutput);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"PDF metadata sanitization failed: {ex.Message}", ex));
        }
    }

    private void SanitizeMetadata(PdfDocument document)
    {
        if (document.Info != null)
        {
            document.Info.Author = string.Empty;
            document.Info.Subject = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator = string.Empty;
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

            File.WriteAllBytes(outputPath, result.Value);

            return Result<byte[]>.Success(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("PDF file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"PDF file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }
}
