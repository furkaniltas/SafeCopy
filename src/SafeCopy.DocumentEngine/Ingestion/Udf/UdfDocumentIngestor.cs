namespace SafeCopy.DocumentEngine.Ingestion.Udf;

using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using global::SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.DocumentEngine.Security;
using System.IO.Compression;
using System.Xml.Linq;

public sealed class UdfDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Udf;
    public override string[] SupportedExtensions => new[] { ".udf", ".udf.zip", ".zip" };

    public UdfDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }

    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // Support both ZIP file and directory-based UDF (e.g., FFF.udf as folder with content.xml)
            if (Directory.Exists(filePath))
            {
                var contentPath = Path.Combine(filePath, "content.xml");
                if (!File.Exists(contentPath))
                    contentPath = Path.Combine(filePath, "content");
                if (!File.Exists(contentPath))
                    return Result<Document>.Failure(Error.FormatError("UDF directory missing content.xml"));
                var contentXml = File.ReadAllText(contentPath, System.Text.Encoding.UTF8);
                var doc = ParseContentXml(contentXml, filePath);
                // Try to load images from directory
                var images = ExtractImagesFromDirectory(filePath).ToList();
                if (images.Count > 0)
                {
                    var pages = doc.Pages.ToList();
                    if (pages.Count > 0)
                    {
                        var p = pages[0];
                        pages[0] = new DocumentPage
                        {
                            PageNumber = p.PageNumber, Width = p.Width, Height = p.Height, DpiX = p.DpiX, DpiY = p.DpiY,
                            Text = p.Text, TextBlocks = p.TextBlocks, Images = images, IsScanned = p.IsScanned
                        };
                        doc = doc.WithPages(pages);
                    }
                }
                return Result<Document>.Success(doc);
            }

            using var archive = ZipFile.OpenRead(filePath);
            return ProcessUdfArchive(archive, filePath, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return Result<Document>.Failure(Error.FormatError($"Invalid UDF format (not a valid ZIP): {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"UDF ingestion failed: {ex.Message}", ex));
        }
    }

    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // For stream, we need to copy to memory first since ZipArchive needs seekable stream
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
            return ProcessUdfArchive(archive, "stream.udf", cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return Result<Document>.Failure(Error.FormatError($"Invalid UDF format (not a valid ZIP): {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"UDF ingestion failed: {ex.Message}", ex));
        }
    }

    private Result<Document> ProcessUdfArchive(ZipArchive archive, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            // Extract content.xml
            var contentEntry = archive.GetEntry("content.xml");
            if (contentEntry == null)
                return Result<Document>.Failure(Error.FormatError("UDF missing content.xml"));

            var contentXml = ReadEntryToString(contentEntry);
            var document = ParseContentXml(contentXml, filePath);
            
            // Check for digital signature
            var signatureEntry = archive.GetEntry("signature.p7s") ?? archive.GetEntry("sign.sgn");
            if (signatureEntry != null)
            {
                document.Metadata.CustomProperties["HasDigitalSignature"] = "true";
                document.Metadata.CustomProperties["SignatureFile"] = signatureEntry.Name;
            }

            // Extract images from binary/ directory
            var images = ExtractImages(archive).ToList();

            var pages = document.Pages.ToList();
            if (pages.Count > 0)
            {
                var page = pages[0];
                pages[0] = new DocumentPage
                {
                    PageNumber = page.PageNumber,
                    Width = page.Width,
                    Height = page.Height,
                    DpiX = page.DpiX,
                    DpiY = page.DpiY,
                    Text = page.Text,
                    TextBlocks = page.TextBlocks,
                    Rotation = page.Rotation,
                    IsScanned = page.IsScanned,
                    OcrInfo = page.OcrInfo,
                    CoordinateSystem = page.CoordinateSystem,
                    Images = images
                };
            }

            var newDocument = document.WithPages(pages);
            return Result<Document>.Success(newDocument);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"UDF processing failed: {ex.Message}", ex));
        }
    }

    private Document ParseContentXml(string xmlContent, string filePath)
    {
        var xdoc = XDocument.Parse(xmlContent);
        var root = xdoc.Root;

        var allText = new List<string>();
        var textBlocks = new List<TextBlock>();

        // Parse UDF XML structure - this is based on the reverse-engineered format
        // Root element is typically <document> or <udf:document>
        var bodyElement = root?.Element("body") ?? root?.Element("{*}body");
        if (bodyElement != null)
        {
            foreach (var paragraph in bodyElement.Elements("paragraph").Concat(bodyElement.Elements("{*}paragraph")))
            {
                var text = paragraph.Value.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                allText.Add(text);

                var block = new TextBlock
                {
                    Text = text,
                    Type = TextBlockType.Paragraph,
                    Direction = global::SafeCopy.Core.Models.TextDirection.LeftToRight,
                    OrderIndex = textBlocks.Count,
                    PageNumber = 1,
                    BoundingBox = BoundingBox.Empty
                };
                textBlocks.Add(block);
            }
        }

        // Real UYAP UDF uses <template><content><![CDATA[ ... ]]></content></template>
        if (!allText.Any())
        {
            var contentElement = root?.Element("content")
                ?? root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "content");
            if (contentElement != null)
            {
                var raw = contentElement.Value ?? string.Empty;
                // Split CDATA by lines, preserve non-empty lines as blocks
                var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                if (lines.Count > 0)
                {
                    // Use full raw trimmed text for DocumentPage.Text (preserves original formatting for detection)
                    var fullText = raw.Trim();
                    allText.Add(fullText);
                    foreach (var line in lines)
                    {
                        var block = new TextBlock
                        {
                            Text = line,
                            Type = TextBlockType.Paragraph,
                            Direction = global::SafeCopy.Core.Models.TextDirection.LeftToRight,
                            OrderIndex = textBlocks.Count,
                            PageNumber = 1,
                            BoundingBox = BoundingBox.Empty
                        };
                        textBlocks.Add(block);
                    }
                }
            }
        }

        // Fallback: get all leaf text from the entire document (avoid duplicate parent Values)
        if (!allText.Any())
        {
            var leafTexts = xdoc.Descendants()
                .Where(e => !e.HasElements)
                .Select(e => e.Value.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            // If still empty, fall back to root value (e.g., single text node)
            if (!leafTexts.Any() && root != null && !string.IsNullOrWhiteSpace(root.Value))
                leafTexts.Add(root.Value.Trim());

            foreach (var text in leafTexts)
            {
                allText.Add(text);
                var block = new TextBlock
                {
                    Text = text,
                    Type = TextBlockType.Paragraph,
                    Direction = global::SafeCopy.Core.Models.TextDirection.LeftToRight,
                    OrderIndex = textBlocks.Count,
                    PageNumber = 1,
                    BoundingBox = BoundingBox.Empty
                };
                textBlocks.Add(block);
            }
        }

        var page = new DocumentPage
        {
            PageNumber = 1,
            Width = 612,
            Height = 792,
            DpiX = 96,
            DpiY = 96,
            Text = string.Join("\n", allText),
            TextBlocks = textBlocks,
            IsScanned = false
        };

        var document = new Document
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            Format = DocumentFormat.Udf,
            Pages = new[] { page },
            Metadata = ExtractMetadata()
        };

        return document;
    }

    private DocumentMetadata ExtractMetadata()
    {
        return new DocumentMetadata
        {
            CustomProperties = new Dictionary<string, string>
            {
                ["Format"] = "UDF (UYAP Document Format)",
                ["Parser"] = "SafeCopy UDF Parser v1.0"
            }
        };
    }

    private string ReadEntryToString(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private IEnumerable<ImageReference> ExtractImages(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("binary/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                ImageReference? imageRef = null;
                try
                {
                    using var stream = entry.Open();
                    var buffer = new byte[entry.Length];
                    stream.ReadExactly(buffer, 0, buffer.Length);

                    var imgRef = new ImageReference
                    {
                        Format = Path.GetExtension(entry.Name).TrimStart('.'),
                        Data = buffer,
                        BoundingBox = BoundingBox.Empty
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
    }

    private IEnumerable<ImageReference> ExtractImagesFromDirectory(string directoryPath)
    {
        var result = new List<ImageReference>();
        foreach (var subDir in new[] { "binary", "images" })
        {
            var dir = Path.Combine(directoryPath, subDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir))
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    result.Add(new ImageReference
                    {
                        Format = Path.GetExtension(file).TrimStart('.'),
                        Data = bytes,
                        BoundingBox = BoundingBox.Empty
                    });
                }
                catch { /* skip */ }
            }
        }
        foreach (var item in result) yield return item;
    }
}