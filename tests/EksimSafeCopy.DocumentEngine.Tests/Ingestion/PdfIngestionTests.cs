using System.Text;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Pdf;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DocEngine = global::EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DocModel = global::EksimSafeCopy.Core.Models.Document;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Ingestion;

public class PdfIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

    public PdfIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, global::EksimSafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, PdfDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();

        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_PdfFile_ReturnsPdf()
    {
        var tempFile = CreateTempPdf();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DocumentFormat.Pdf);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ValidPdf_ReturnsDocument()
    {
        var tempFile = CreateTempPdf();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DocumentFormat.Pdf);
            result.Value.Pages.Should().NotBeEmpty();
            result.Value.Source.FilePath.Should().Be(tempFile);
            result.Value.Source.FileHash.Should().NotBeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsFailure()
    {
        var result = _documentEngine.Load("nonexistent.pdf");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void Load_EmptyPdf_ReturnsDocumentWithPages()
    {
        var tempFile = CreateEmptyPdf();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Pages.Should().NotBeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
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
BT /F1 12 Tf 100 700 Td (Test PDF Content) Tj ET
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

    private string CreateEmptyPdf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.pdf");

        var pdfContent = @"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>
endobj
xref
0 4
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
trailer
<< /Size 4 /Root 1 0 R >>
startxref
180
%%EOF";

        File.WriteAllText(tempFile, pdfContent);
        return tempFile;
    }
}