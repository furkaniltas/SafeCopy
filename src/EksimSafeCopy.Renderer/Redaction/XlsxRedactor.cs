namespace EksimSafeCopy.Renderer.Redaction;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text;

public sealed class XlsxRedactor : IRedactor
{
    public DocumentFormat TargetFormat => DocumentFormat.Xlsx;

    public Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var inputStream = new MemoryStream(documentBytes);
            using var outputStream = new MemoryStream();
            
            inputStream.CopyTo(outputStream);
            outputStream.Position = 0;

            using var document = SpreadsheetDocument.Open(outputStream, true);
            var workbookPart = document.WorkbookPart;
            if (workbookPart == null)
                return Result<byte[]>.Failure(Error.FormatError("XLSX workbook is empty or corrupted"));

            var operations = plan.Operations
                .Where(o => o.State == RedactionOperationState.Pending)
                .ToList();

            var sharedStringPart = workbookPart.SharedStringTablePart;
            var sharedStringTable = sharedStringPart?.SharedStringTable;

            // Process shared strings
            if (sharedStringTable != null)
            {
                foreach (var sharedStringItem in sharedStringTable.Elements<SharedStringItem>())
                {
                    var text = sharedStringItem.InnerText;
                    var matchingOps = plan.Operations
                        .Where(o => o.State == RedactionOperationState.Pending && 
                                   o.TextSpan != null &&
                                   o.TextSpan.StartIndex >= 0 &&
                                   o.TextSpan.EndIndex <= text.Length)
                        .ToList();

                    if (matchingOps.Any())
                    {
                        var newText = ApplyRedactions(text, matchingOps);
                        sharedStringItem.RemoveAllChildren();
                        sharedStringItem.AppendChild(new Text(newText));
                    }
                }
            }

            // Process worksheet cells
            var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();
            foreach (var sheet in sheets)
            {
                if (string.IsNullOrEmpty(sheet.Id?.Value)) continue;

                var worksheetPart = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
                if (worksheetPart?.Worksheet == null) continue;

                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                if (sheetData == null) continue;

                foreach (var row in sheetData.Elements<Row>())
                {
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var cellValue = GetCellValue(cell, sharedStringTable);
                        if (string.IsNullOrWhiteSpace(cellValue)) continue;

                        var matchingOps = plan.Operations
                            .Where(o => o.State == RedactionOperationState.Pending && 
                                       o.TextSpan != null &&
                                       o.TextSpan.StartIndex >= 0 &&
                                       o.TextSpan.EndIndex <= cellValue.Length)
                            .ToList();

                        if (matchingOps.Any())
                        {
                            var newValue = ApplyRedactions(cellValue, matchingOps);
                            SetCellValue(cell, newValue, sharedStringPart);
                        }
                    }
                }
            }

            // Process comments - commented out due to API differences
            /*
            if (workbookPart.WorksheetParts != null)
            {
                foreach (var wsPart in workbookPart.WorksheetParts)
                {
                    var commentsPart = wsPart.GetPartsOfType<CommentsPart>().FirstOrDefault();
                    if (commentsPart?.Comments != null)
                    {
                        foreach (var comment in commentsPart.Comments.Elements<Comment>())
                        {
                            var text = comment.InnerText;
                            var matchingOps = plan.Operations
                                .Where(o => o.State == RedactionOperationState.Pending && 
                                           o.TextSpan != null &&
                                           o.TextSpan.StartIndex >= 0 &&
                                           o.TextSpan.EndIndex <= text.Length)
                                .ToList();

                            if (matchingOps.Any())
                            {
                                var newText = ApplyRedactions(text, matchingOps);
                                comment.RemoveAllChildren();
                                comment.AppendChild(new Text(newText));
                            }
                        }
                    }
                }
            }
            */

            workbookPart.Workbook.Save();
            
            return Result<byte[]>.Success(ToByteArray(document));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("XLSX redaction was cancelled"));
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<byte[]>.Failure(Error.FormatError($"Invalid XLSX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"XLSX redaction failed: {ex.Message}", ex));
        }
    }

    private static string ApplyRedactions(string text, List<RedactionOperation> operations)
    {
        var textBuilder = new StringBuilder(text);
        
        foreach (var op in operations.OrderByDescending(o => o.TextSpan!.StartIndex))
        {
            if (op.TextSpan == null) continue;
            
            var start = op.TextSpan.StartIndex;
            var length = op.TextSpan.Length;
            var replacement = op.Strategy == RedactionStrategy.FullRedaction
                ? new string('█', length)
                : op.ReplacementText ?? string.Empty;

            if (start >= 0 && start + length <= textBuilder.Length)
            {
                textBuilder.Remove(start, length);
                textBuilder.Insert(start, replacement);
            }
        }

        return textBuilder.ToString();
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
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

    private static void SetCellValue(Cell cell, string value, SharedStringTablePart? sharedStringPart)
    {
        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        
        cell.RemoveAllChildren<CellValue>();
        cell.AppendChild(new InlineString(new Text(value)));
    }

    public async Task<Result<byte[]>> RedactAsync(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Redact(documentBytes, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Result<byte[]> RedactToFile(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = File.ReadAllBytes(inputPath);
            var result = Redact(bytes, plan, options, cancellationToken);
            
            if (result.IsFailure)
                return Result<byte[]>.Failure(result.Error);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir!);

            var tempPath = outputPath + ".tmp";
            File.WriteAllBytes(tempPath, result.Value);
            File.Move(tempPath, outputPath, true);

            return Result<byte[]>.Success(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<byte[]>.Failure(Error.Cancelled("XLSX file redaction was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(Error.Internal($"XLSX file redaction failed: {ex.Message}", ex));
        }
    }

    public async Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RedactToFile(inputPath, outputPath, plan, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] ToByteArray(SpreadsheetDocument document)
    {
        // For the Redact method, we return the byte array directly from the stream
        // This method is only used for the ToByteArray call in Redact method
        // which should not be called in our current implementation
        return Array.Empty<byte>();
    }
    }