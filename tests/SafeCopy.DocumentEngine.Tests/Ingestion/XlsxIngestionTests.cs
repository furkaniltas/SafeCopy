using System.IO;
using System.Text;
using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using DocModel = global::SafeCopy.Core.Models.Document;
using SafeCopy.DocumentEngine.Ingestion;
using SafeCopy.DocumentEngine.Ingestion.Xlsx;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using DocEngine = global::SafeCopy.DocumentEngine.Ingestion.DocumentEngine;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;
using Ox = DocumentFormat.OpenXml;
using OxPackaging = DocumentFormat.OpenXml.Packaging;
using OxSpreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SafeCopy.DocumentEngine.Tests.Ingestion;

public class XlsxIngestionTests
{
    private readonly IDocumentEngine _documentEngine;

public XlsxIngestionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
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
            result.Value.Should().Be(DF.Xlsx);
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
            result.Value!.Format.Should().Be(DF.Xlsx);
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

        using (var spreadsheetDoc = OxPackaging.SpreadsheetDocument.Create(tempFile, Ox.SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheetDoc.AddWorkbookPart();
            workbookPart.Workbook = new OxSpreadsheet.Workbook();

            var worksheetPart = workbookPart.AddNewPart<OxPackaging.WorksheetPart>();
            worksheetPart.Worksheet = new OxSpreadsheet.Worksheet(new OxSpreadsheet.SheetData());

            var sheets = workbookPart.Workbook.AppendChild(new OxSpreadsheet.Sheets());
            sheets.Append(new OxSpreadsheet.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1"
            });

            var sheetData = worksheetPart.Worksheet.GetFirstChild<OxSpreadsheet.SheetData>()!;

            // Add header row
            var headerRow = new OxSpreadsheet.Row { RowIndex = 1 };
            headerRow.Append(
                new OxSpreadsheet.Cell { CellReference = "A1", CellValue = new OxSpreadsheet.CellValue("Name"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "B1", CellValue = new OxSpreadsheet.CellValue("TC Kimlik"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "C1", CellValue = new OxSpreadsheet.CellValue("Email"), DataType = OxSpreadsheet.CellValues.String }
            );
            sheetData.Append(headerRow);

            // Add data rows
            var dataRow1 = new OxSpreadsheet.Row { RowIndex = 2 };
            dataRow1.Append(
                new OxSpreadsheet.Cell { CellReference = "A2", CellValue = new OxSpreadsheet.CellValue("Ahmet Yılmaz"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "B2", CellValue = new OxSpreadsheet.CellValue("11111111111"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "C2", CellValue = new OxSpreadsheet.CellValue("ahmet@example.com"), DataType = OxSpreadsheet.CellValues.String }
            );
            sheetData.Append(dataRow1);

            var dataRow2 = new OxSpreadsheet.Row { RowIndex = 3 };
            dataRow2.Append(
                new OxSpreadsheet.Cell { CellReference = "A3", CellValue = new OxSpreadsheet.CellValue("Ayşe Demir"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "B3", CellValue = new OxSpreadsheet.CellValue("22222222222"), DataType = OxSpreadsheet.CellValues.String },
                new OxSpreadsheet.Cell { CellReference = "C3", CellValue = new OxSpreadsheet.CellValue("ayse@example.com"), DataType = OxSpreadsheet.CellValues.String }
            );
            sheetData.Append(dataRow2);

            workbookPart.Workbook.Save();
        }

        return tempFile;
    }
}






