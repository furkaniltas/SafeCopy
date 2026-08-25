using System.Text;
using System.IO;

using EksimSafeCopy.Core.Models;\nusing DocModel = global::EksimSafeCopy.Core.Models.DocModel;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Pdf;
using EksimSafeCopy.DocumentEngine.Ingestion.Docx;
using EksimSafeCopy.DocumentEngine.Ingestion.Xlsx;
using EksimSafeCopy.DocumentEngine.Ingestion.Txt;
using EksimSafeCopy.DocumentEngine.Ingestion.Udf;
using EksimSafeCopy.DocumentEngine.Ingestion.Image;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DE = global::EksimSafeCopy.DocumentEngine.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Security;

public class OriginalImmutabilityTests
{
    private readonly IDocumentEngine _documentEngine;
    private readonly IFileSystem _fileSystem;

    public OriginalImmutabilityTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, global::EksimSafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DE.DocumentEngine>();
        
        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
        _fileSystem = provider.GetRequiredService<IFileSystem>();
    }

    [Fact]
    public void Load_Pdf_OriginalFileUnchanged()
    {
        var tempFile = CreateTempPdf();
        var originalHash = ComputeFileHash(tempFile);
        var originalLastWrite = File.GetLastWriteTimeUtc(tempFile);
        var originalSize = new FileInfo(tempFile).Length;

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            // Verify original file unchanged
            var newHash = ComputeFileHash(tempFile);
            var newLastWrite = File.GetLastWriteTimeUtc(tempFile);
            var newSize = new FileInfo(tempFile).Length;

            newHash.Should().Be(originalHash, "File content should not change");
            newLastWrite.Should().Be(originalLastWrite, "Last write time should not change");
            newSize.Should().Be(originalSize, "File size should not change");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Docx_OriginalFileUnchanged()
    {
        var tempFile = CreateTempDocx();
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Xlsx_OriginalFileUnchanged()
    {
        var tempFile = CreateTempXlsx();
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Txt_OriginalFileUnchanged()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Test content with Ahmet Yılmaz");
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Image_OriginalFileUnchanged()
    {
        var tempFile = CreateTempPng();
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_Udf_OriginalFileUnchanged()
    {
        var tempFile = CreateTempUdf();
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ConcurrentAccess_OriginalFileReadable()
    {
        var tempFile = CreateTempPdf();
        var originalHash = ComputeFileHash(tempFile);

        try
        {
            // Open file for reading concurrently
            using var concurrentStream = File.OpenRead(tempFile);
            var buffer = new byte[10];
            concurrentStream.Read(buffer, 0, 10);

            // Now load through document engine
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            // Verify original still intact
            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ReadOnlyFile_OriginalFileUnchanged()
    {
        var tempFile = CreateTempPdf();
        var originalHash = ComputeFileHash(tempFile);
        
        // Make file read-only
        File.SetAttributes(tempFile, FileAttributes.ReadOnly);

        try
        {
            var result = _documentEngine.Load(tempFile);
            result.IsSuccess.Should().BeTrue();

            var newHash = ComputeFileHash(tempFile);
            newHash.Should().Be(originalHash);
        }
        finally
        {
            File.SetAttributes(tempFile, FileAttributes.Normal);
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
        using (var DocModel = WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.DocModel = new DocModel(new Body(new Paragraph(new Run(new Text("Test")))));
        }
        return tempFile;
    }

    private string CreateTempXlsx()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using (var DocModel = SpreadsheetDocument.Create(tempFile, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" });
            workbookPart.Workbook.Save();
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

    private string CreateTempUdf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.udf");
        using (var archive = ZipFile.Open(tempFile, ZipArchiveMode.Create))
        {
            var contentEntry = archive.CreateEntry("content.xml");
            using (var entryStream = contentEntry.Open())
            using (var writer = new StreamWriter(entryStream, System.Text.Encoding.UTF8))
            {
                writer.Write("<document><body><paragraph>Test</paragraph></body></document>");
            }
        }
        return tempFile;
    }

    private string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}






