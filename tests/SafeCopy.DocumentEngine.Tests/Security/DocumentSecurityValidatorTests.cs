using System.IO;
using System.Text;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using DocModel = global::SafeCopy.Core.Models.Document;
using SafeCopy.DocumentEngine.Security;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using Ox = DocumentFormat.OpenXml;
using OxPackaging = DocumentFormat.OpenXml.Packaging;
using OxWord = DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SafeCopy.DocumentEngine.Tests.Security;

public class DocumentSecurityValidatorTests
{
    private readonly IDocumentSecurityValidator _validator;

public DocumentSecurityValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        var provider = services.BuildServiceProvider();
        _validator = provider.GetRequiredService<IDocumentSecurityValidator>();
    }

    [Fact]
    public void ValidateFileAccess_ExistingFile_ReturnsSuccess()
    {
        var tempFile = CreateTempFile("test content");
        
        try
        {
            var result = _validator.ValidateFileAccess(tempFile);
            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFileAccess_NonExistentFile_ReturnsFailure()
    {
        var result = _validator.ValidateFileAccess("nonexistent.pdf");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void ValidateFileAccess_EmptyFile_ReturnsFailure()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(tempFile, Array.Empty<byte>());

        try
        {
            var result = _validator.ValidateFileAccess(tempFile);
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("VALIDATION_ERROR");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFileAccess_OversizedFile_ReturnsFailure()
    {
        var options = new DocumentSecurityOptions { MaxFileSizeBytes = 100 };
        var validator = new DocumentSecurityValidator(options);
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"large_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(tempFile, new byte[200]);

        try
        {
            var result = validator.ValidateFileAccess(tempFile);
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("VALIDATION_ERROR");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFileAccess_DisallowedExtension_ReturnsFailure()
    {
        var options = new DocumentSecurityOptions 
        { 
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" }
        };
        var validator = new DocumentSecurityValidator(options);
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A }); // MZ header

        try
        {
            var result = validator.ValidateFileAccess(tempFile);
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("VALIDATION_ERROR");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFormatMatch_MatchingFormat_ReturnsSuccess()
    {
        var tempFile = CreateTempPdf();
        
        try
        {
            var result = _validator.ValidateFormatMatch(tempFile, DF.Pdf);
            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ValidateFormatMatch_MismatchedFormat_ReturnsFailure()
    {
        var tempFile = CreateTempPdf();
        
        try
        {
            var result = _validator.ValidateFormatMatch(tempFile, DF.Docx);
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("FORMAT_ERROR");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormatFromSignature_Pdf_ReturnsPdf()
    {
        var tempFile = CreateTempPdf();
        
        try
        {
            var format = _validator.DetectFormatFromSignature(tempFile);
            format.Should().Be(DF.Pdf);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormatFromSignature_Docx_ReturnsUnknownZip()
    {
        var tempFile = CreateTempDocx();
        
        try
        {
            var format = _validator.DetectFormatFromSignature(tempFile);
            // DOCX is ZIP-based, signature detection returns Unknown for ZIP-based formats
            format.Should().Be(DF.Unknown);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormatFromSignature_Image_ReturnsCorrectFormat()
    {
        var tempFile = CreateTempPng();
        
        try
        {
            var format = _validator.DetectFormatFromSignature(tempFile);
            format.Should().Be(DF.Png);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ComputeFileHash_ValidFile_ReturnsHash()
    {
        var tempFile = CreateTempFile("test content");
        
        try
        {
            var result = _validator.ComputeFileHash(tempFile, HashAlgorithm.SHA256);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeEmpty();
            result.Value.Length.Should().Be(64); // SHA256 hex length
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ComputeFileHash_DifferentAlgorithms_ReturnDifferentHashes()
    {
        var tempFile = CreateTempFile("test content");
        
        try
        {
            var sha256 = _validator.ComputeFileHash(tempFile, HashAlgorithm.SHA256);
            var sha512 = _validator.ComputeFileHash(tempFile, HashAlgorithm.SHA512);
            var md5 = _validator.ComputeFileHash(tempFile, HashAlgorithm.MD5);

            sha256.IsSuccess.Should().BeTrue();
            sha512.IsSuccess.Should().BeTrue();
            md5.IsSuccess.Should().BeTrue();

            sha256.Value.Should().NotBe(sha512.Value);
            sha256.Value.Should().NotBe(md5.Value);
            sha512.Value.Should().NotBe(md5.Value);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private string CreateTempFile(string content)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, content);
        return tempFile;
    }

    private string CreateTempPdf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        var pdfContent = @"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>
endobj
4 0 obj
<< /Length 44 >>
stream
BT /F1 12 Tf 100 700 Td (Test) Tj ET
endstream
endobj
5 0 obj
<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000216 00000 n 
0000000317 00000 n 
trailer
<< /Size 6 /Root 1 0 R >>
startxref
400
%%EOF";
        File.WriteAllText(tempFile, pdfContent);
        return tempFile;
    }

private string CreateTempDocx()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        using (var wordDoc = OxPackaging.WordprocessingDocument.Create(tempFile, Ox.WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new OxWord.Document(new OxWord.Body(new OxWord.Paragraph(new OxWord.Run(new OxWord.Text("Test")))));
        }
        return tempFile;
    }

    private string CreateTempPng()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.png");
        using (var image = new Image<Rgba32>(10, 10))
        {
            image.SaveAsPng(tempFile);
        }
        return tempFile;
    }
}






