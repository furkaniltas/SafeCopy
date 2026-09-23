namespace SafeCopy.DocumentEngine.Ingestion.Image;
using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using global::SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.DocumentEngine.Security;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
public sealed class ImageDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Png; // Base format, actual detected per file
    public override string[] SupportedExtensions => new[] { ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".bmp" };
    private readonly IOcrEngine? _ocrEngine;
    public ImageDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem, IOcrEngine? ocrEngine = null)
        : base(securityValidator, fileSystem) { _ocrEngine = ocrEngine; }
    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // Detect actual image format from file signature
            var detectedFormat = DetectImageFormat(filePath);
            using var image = Image.Load<Rgba32>(filePath);
            var imageBytes = ReadImageBytes(filePath);
            var (ocrText, ocrBlocks, ocrInfo) = TryOcr(imageBytes, image.Width, image.Height, 1, cancellationToken);
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = image.Width,
                Height = image.Height,
                DpiX = image.Metadata.HorizontalResolution > 0 ? image.Metadata.HorizontalResolution : 96,
                DpiY = image.Metadata.VerticalResolution > 0 ? image.Metadata.VerticalResolution : 96,
                Text = ocrText ?? string.Empty,
                TextBlocks = ocrBlocks ?? Array.Empty<TextBlock>(),
                IsScanned = true,
                OcrInfo = ocrInfo,
                CoordinateSystem = new CoordinateSystem(image.Width, image.Height, CoordinateOrigin.TopLeft, CoordinateUnit.Pixels),
                Images = new List<ImageReference>
                {
                    new ImageReference
                    {
                        Format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                        Width = image.Width,
                        Height = image.Height,
                        Data = imageBytes,
                        BoundingBox = new BoundingBox(0, 0, image.Width, image.Height, image.Width, image.Height)
                    }
                }
            };
            var document = new Document
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = detectedFormat,
                Pages = new[] { page },
                Metadata = new DocumentMetadata
                {
                    FileSize = new FileInfo(filePath).Length,
                    Created = File.GetCreationTimeUtc(filePath),
                    Modified = File.GetLastWriteTimeUtc(filePath),
                    CustomProperties = ExtractImageMetadata(image)
                }
            };
            return Result<Document>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"Image ingestion failed: {ex.Message}", ex));
        }
    }
    private DocumentFormat DetectImageFormat(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return DetectImageFormatFromStream(stream);
        }
        catch
        {
            return DetectFormatFromExtension(filePath);
        }
    }
    private DocumentFormat DetectImageFormatFromStream(Stream stream)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var header = new byte[16];
            var bytesRead = stream.Read(header, 0, header.Length);
            stream.Position = originalPosition;
            if (bytesRead < 4)
                return DocumentFormat.Unknown;
            // PNG: \x89PNG\r\n\x1a\n
            if (bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return DocumentFormat.Png;
            // JPEG: FF D8 FF
            if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return DocumentFormat.Jpeg;
            // TIFF: II\x2A\x00 or MM\x00\x2A
            if (bytesRead >= 4 &&
                ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00) ||
                 (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
                return DocumentFormat.Tiff;
            // BMP: BM
            if (bytesRead >= 2 && header[0] == 0x42 && header[1] == 0x4D)
                return DocumentFormat.Bmp;
            return DocumentFormat.Unknown;
        }
        catch
        {
            return DocumentFormat.Unknown;
        }
    }
    public override Result<Document> Ingest(string filePath, IngestionOptions options, CancellationToken cancellationToken = default)
    {
        // Override to skip base format validation (we do our own format detection)
        var effectiveOptions = options ?? new IngestionOptions();
        try
        {
            // Validate file access (but skip format validation since we detect actual format)
            var accessResult = _securityValidator.ValidateFileAccess(filePath);
            if (accessResult.IsFailure)
                return Result<Document>.Failure(accessResult.Error);
            // Compute hash if requested
            string fileHash = string.Empty;
            if (effectiveOptions.ComputeHash)
            {
                var hashResult = _securityValidator.ComputeFileHash(filePath, effectiveOptions.HashAlgorithm);
                if (hashResult.IsFailure)
                    return Result<Document>.Failure(hashResult.Error);
                fileHash = hashResult.Value;
            }
            // Get file size
            long fileSize = 0;
            var sizeResult = _fileSystem.GetSize(filePath);
            if (sizeResult.IsSuccess)
                fileSize = sizeResult.Value;
            // Perform ingestion with timeout
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveOptions.Timeout);
            var document = IngestInternal(filePath, effectiveOptions, cts.Token);
            if (document.IsSuccess)
            {
                // Create new document with source and metadata
                var sourceRef = new SourceReference
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Format = document.Value.Format, // Use detected format
                    FileSize = fileSize,
                    FileHash = fileHash,
                    LoadedAt = DateTime.UtcNow
                };
                var metadata = document.Value.Metadata.WithFileInfo(fileSize, fileHash);
                document = document.Value
                    .WithSource(sourceRef)
                    .WithMetadata(metadata);
            }
            return document;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<Document>.Failure(Error.Cancelled("Ingestion was cancelled"));
        }
        catch (OperationCanceledException)
        {
            return Result<Document>.Failure(Error.Timeout($"Ingestion timed out after {effectiveOptions.Timeout}"));
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"Ingestion failed: {ex.Message}", ex));
        }
    }
    private byte[] ReadImageBytes(string filePath)
    {
        return File.ReadAllBytes(filePath);
    }
    private byte[] ReadStreamBytes(Stream stream)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        stream.Position = originalPosition;
        return memoryStream.ToArray();
    }
    private Dictionary<string, string> ExtractImageMetadata(Image<Rgba32> image)
    {
        var properties = new Dictionary<string, string>
        {
            ["Width"] = image.Width.ToString(),
            ["Height"] = image.Height.ToString(),
            ["HorizontalResolution"] = image.Metadata.HorizontalResolution.ToString(),
            ["VerticalResolution"] = image.Metadata.VerticalResolution.ToString(),
            ["PixelFormat"] = "RGBA32"
        };
        if (image.Metadata.ExifProfile != null)
        {
            foreach (var value in image.Metadata.ExifProfile.Values)
            {
                properties[$"EXIF_{value.Tag}"] = value.GetValue()?.ToString() ?? string.Empty;
            }
        }
        return properties;
    }
    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var streamBytes = ReadStreamBytes(stream);
            using var image = Image.Load<Rgba32>(streamBytes);
            var (ocrText, ocrBlocks, ocrInfo) = TryOcr(streamBytes, image.Width, image.Height, 1, cancellationToken);
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = image.Width,
                Height = image.Height,
                DpiX = image.Metadata.HorizontalResolution > 0 ? image.Metadata.HorizontalResolution : 96,
                DpiY = image.Metadata.VerticalResolution > 0 ? image.Metadata.VerticalResolution : 96,
                Text = ocrText ?? string.Empty,
                TextBlocks = ocrBlocks ?? Array.Empty<TextBlock>(),
                IsScanned = true,
                OcrInfo = ocrInfo,
                CoordinateSystem = new CoordinateSystem(image.Width, image.Height, CoordinateOrigin.TopLeft, CoordinateUnit.Pixels),
                Images = new List<ImageReference>
                {
                    new ImageReference
                    {
                        Format = "unknown",
                        Width = image.Width,
                        Height = image.Height,
                        Data = streamBytes,
                        BoundingBox = new BoundingBox(0, 0, image.Width, image.Height, image.Width, image.Height)
                    }
                }
            };
            var document = new Document
            {
                Name = "image",
                Format = DocumentFormat.Png,
                Pages = new[] { page },
                Metadata = new DocumentMetadata
                {
                    CustomProperties = ExtractImageMetadata(image)
                }
            };
            return Result<Document>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"Image ingestion failed: {ex.Message}", ex));
        }
    }

    private (string? Text, IReadOnlyList<TextBlock>? Blocks, OcrInfo? Info) TryOcr(byte[] data, int width, int height, int pageNumber, CancellationToken ct)
    {
        if (_ocrEngine == null || !_ocrEngine.IsAvailable) return (null, null, null);
        try
        {
            var result = _ocrEngine.Recognize(data, "tr", ct);
            if (result.IsFailure) return (null, null, null);
            var ocr = result.Value;
            if (string.IsNullOrWhiteSpace(ocr.Text)) return (ocr.Text, Array.Empty<TextBlock>(), null);
            var blocks = MapOcrToBlocks(ocr, pageNumber, width, height);
            var info = new OcrInfo { Engine = _ocrEngine.EngineName, Language = ocr.Language, AverageConfidence = ocr.Confidence, ProcessedAt = ocr.ProcessedAt, Duration = ocr.Duration };
            return (ocr.Text, blocks, info);
        }
        catch { return (null, null, null); }
    }

    private static IReadOnlyList<TextBlock> MapOcrToBlocks(OcrResult ocrResult, int pageNumber, int width, int height)
    {
        double safeW = Math.Max(1, width);
        double safeH = Math.Max(1, height);
        var blocks = new List<TextBlock>();
        if (ocrResult.Lines.Count > 0)
        {
            int order = 0;
            foreach (var line in ocrResult.Lines)
            {
                var bb = line.BoundingBox.IsEmpty ? new BoundingBox(0, order * 22, safeW, 20, safeW, safeH) : line.BoundingBox;
                blocks.Add(new TextBlock
                {
                    Text = line.Text,
                    BoundingBox = bb,
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
                });
            }
            return blocks;
        }
        if (!string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            blocks.Add(new TextBlock
            {
                Text = ocrResult.Text,
                BoundingBox = new BoundingBox(0, 0, safeW, 20, safeW, safeH),
                PageNumber = pageNumber,
                OrderIndex = 0,
                Type = TextBlockType.Paragraph,
                Direction = TextDirection.LeftToRight,
                Properties = new Dictionary<string, object> { ["DetectionSource"] = "Ocr", ["Confidence"] = ocrResult.Confidence }
            });
        }
        return blocks;
    }
}
