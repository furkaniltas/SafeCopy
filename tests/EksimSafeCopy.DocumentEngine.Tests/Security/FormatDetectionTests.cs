using System.IO;
using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using DocModel = global::EksimSafeCopy.Core.Models.Document;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Pdf;
using EksimSafeCopy.DocumentEngine.Ingestion.Docx;
using EksimSafeCopy.DocumentEngine.Ingestion.Xlsx;
using EksimSafeCopy.DocumentEngine.Ingestion.Txt;
using EksimSafeCopy.DocumentEngine.Ingestion.Udf;
using EksimSafeCopy.DocumentEngine.Ingestion.Image;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DocEngine = global::EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Security;

public class FormatDetectionTests
{
    private readonly IDocumentEngine _documentEngine;

public FormatDetectionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IDocumentIngestor, PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();
        
        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_ExtensionVsSignature_Match_ReturnsFormat()
    {
        var tempFile = CreateTempPdf();
        
        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DF.Pdf);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormat_ExtensionVsSignature_Mismatch_ReturnsFailure()
    {
        var tempFile = CreateTempPdf();
        var renamedFile = Path.ChangeExtension(tempFile, ".docx");
        
        try
        {
            File.Move(tempFile, renamedFile);
            
            var result = _documentEngine.DetectFormat(renamedFile);
            // Extension says DOCX but signature says PDF - should fail
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("FORMAT_ERROR");
        }
        finally
        {
            if (File.Exists(renamedFile)) File.Delete(renamedFile);
        }
    }

    [Fact]
    public void DetectFormat_UnknownExtension_ReturnsFailure()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xyz");
        File.WriteAllText(tempFile, "test");
        
        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("FORMAT_ERROR");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFormat_Stream_Pdf_ReturnsPdf()
    {
        var tempFile = CreateTempPdf();
        
        try
        {
            using var stream = File.OpenRead(tempFile);
            var result = _documentEngine.DetectFormat(stream);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DF.Pdf);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetSupportedFormats_ReturnsAllFormats()
    {
        var result = _documentEngine.GetSupportedFormats();
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(DF.Pdf);
        result.Value.Should().Contain(DF.Docx);
        result.Value.Should().Contain(DF.Xlsx);
        result.Value.Should().Contain(DF.Txt);
        result.Value.Should().Contain(DF.Udf);
        result.Value.Should().Contain(DF.Png);
        result.Value.Should().Contain(DF.Jpeg);
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
}






