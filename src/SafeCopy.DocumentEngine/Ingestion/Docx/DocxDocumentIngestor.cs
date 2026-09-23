namespace SafeCopy.DocumentEngine.Ingestion.Docx;

using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using CoreDocument = global::SafeCopy.Core.Models.Document;
using global::SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.DocumentEngine.Security;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OxTextDirection = DocumentFormat.OpenXml.Wordprocessing.TextDirection;
using OxUnderlineValues = DocumentFormat.OpenXml.Wordprocessing.UnderlineValues;
using System.Text;

public sealed class DocxDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Docx;
    public override string[] SupportedExtensions => new[] { ".docx" };

    public DocxDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }

    protected override Result<CoreDocument> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            return ProcessDocxDocument(wordDocument, filePath, cancellationToken);
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<CoreDocument>.Failure(Error.FormatError($"Invalid DOCX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<CoreDocument>.Failure(Error.Internal($"DOCX ingestion failed: {ex.Message}", ex));
        }
    }

    protected override Result<CoreDocument> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            return ProcessDocxDocument(wordDocument, "stream.docx", cancellationToken);
        }
        catch (OpenXmlPackageException ex)
        {
            return Result<CoreDocument>.Failure(Error.FormatError($"Invalid DOCX format: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<CoreDocument>.Failure(Error.Internal($"DOCX ingestion failed: {ex.Message}", ex));
        }
    }

    private Result<CoreDocument> ProcessDocxDocument(WordprocessingDocument wordDocument, string filePath, CancellationToken cancellationToken)
    {
        var pages = new List<DocumentPage>();
        var fileName = Path.GetFileName(filePath);

        try
        {
            var mainDocumentPart = wordDocument.MainDocumentPart;
            if (mainDocumentPart?.Document == null)
                return Result<CoreDocument>.Failure(Error.FormatError("DOCX document is empty or corrupted"));

            var documentText = ExtractText(mainDocumentPart);
            var textBlocks = ExtractTextBlocks(mainDocumentPart);
            var images = ExtractImages(mainDocumentPart).ToList();

            // DOCX doesn't have explicit page structure, so we create a single "page"
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = 612, // Default letter width in points
                Height = 792, // Default letter height in points
                DpiX = 96,
                DpiY = 96,
                Text = documentText,
                TextBlocks = textBlocks,
                Images = images,
                IsScanned = false
            };

            var document = new CoreDocument
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = DocumentFormat.Docx,
                Pages = new[] { page },
                Metadata = ExtractMetadata(wordDocument)
            };

            return Result<CoreDocument>.Success(new CoreDocument
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = DocumentFormat.Docx,
                Pages = new[] { page },
                Metadata = ExtractMetadata(wordDocument)
            });
        }
        catch (Exception ex)
        {
            return Result<CoreDocument>.Failure(Error.Internal($"DOCX processing failed: {ex.Message}", ex));
        }
    }

    private string ExtractText(MainDocumentPart mainDocumentPart)
    {
        var body = mainDocumentPart.Document.Body;
        if (body == null) return string.Empty;

        // Join paragraph texts with newline to preserve boundaries for detection
        return string.Join("\n", body.Elements<Paragraph>().Select(p => p.InnerText));
    }

    private IReadOnlyList<TextBlock> ExtractTextBlocks(MainDocumentPart mainDocumentPart)
    {
        var body = mainDocumentPart.Document.Body;
        if (body == null) return Array.Empty<TextBlock>();

        var blocks = new List<TextBlock>();
        int orderIndex = 0;

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var block = new TextBlock
            {
                Text = text,
                Type = DetermineBlockType(paragraph),
                Direction = global::SafeCopy.Core.Models.TextDirection.LeftToRight,
                OrderIndex = orderIndex++,
                PageNumber = 1,
                BoundingBox = BoundingBox.Empty, // DOCX doesn't have explicit coordinates
                Spans = ExtractSpansFromParagraph(paragraph).ToList()
            };

            blocks.Add(block);
        }

        return blocks;
    }

    private TextBlockType DetermineBlockType(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId != null)
        {
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                return TextBlockType.Heading;
            if (styleId.Contains("Header", StringComparison.OrdinalIgnoreCase))
                return TextBlockType.Header;
            if (styleId.Contains("Footer", StringComparison.OrdinalIgnoreCase))
                return TextBlockType.Footer;
        }
        return TextBlockType.Paragraph;
    }

    private IEnumerable<TextSpan> ExtractSpansFromParagraph(Paragraph paragraph)
    {
        var runs = paragraph.Elements<Run>().ToList();
        int currentIndex = 0;

        foreach (var run in runs)
        {
            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) continue;

            var span = new TextSpan
            {
                StartIndex = currentIndex,
                Length = text.Length,
                Text = text,
                BoundingBox = BoundingBox.Empty,
                Font = ExtractFontInfo(run),
                BlockId = 0
            };

            currentIndex += text.Length;
            yield return span;
        }
    }

    private FontInfo? ExtractFontInfo(Run run)
    {
        var runProps = run.RunProperties;
        if (runProps == null) return null;

        return new FontInfo
        {
            Name = runProps.RunFonts?.Ascii?.Value ?? string.Empty,
            Size = int.TryParse(runProps.FontSize?.Val, out var fontSize) ? fontSize : 0,
            Bold = runProps.Bold?.Val?.Value == true,
            Italic = runProps.Italic?.Val?.Value == true,
            Underline = runProps.Underline?.Val?.Value != OxUnderlineValues.None,
            Strikeout = runProps.Strike?.Val?.Value == true,
            Color = runProps.Color?.Val?.Value ?? "#000000"
        };
    }

    private IEnumerable<ImageReference> ExtractImages(MainDocumentPart mainDocumentPart)
    {
        if (mainDocumentPart.ImageParts == null) yield break;

        foreach (var imagePart in mainDocumentPart.ImageParts)
        {
            ImageReference? imageRef = null;
            try
            {
                using var stream = imagePart.GetStream();
                var imageBytes = new byte[stream.Length];
                stream.ReadExactly(imageBytes, 0, (int)stream.Length);

                imageRef = new ImageReference
                {
                    Format = imagePart.ContentType.Split('/').Last(),
                    Data = imageBytes,
                    BoundingBox = BoundingBox.Empty
                };
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

    private DocumentMetadata ExtractMetadata(WordprocessingDocument wordDocument)
    {
        var coreProps = wordDocument.PackageProperties;
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