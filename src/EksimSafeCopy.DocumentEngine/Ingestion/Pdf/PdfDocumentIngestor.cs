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
    public PdfDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }
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
        var textBlocks = new List<TextBlock>();
        var pageText = pdfPage.Text;
        // Extract words with positions
        var words = pdfPage.GetWords().ToList();
        var pageWidth = pdfPage.Width;
        var pageHeight = pdfPage.Height;
        var textBlocksFromWords = CreateTextBlocksFromWords(words, pageWidth, pageHeight);
        var page = new DocumentPage
        {
            PageNumber = pdfPage.Number,
            Width = Math.Max(0, (int)pdfPage.Width),
            Height = Math.Max(0, (int)pdfPage.Height),
            DpiX = 72, // PDF default
            DpiY = 72,
            Text = pageText,
            TextBlocks = textBlocksFromWords,
            IsScanned = words.Count == 0 && pdfPage.GetImages().Any(), // Heuristic: no text but has images
            Images = ExtractImages(pdfPage).ToList()
        };
        return page;
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
