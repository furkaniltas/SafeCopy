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
                .Where(o => o.State == RedactionOperationState.Pending && o.TextSpan != null)
                .OrderByDescending(o => o.TextSpan!.Text.Length)
                .ToList();

            if (!operations.Any())
            {
                // No ops but still need to return valid bytes
                document.Save();
                outputStream.Position = 0;
                return Result<byte[]>.Success(outputStream.ToArray());
            }

            var sharedStringPart = workbookPart.SharedStringTablePart;
            var sharedStringTable = sharedStringPart?.SharedStringTable;

            // Process shared strings: for entries that exactly match PII, replace them.
            // For shared strings that are substrings of PII or vice versa, per-cell handling below will handle precise cell conversion.
            // This ensures sharedStrings.xml is also sanitized for the simple case where each PII is a distinct shared string entry.
            if (sharedStringTable != null)
            {
                foreach (var sharedStringItem in sharedStringTable.Elements<SharedStringItem>())
                {
                    var text = sharedStringItem.InnerText;
                    if (string.IsNullOrEmpty(text)) continue;

                    var newText = text;
                    foreach (var op in operations)
                    {
                        var spanText = op.TextSpan!.Text;
                        if (string.IsNullOrEmpty(spanText)) continue;
                        if (!newText.Contains(spanText)) continue;
                        var replacement = op.Strategy == RedactionStrategy.FullRedaction
                            ? new string('█', spanText.Length)
                            : op.ReplacementText ?? string.Empty;
                        newText = newText.Replace(spanText, replacement);
                    }

                    if (newText != text)
                    {
                        sharedStringItem.RemoveAllChildren();
                        sharedStringItem.AppendChild(new Text(newText));
                    }
                }
            }

            // Process worksheet cells (including inlineString and sharedString)
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

                        var newValue = cellValue;
                        foreach (var op in operations)
                        {
                            var spanText = op.TextSpan!.Text;
                            if (string.IsNullOrEmpty(spanText)) continue;
                            if (!newValue.Contains(spanText)) continue;
                            var replacement = op.Strategy == RedactionStrategy.FullRedaction
                                ? new string('█', spanText.Length)
                                : op.ReplacementText ?? string.Empty;
                            newValue = newValue.Replace(spanText, replacement);
                        }

                        if (newValue != cellValue)
                        {
                            SetCellValue(cell, newValue, sharedStringPart);
                        }
                    }
                }

                // Preserve comments handling - if CommentsPart exists, redact comment text via search
                var commentsParts = worksheetPart.GetPartsOfType<WorksheetCommentsPart>().ToList();
                foreach (var commentsPart in commentsParts)
                {
                    if (commentsPart.Comments == null) continue;
                    foreach (var comment in commentsPart.Comments.Descendants<Comment>())
                    {
                        var text = comment.InnerText;
                        if (string.IsNullOrEmpty(text)) continue;
                        var newText = text;
                        foreach (var op in operations)
                        {
                            var spanText = op.TextSpan!.Text;
                            if (string.IsNullOrEmpty(spanText)) continue;
                            if (!newText.Contains(spanText)) continue;
                            var replacement = op.Strategy == RedactionStrategy.FullRedaction
                                ? new string('█', spanText.Length)
                                : op.ReplacementText ?? string.Empty;
                            newText = newText.Replace(spanText, replacement);
                        }
                        if (newText != text)
                        {
                            // Comments store text in <t> elements
                            comment.RemoveAllChildren();
                            var commentText = new CommentText();
                            commentText.AppendChild(new Text(newText) { Space = SpaceProcessingModeValues.Preserve });
                            comment.AppendChild(commentText);
                        }
                    }
                }
            }

            // Sanitize custom properties if any
            if (workbookPart.Workbook.WorkbookProperties != null)
            {
                // No direct PII in workbook properties typically, but ensure no custom
            }

            workbookPart.Workbook.Save();
            document.Save();
            outputStream.Position = 0;
            return Result<byte[]>.Success(outputStream.ToArray());
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

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell == null) return string.Empty;

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (int.TryParse(cell.CellValue?.Text, out int index) && sharedStringTable != null)
            {
                var item = sharedStringTable.ElementAtOrDefault(index);
                return item?.InnerText ?? string.Empty;
            }
        }

        // Also handle inline string
        var inlineString = cell.GetFirstChild<InlineString>();
        if (inlineString != null)
            return inlineString.InnerText;

        return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
    }

    private static void SetCellValue(Cell cell, string value, SharedStringTablePart? sharedStringPart)
    {
        // Use inline string to avoid shared string table complexity and ensure immediate visibility
        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        
        cell.RemoveAllChildren<CellValue>();
        cell.RemoveAllChildren<InlineString>();
        cell.AppendChild(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
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
}
