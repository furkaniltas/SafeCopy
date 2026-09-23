namespace SafeCopy.DocumentEngine.Ingestion.Xlsx;

using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using global::SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.DocumentEngine.Security;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

public sealed class XlsxDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Xlsx;
    public override string[] SupportedExtensions => new[] { ".xlsx" };

    public XlsxDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }

    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(filePath, false);
            return ProcessXlsxDocument(spreadsheetDocument, filePath, cancellationToken);
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<Document>.Failure(Error.FormatError($"Invalid XLSX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"XLSX ingestion failed: {ex.Message}", ex));
        }
    }

    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(stream, false);
            return ProcessXlsxDocument(spreadsheetDocument, "stream.xlsx", cancellationToken);
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<Document>.Failure(Error.FormatError($"Invalid XLSX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"XLSX ingestion failed: {ex.Message}", ex));
        }
    }

    private Result<Document> ProcessXlsxDocument(SpreadsheetDocument spreadsheetDocument, string filePath, CancellationToken cancellationToken)
    {
        var pages = new List<DocumentPage>();
        var workbookPart = spreadsheetDocument.WorkbookPart;
        
        if (workbookPart == null)
            return Result<Document>.Failure(Error.FormatError("XLSX workbook is empty or corrupted"));

        try
        {
            var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();
            var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

            int pageNumber = 0;

            foreach (var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(sheet.Id?.Value))
                    continue;

                var worksheetPart = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
                if (worksheetPart?.Worksheet == null) continue;

                pageNumber++;
                var sheetName = sheet.Name?.Value ?? $"Sheet{pageNumber}";
                var page = ProcessWorksheet(worksheetPart, sharedStringTable, sheetName, pageNumber);
                pages.Add(page);
            }

            var document = new Document
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = DocumentFormat.Xlsx,
                Pages = pages,
                Metadata = ExtractMetadata(spreadsheetDocument)
            };

            return Result<Document>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"XLSX processing failed: {ex.Message}", ex));
        }
    }

    private DocumentPage ProcessWorksheet(WorksheetPart worksheetPart, SharedStringTable? sharedStringTable, string sheetName, int pageNumber)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.GetFirstChild<SheetData>();
        
        if (sheetData == null)
        {
            return new DocumentPage
            {
                PageNumber = pageNumber,
                Width = 1000,
                Height = 1000,
                DpiX = 96,
                DpiY = 96,
                Text = sheetName,
                IsScanned = false
            };
        }

        var textBlocks = new List<TextBlock>();
        var allText = new List<string>();
        int orderIndex = 0;

        foreach (var row in sheetData.Elements<Row>())
        {
            var rowTextBlocks = ProcessRow(row, sharedStringTable, orderIndex);
            textBlocks.AddRange(rowTextBlocks);
            orderIndex += rowTextBlocks.Count;
        }

        // Collect all text
        foreach (var block in textBlocks)
        {
            if (!string.IsNullOrWhiteSpace(block.Text))
                allText.Add(block.Text);
        }

        return new DocumentPage
        {
            PageNumber = pageNumber,
            Width = 1000,
            Height = 1000,
            DpiX = 96,
            DpiY = 96,
            Text = string.Join("\n", allText),
            TextBlocks = textBlocks,
            Images = Array.Empty<ImageReference>(),
            IsScanned = false
        };
    }

    private IReadOnlyList<TextBlock> ProcessRow(Row row, SharedStringTable? sharedStringTable, int startOrderIndex)
    {
        var blocks = new List<TextBlock>();
        int orderIndex = startOrderIndex;

        foreach (var cell in row.Elements<Cell>())
        {
            var cellValue = GetCellValue(cell, sharedStringTable);
            if (string.IsNullOrWhiteSpace(cellValue)) continue;

            var cellAddress = cell.CellReference?.Value ?? string.Empty;
            var currentOrder = orderIndex++;
            var block = new TextBlock
            {
                Text = $"{cellAddress}: {cellValue}",
                Type = TextBlockType.Table,
                Direction = TextDirection.LeftToRight,
                OrderIndex = currentOrder,
                PageNumber = 1,
                BoundingBox = BoundingBox.Empty,
                Properties = new Dictionary<string, object> { ["CellReference"] = cellAddress },
                Spans = new List<TextSpan>
                {
                    new TextSpan
                    {
                        StartIndex = 0,
                        Length = cellAddress.Length + 2,
                        Text = $"{cellAddress}: ",
                        BoundingBox = BoundingBox.Empty,
                        BlockId = currentOrder,
                        Properties = new Dictionary<string, object> { ["CellReference"] = cellAddress, ["IsAddress"] = true }
                    },
                    new TextSpan
                    {
                        StartIndex = cellAddress.Length + 2,
                        Length = cellValue.Length,
                        Text = cellValue,
                        BoundingBox = BoundingBox.Empty,
                        BlockId = currentOrder,
                        Properties = new Dictionary<string, object> { ["CellReference"] = cellAddress, ["IsAddress"] = false }
                    }
                }
            };

            blocks.Add(block);
        }

        return blocks;
    }

    private string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell == null) return string.Empty;

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (int.TryParse(cell.CellValue?.Text, out int index) && sharedStringTable != null)
            {
                var item = sharedStringTable.ElementAt(index);
                return item.InnerText;
            }
        }

        return cell.CellValue?.Text ?? string.Empty;
    }

    private DocumentMetadata ExtractMetadata(SpreadsheetDocument spreadsheetDocument)
    {
        var coreProps = spreadsheetDocument.PackageProperties;
        return new DocumentMetadata
        {
            Title = coreProps.Title,
            Author = coreProps.Creator,
            Subject = coreProps.Subject,
            Keywords = coreProps.Keywords,
            Creator = coreProps.Creator,
            Producer = coreProps.LastModifiedBy,
            Created = coreProps.Created?.ToUniversalTime(),
            Modified = coreProps.Modified?.ToUniversalTime(),
            CustomProperties = new Dictionary<string, string>()
        };
    }
}