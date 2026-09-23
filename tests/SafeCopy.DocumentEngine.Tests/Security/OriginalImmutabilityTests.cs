using System.IO;
using System.IO.Compression;
using System.Text;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using DocModel = global::SafeCopy.Core.Models.Document;
using SafeCopy.DocumentEngine.Ingestion;
using SafeCopy.DocumentEngine.Ingestion.Pdf;
using SafeCopy.DocumentEngine.Ingestion.Docx;
using SafeCopy.DocumentEngine.Ingestion.Xlsx;
using SafeCopy.DocumentEngine.Ingestion.Txt;
using SafeCopy.DocumentEngine.Ingestion.Udf;
using SafeCopy.DocumentEngine.Ingestion.Image;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using DocEngine = global::SafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using Ox = DocumentFormat.OpenXml;
using OxPackaging = DocumentFormat.OpenXml.Packaging;
using OxWord = DocumentFormat.OpenXml.Wordprocessing;
using OxSpreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SafeCopy.DocumentEngine.Tests.Security;

public class OriginalImmutabilityTests
{
    private readonly IDocumentEngine _documentEngine;
    private readonly IFileSystem _fileSystem;

public OriginalImmutabilityTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, global::SafeCopy.Infrastructure.FileSystem>();
        services.AddSingleton<IDocumentIngestor, PdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, DocxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, TxtDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, UdfDocumentIngestor>();
        services.AddSingleton<IDocumentIngestor, ImageDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();
        
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
            concurrentStream.ReadExactly(buffer);

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
        using (var wordDoc = OxPackaging.WordprocessingDocument.Create(tempFile, Ox.WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new OxWord.Document(new OxWord.Body(new OxWord.Paragraph(new OxWord.Run(new OxWord.Text("Test")))));
        }
        return tempFile;
    }

    private string CreateTempXlsx()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using (var spreadsheetDoc = OxPackaging.SpreadsheetDocument.Create(tempFile, Ox.SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheetDoc.AddWorkbookPart();
            workbookPart.Workbook = new OxSpreadsheet.Workbook();
            var worksheetPart = workbookPart.AddNewPart<OxPackaging.WorksheetPart>();
            worksheetPart.Worksheet = new OxSpreadsheet.Worksheet(new OxSpreadsheet.SheetData());
            var sheets = workbookPart.Workbook.AppendChild(new OxSpreadsheet.Sheets());
            sheets.Append(new OxSpreadsheet.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" });
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






