using System.Text;
using System.IO;

using EksimSafeCopy.Core.Models;\nusing DocModel = global::EksimSafeCopy.Core.Models.DocModel;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Ingestion.Xlsx;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DE = global::EksimSafeCopy.DocumentEngine.Ingestion;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.DocumentEngine.Tests.Ingestion;

public class XlsxIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

    public XlsxIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IDocumentIngestor, XlsxDocumentIngestor>();
        services.AddSingleton<IDocumentEngine, DocEngine>();
        
        var provider = services.BuildServiceProvider();
        _documentEngine = provider.GetRequiredService<IDocumentEngine>();
    }

    [Fact]
    public void DetectFormat_XlsxFile_ReturnsXlsx()
    {
        var tempFile = CreateTempXlsx();

        try
        {
            var result = _documentEngine.DetectFormat(tempFile);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(DocumentFormat.Xlsx);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ValidXlsx_ReturnsDocument()
    {
        var tempFile = CreateTempXlsx();

        try
        {
            var result = _documentEngine.Load(tempFile);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Format.Should().Be(DocumentFormat.Xlsx);
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
        var result = _documentEngine.Load("nonexistent.xlsx");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
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
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1"
            });

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

            // Add header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(
                new Cell { CellReference = "A1", CellValue = new CellValue("Name"), DataType = CellValues.String },
                new Cell { CellReference = "B1", CellValue = new CellValue("TC Kimlik"), DataType = CellValues.String },
                new Cell { CellReference = "C1", CellValue = new CellValue("Email"), DataType = CellValues.String }
            );
            sheetData.Append(headerRow);

            // Add data rows
            var dataRow1 = new Row { RowIndex = 2 };
            dataRow1.Append(
                new Cell { CellReference = "A2", CellValue = new CellValue("Ahmet Yılmaz"), DataType = CellValues.String },
                new Cell { CellReference = "B2", CellValue = new CellValue("11111111111"), DataType = CellValues.String },
                new Cell { CellReference = "C2", CellValue = new CellValue("ahmet@example.com"), DataType = CellValues.String }
            );
            sheetData.Append(dataRow1);

            var dataRow2 = new Row { RowIndex = 3 };
            dataRow2.Append(
                new Cell { CellReference = "A3", CellValue = new CellValue("Ayşe Demir"), DataType = CellValues.String },
                new Cell { CellReference = "B3", CellValue = new CellValue("22222222222"), DataType = CellValues.String },
                new Cell { CellReference = "C3", CellValue = new CellValue("ayse@example.com"), DataType = CellValues.String }
            );
            sheetData.Append(dataRow2);

            workbookPart.Workbook.Save();
        }

        return tempFile;
    }
}






