#pragma warning disable CS8602,CS8604
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.DocumentEngine.Ingestion;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EksimSafeCopy.Detectors;

public class TesisatReproTests
{
    [Fact]
    public void Repro_TesisatNumarasi_ShouldBeInstallationNotPersonName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tesisat_{Guid.NewGuid():N}.xlsx");
        CreateXlsx(path);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new DocumentSecurityOptions());
            services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<IDocumentIngestor, EksimSafeCopy.DocumentEngine.Ingestion.Xlsx.XlsxDocumentIngestor>();
            services.AddSingleton<IDocumentEngine, EksimSafeCopy.DocumentEngine.Ingestion.DocumentEngine>();
            services.AddDetectors();
            var provider = services.BuildServiceProvider();
            var engine = provider.GetRequiredService<IDocumentEngine>();
            var detector = provider.GetRequiredService<IDetectionEngine>();
            var load = engine.Load(path);
            load.IsSuccess.Should().BeTrue();
            var doc = load.Value;
            var det = detector.Detect(doc);
            det.IsSuccess.Should().BeTrue();
            // Log all detections
            var all = string.Join("\n", det.Value.Select(d => $"{d.Type}='{d.Value}' Span='{d.TextSpan?.Text}'"));
            System.IO.File.WriteAllText(Path.Combine(Path.GetTempPath(), "tesisat_detections.txt"), all + "\nDocText: " + doc.Pages[0].Text);
            // Tesisat Numarası should NOT be detected as FullName
            det.Value.Should().NotContain(d => d.Type == DetectionType.FullName && d.Value == "Tesisat Numarası", "Tesisat Numarası is a label, not a person name");
            // It should be detected as InstallationNumber if anything, or not at all
            // The actual installation number at A2 should be detected
            det.Value.Should().Contain(d => d.Type == DetectionType.TesisatNo && d.Value == "12222222");
            // Furkan İltaş should be FullName
            det.Value.Should().Contain(d => d.Type == DetectionType.FullName && d.Value.Contains("Furkan"));
            // 60908186000 should be TcKimlikNo (with our fallback)
            det.Value.Should().Contain(d => d.Type == DetectionType.TcKimlikNo && d.Value == "60908186000");
            // Phone should be Phone
            det.Value.Should().Contain(d => d.Type == DetectionType.Phone);
            // Address should be Address
            det.Value.Should().Contain(d => d.Type == DetectionType.Address);
        }
        finally { File.Delete(path); }
    }

    private void CreateXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        wbPart.Workbook = new Workbook();
        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        wsPart.Worksheet = new Worksheet(new SheetData());
        var sheets = wbPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Sheet1" });
        var sd = wsPart.Worksheet.GetFirstChild<SheetData>();
        var r1 = new Row { RowIndex = 1 };
        r1.Append(new Cell { CellReference = "A1", CellValue = new CellValue("Tesisat Numarası"), DataType = CellValues.String });
        r1.Append(new Cell { CellReference = "B1", CellValue = new CellValue("AD Soyad"), DataType = CellValues.String });
        r1.Append(new Cell { CellReference = "C1", CellValue = new CellValue("TC"), DataType = CellValues.String });
        r1.Append(new Cell { CellReference = "D1", CellValue = new CellValue("Telefon"), DataType = CellValues.String });
        r1.Append(new Cell { CellReference = "E1", CellValue = new CellValue("Adres"), DataType = CellValues.String });
        sd.Append(r1);
        var r2 = new Row { RowIndex = 2 };
        r2.Append(new Cell { CellReference = "A2", CellValue = new CellValue("12222222"), DataType = CellValues.String });
        r2.Append(new Cell { CellReference = "B2", CellValue = new CellValue("Furkan İltaş"), DataType = CellValues.String });
        r2.Append(new Cell { CellReference = "C2", CellValue = new CellValue("60908186000"), DataType = CellValues.String });
        r2.Append(new Cell { CellReference = "D2", CellValue = new CellValue("5433324320"), DataType = CellValues.String });
        r2.Append(new Cell { CellReference = "E2", CellValue = new CellValue("Bağcılar mah koop cad no:11"), DataType = CellValues.String });
        sd.Append(r2);
        wbPart.Workbook.Save();
    }
}
