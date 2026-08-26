namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;

public sealed class UdfRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Udf;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        // AŞAMA 8 KARARI: UDF (UYAP Doküman Formatı) gerçek formatı repository'de
        // doğrulanmamıştır (ZIP/XML iç yapısı, content.xml/attachments/signature
        // ilişkileri, şifreleme, sürüm farkları). Kısmi redaction ile eksik/yanlış
        // maskelenmiş UDF üretmek, güvenli çıktıdan daha risklidir.
        // Bu nedenle production-grade UDF redaction desteklenmiyor olarak işaretlendi.
        var hasPending = plan.Operations.Any(o => o.State == RedactionOperationState.Pending);
        if (hasPending)
        {
            return Result<byte[]>.Failure(Error.SecurityError(
                "UDF redaction desteklenmiyor: UYAP UDF formatı production verification testlerinden geçmedi. " +
                "Mevcut ZIP/XML parser (content.xml, attachments, signature) kısmi redact edebilir ancak eksik maskelenmiş UDF üretmek güvensizdir. " +
                "Bu nedenle PII içeren UDF için güvenli çıktı oluşturulmadı. Lütfen UDF'i PDF'e dönüştürüp TXT/DOCX/XLSX/Image olarak işleyin veya manuel redaksiyon uygulayın.",
                new { format = "UDF", pendingOperations = hasPending }));
        }

        return Result<byte[]>.Success(documentBytes);
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
