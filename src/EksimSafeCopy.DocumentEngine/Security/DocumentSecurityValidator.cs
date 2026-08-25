namespace EksimSafeCopy.DocumentEngine.Security;

using global::EksimSafeCopy.Core.Abstractions;
using global::EksimSafeCopy.Core.Models;
using CoreHashAlgorithm = global::EksimSafeCopy.Core.Abstractions.HashAlgorithm;
using System.Security.Cryptography;

public sealed class DocumentSecurityValidator : IDocumentSecurityValidator
{
    private readonly DocumentSecurityOptions _options;

    public DocumentSecurityValidator(DocumentSecurityOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Result ValidateFileAccess(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result.Failure(Error.Validation("File path cannot be empty"));

        if (!File.Exists(filePath))
            return Result.Failure(Error.NotFound($"File not found: {filePath}"));

        try
        {
            var fileInfo = new FileInfo(filePath);
            
            if (fileInfo.Length == 0)
                return Result.Failure(Error.Validation("File is empty"));

            if (fileInfo.Length > _options.MaxFileSizeBytes)
                return Result.Failure(Error.Validation($"File size exceeds maximum allowed: {fileInfo.Length} > {_options.MaxFileSizeBytes}"));

            if (IsReparsePoint(filePath))
                return Result.Failure(Error.SecurityError("Reparse points/symlinks are not allowed"));

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!_options.AllowedExtensions.Contains(extension))
                return Result.Failure(Error.Validation($"File extension not allowed: {extension}"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.IoError($"File access validation failed: {ex.Message}", ex));
        }
    }

    public Result ValidateFormatMatch(string filePath, DocumentFormat expectedFormat)
    {
        var detectedFormat = DetectFormatFromSignature(filePath);
        
        if (detectedFormat == DocumentFormat.Unknown)
            return Result.Failure(Error.FormatError("Could not detect file format from signature"));

        if (detectedFormat != expectedFormat)
        {
            return Result.Failure(Error.FormatError(
                $"Format mismatch: expected {expectedFormat}, detected {detectedFormat}"));
        }

        return Result.Success();
    }

    public DocumentFormat DetectFormatFromSignature(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return DetectFormatFromStream(stream);
        }
        catch
        {
            return DocumentFormat.Unknown;
        }
    }

    public DocumentFormat DetectFormatFromStream(Stream stream)
    {
        if (!stream.CanRead || stream.Length < 4)
            return DocumentFormat.Unknown;

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var header = new byte[16];
            var bytesRead = stream.Read(header, 0, header.Length);
            stream.Position = originalPosition;

            if (bytesRead < 4)
                return DocumentFormat.Unknown;

            // PDF: %PDF
            if (bytesRead >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
                return DocumentFormat.Pdf;

            // ZIP-based formats (DOCX, XLSX, UDF): PK\x03\x04 or PK\x05\x06 or PK\x07\x08
            if (bytesRead >= 4 && header[0] == 0x50 && header[1] == 0x4B && 
                (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
                (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08))
            {
                // Need to inspect ZIP contents to differentiate
                return DocumentFormat.Unknown; // Will be resolved by deeper inspection
            }

            // PNG: \x89PNG\r\n\x1a\n
            if (bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return DocumentFormat.Png;

            // JPEG: FF D8 FF
            if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return DocumentFormat.Jpeg;

            // TIFF: II\x2A\x00 or MM\x00\x2A
            if (bytesRead >= 4 && 
                ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00) ||
                 (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
                return DocumentFormat.Tiff;

            // BMP: BM
            if (bytesRead >= 2 && header[0] == 0x42 && header[1] == 0x4D)
                return DocumentFormat.Bmp;

            return DocumentFormat.Unknown;
        }
        catch
        {
            return DocumentFormat.Unknown;
        }
    }

    public Result<string> ComputeFileHash(string filePath, CoreHashAlgorithm algorithm = CoreHashAlgorithm.SHA256)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            System.Security.Cryptography.HashAlgorithm hasher = algorithm switch
            {
                CoreHashAlgorithm.SHA256 => SHA256.Create(),
                CoreHashAlgorithm.SHA512 => SHA512.Create(),
                CoreHashAlgorithm.MD5 => MD5.Create(),
                _ => SHA256.Create()
            };

            using (hasher)
            {
                var hash = hasher.ComputeHash(stream);
                return Result<string>.Success(Convert.ToHexString(hash));
            }
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(Error.IoError($"Hash computation failed: {ex.Message}", ex));
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class DocumentSecurityOptions
{
    public long MaxFileSizeBytes { get; init; } = 500_000_000; // 500 MB
    public HashSet<string> AllowedExtensions { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".txt", ".udf", ".png", ".jpg", ".jpeg", ".tiff", ".bmp"
    };
}

public interface IDocumentSecurityValidator
{
    Result ValidateFileAccess(string filePath);
    Result ValidateFormatMatch(string filePath, DocumentFormat expectedFormat);
    DocumentFormat DetectFormatFromSignature(string filePath);
    DocumentFormat DetectFormatFromStream(Stream stream);
    Result<string> ComputeFileHash(string filePath, CoreHashAlgorithm algorithm = CoreHashAlgorithm.SHA256);
}