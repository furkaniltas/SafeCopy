namespace EksimSafeCopy.DocumentEngine.Ingestion.Pdf;
using global::EksimSafeCopy.Core.Abstractions;
using global::EksimSafeCopy.Core.Models;
using global::EksimSafeCopy.DocumentEngine.Ingestion;
using global::EksimSafeCopy.DocumentEngine.Security;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
public sealed class PdfDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Pdf;
    public override string[] SupportedExtensions => new[] { ".pdf" };
    private readonly IOcrEngine? _ocrEngine;
    public PdfDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem, IOcrEngine? ocrEngine = null)
        : base(securityValidator, fileSystem) { _ocrEngine = ocrEngine; }
    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var pdfDocument = PdfDocument.Open(filePath);
            return ProcessPdfDocument(pdfDocument, filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"PDF ingestion failed: {ex.Message}", ex));
        }
    }
    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // PdfPig requires a file path, so we need to copy to a temp file
            // For now, we'll throw NotSupportedException for stream ingestion
            // In production, we'd copy to a temp file first
            return Result<Document>.Failure(Error.FormatError("Stream ingestion not supported for PDF - use file path"));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"PDF stream ingestion failed: {ex.Message}", ex));
        }
    }
    private Result<Document> ProcessPdfDocument(PdfDocument pdfDocument, string filePath, CancellationToken cancellationToken)
    {
        var pages = new List<DocumentPage>();
        var fileName = Path.GetFileName(filePath);
        try
        {
            foreach (var pdfPage in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = ProcessPdfPage(pdfPage);
                pages.Add(page);
            }
            var document = new Document
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = DocumentFormat.Pdf,
                Pages = pages,
                Metadata = ExtractMetadata(pdfDocument)
            };
            return Result<Document>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"PDF processing failed: {ex.Message}", ex));
        }
    }
    private DocumentPage ProcessPdfPage(Page pdfPage)
    {
        var pageText = pdfPage.Text;
        // Extract words with positions
        var words = pdfPage.GetWords().ToList();
        var pageWidth = pdfPage.Width;
        var pageHeight = pdfPage.Height;
        var textBlocksFromWords = CreateTextBlocksFromWords(words, pageWidth, pageHeight);
        var isScanned = words.Count == 0 && pdfPage.GetImages().Any(); // Heuristic: no text but has images
        var images = ExtractImages(pdfPage).ToList();

        string finalText = pageText;
        var finalBlocks = textBlocksFromWords;
        OcrInfo? ocrInfo = null;

        // OCR integration: when scanned, call OCR on embedded images
        if (isScanned && _ocrEngine != null && _ocrEngine.IsAvailable && images.Count > 0)
        {
            try
            {
                var ocrTextParts = new List<string>();
                var ocrBlocks = new List<TextBlock>();
                double accumulatedY = 0;
                foreach (var imgRef in images)
                {
                    if (imgRef.Data == null || imgRef.Data.Length == 0) continue;
                    // Use OCR with Turkish priority, 30s timeout via internal engine
                    var ocrResult = _ocrEngine.Recognize(imgRef.Data, "tr");
                    if (ocrResult.IsSuccess && !string.IsNullOrWhiteSpace(ocrResult.Value.Text))
                    {
                        ocrTextParts.Add(ocrResult.Value.Text);
                        var mapped = MapOcrResultToTextBlocks(ocrResult.Value, pdfPage.Number, pageWidth, pageHeight, ref accumulatedY);
                        ocrBlocks.AddRange(mapped);
                        ocrInfo = new OcrInfo
                        {
                            Engine = _ocrEngine.EngineName,
                            Language = ocrResult.Value.Language,
                            AverageConfidence = ocrResult.Value.Confidence,
                            ProcessedAt = ocrResult.Value.ProcessedAt,
                            Duration = ocrResult.Value.Duration
                        };
                    }
                }
                if (ocrBlocks.Count > 0)
                {
                    finalText = string.Join("\n", ocrTextParts);
                    finalBlocks = ocrBlocks;
                }
            }
            catch
            {
                // OCR failure fallback: keep original (empty) text, do not crash
            }
        }

        var page = new DocumentPage
        {
            PageNumber = pdfPage.Number,
            Width = Math.Max(0, (int)pdfPage.Width),
            Height = Math.Max(0, (int)pdfPage.Height),
            DpiX = 72, // PDF default
            DpiY = 72,
            Text = finalText,
            TextBlocks = finalBlocks,
            IsScanned = isScanned,
            Images = images,
            OcrInfo = ocrInfo,
            CoordinateSystem = new CoordinateSystem(Math.Max(1, (int)pageWidth), Math.Max(1, (int)pageHeight), CoordinateOrigin.TopLeft, CoordinateUnit.Points)
        };
        return page;
    }

    private static IReadOnlyList<TextBlock> MapOcrResultToTextBlocks(OcrResult ocrResult, int pageNumber, double pageWidth, double pageHeight, ref double accumulatedY)
    {
        var blocks = new List<TextBlock>();
        double safeW = Math.Max(1, pageWidth);
        double safeH = Math.Max(1, pageHeight);
        // Prefer Lines if available, else Words
        if (ocrResult.Lines.Count > 0)
        {
            int order = 0;
            foreach (var line in ocrResult.Lines)
            {
                var bb = line.BoundingBox.IsEmpty ? new BoundingBox(0, accumulatedY, safeW, 20, safeW, safeH) : line.BoundingBox;
                // Clamp to page bounds
                var clamped = new BoundingBox(Math.Max(0, bb.X), Math.Max(0, bb.Y), Math.Min(bb.Width, safeW), Math.Min(bb.Height, safeH), safeW, safeH);
                var block = new TextBlock
                {
                    Text = line.Text,
                    BoundingBox = clamped,
                    PageNumber = pageNumber,
                    OrderIndex = order++,
                    Type = TextBlockType.Paragraph,
                    Direction = TextDirection.LeftToRight,
                    Properties = new Dictionary<string, object> { ["DetectionSource"] = "Ocr", ["Confidence"] = line.Confidence },
                    Spans = line.Words.Select((w, idx) => new TextSpan
                    {
                        StartIndex = idx == 0 ? 0 : line.Words.Take(idx).Sum(x => x.Text.Length + 1),
                        Length = w.Text.Length,
                        Text = w.Text,
                        BoundingBox = w.BoundingBox,
                        Properties = new Dictionary<string, object> { ["Confidence"] = w.Confidence }
                    }).ToList().AsReadOnly()
                };
                blocks.Add(block);
                accumulatedY += bb.Height + 5;
            }
            return blocks;
        }
        if (ocrResult.Words.Count > 0)
        {
            var text = ocrResult.Text;
            var bb = new BoundingBox(0, accumulatedY, safeW, 20, safeW, safeH);
            var block = new TextBlock
            {
                Text = text,
                BoundingBox = bb,
                PageNumber = pageNumber,
                OrderIndex = 0,
                Type = TextBlockType.Paragraph,
                Direction = TextDirection.LeftToRight,
                Properties = new Dictionary<string, object> { ["DetectionSource"] = "Ocr", ["Confidence"] = ocrResult.Confidence }
            };
            blocks.Add(block);
            return blocks;
        }
        // Fallback: single block from Text
        if (!string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            var bb = new BoundingBox(0, accumulatedY, safeW, 20, safeW, safeH);
            blocks.Add(new TextBlock
            {
                Text = ocrResult.Text,
                BoundingBox = bb,
                PageNumber = pageNumber,
                OrderIndex = 0,
                Type = TextBlockType.Paragraph,
                Direction = TextDirection.LeftToRight,
                Properties = new Dictionary<string, object> { ["DetectionSource"] = "Ocr", ["Confidence"] = ocrResult.Confidence }
            });
            accumulatedY += 25;
        }
        return blocks;
    }
    private IReadOnlyList<TextBlock> CreateTextBlocksFromWords(IReadOnlyList<Word> words, double pageWidth, double pageHeight)
    {
        if (words.Count == 0)
            return Array.Empty<TextBlock>();
        var blocks = new List<TextBlock>();
        var currentBlock = new List<Word>();
        double lastBottom = -1;
        const double lineThreshold = 5.0; // Points
        foreach (var word in words.OrderBy(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            if (lastBottom >= 0 && word.BoundingBox.Top - lastBottom > lineThreshold)
            {
                if (currentBlock.Count > 0)
                {
                    blocks.Add(CreateTextBlock(currentBlock, pageWidth, pageHeight));
                    currentBlock.Clear();
                }
            }
            currentBlock.Add(word);
            lastBottom = word.BoundingBox.Bottom;
        }
        if (currentBlock.Count > 0)
        {
            blocks.Add(CreateTextBlock(currentBlock, pageWidth, pageHeight));
        }
        // Assign order indices
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            blocks[i] = new TextBlock
            {
                Id = block.Id,
                BoundingBox = block.BoundingBox,
                Text = block.Text,
                Spans = block.Spans,
                Type = block.Type,
                Direction = block.Direction,
                PageNumber = block.PageNumber,
                OrderIndex = i,
                Properties = block.Properties
            };
        }
        return blocks;
    }
    private TextBlock CreateTextBlock(List<Word> words, double pageWidth, double pageHeight)
    {
        if (words.Count == 0)
            return new TextBlock { Text = string.Empty, BoundingBox = BoundingBox.Empty };
        var text = string.Join(" ", words.Select(w => w.Text));
        var minX = words.Min(w => w.BoundingBox.Left);
        var minY = words.Min(w => w.BoundingBox.Top);
        var maxX = words.Max(w => w.BoundingBox.Right);
        var maxY = words.Max(w => w.BoundingBox.Bottom);
        // Ensure valid dimensions
        var blockWidth = Math.Max(0, maxX - minX);
        var blockHeight = Math.Max(0, maxY - minY);
        var safePageWidth = Math.Max(1, pageWidth);
        var safePageHeight = Math.Max(1, pageHeight);
        var spans = words.Select(w => new TextSpan
        {
            StartIndex = 0, // Will be calculated relative to block
            Length = w.Text.Length,
            Text = w.Text,
            BoundingBox = new BoundingBox(
                Math.Max(0, w.BoundingBox.Left),
                Math.Max(0, w.BoundingBox.Top),
                Math.Max(0, w.BoundingBox.Width),
                Math.Max(0, w.BoundingBox.Height),
                safePageWidth,
                safePageHeight),
            Font = ExtractFontInfo(w),
            BlockId = 0 // Will be set by caller
        }).ToList();
        // Calculate relative start indices
        int currentIndex = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            spans[i] = new TextSpan
            {
                StartIndex = currentIndex,
                Length = span.Length,
                Text = span.Text,
                BoundingBox = span.BoundingBox,
                Font = span.Font,
                Style = span.Style,
                BlockId = span.BlockId,
                Properties = span.Properties
            };
            currentIndex += span.Length;
            if (i < spans.Count - 1) currentIndex++; // Space
        }
        var block = new TextBlock
        {
            Text = string.Join(" ", words.Select(w => w.Text)),
            BoundingBox = new BoundingBox(minX, minY, blockWidth, blockHeight, safePageWidth, safePageHeight),
            Spans = spans,
            Type = TextBlockType.Paragraph,
            Direction = TextDirection.LeftToRight
        };
        return block;
    }
    private FontInfo? ExtractFontInfo(Word word)
{
    try
    {
        if (word.FontName == null) return null;
        return new FontInfo
        {
            Name = word.FontName,
            Size = 12, // PdfPig 1.7 doesn't expose FontSize directly on Word
            Bold = word.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) ?? false,
            Italic = word.FontName?.Contains("Italic", StringComparison.OrdinalIgnoreCase) ?? false,
            Color = "#000000"
        };
    }
    catch
    {
        return null;
    }
}
    private IEnumerable<ImageReference> ExtractImages(Page pdfPage)
    {
        foreach (var image in pdfPage.GetImages())
        {
            ImageReference? imageRef = null;
            try
            {
                var imageBytes = image.RawBytes.ToArray();
                var bbox = image.Bounds;
                var hasValidBounds = bbox.Width > 0 && bbox.Height > 0;
                var imgRef = new ImageReference
                {
                    Width = hasValidBounds ? (int)Math.Max(0, bbox.Width) : 0,
                    Height = hasValidBounds ? (int)Math.Max(0, bbox.Height) : 0,
                    Format = "unknown",
                    Data = imageBytes,
                    BoundingBox = hasValidBounds
                        ? new BoundingBox(0, 0, Math.Max(0, bbox.Width), Math.Max(0, bbox.Height), Math.Max(0, bbox.Width), Math.Max(0, bbox.Height))
                        : BoundingBox.Empty
                };
                imageRef = imgRef;
            }
            catch
            {
                // Skip failed image extraction
            }
            if (imageRef != null)
            {
                yield return imageRef;
            }
        }
    }
    private DocumentMetadata ExtractMetadata(PdfDocument pdfDocument)
    {
        var info = pdfDocument.Information;
        DateTime? created = null;
        if (DateTime.TryParse(info.CreationDate, out var parsedCreated))
        {
            created = parsedCreated.ToUniversalTime();
        }
        return new DocumentMetadata
        {
            Title = info.Title,
            Author = info.Author,
            Subject = info.Subject,
            Keywords = info.Keywords,
            Creator = info.Creator,
            Producer = info.Producer,
            Created = created,
            // ModificationDate not available in PdfPig 1.7 DocumentInformation
            CustomProperties = new Dictionary<string, string>()
        };
    }
}
