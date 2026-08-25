namespace EksimSafeCopy.DocumentEngine.Ingestion.Image;

using global::EksimSafeCopy.Core.Abstractions;
using global::EksimSafeCopy.Core.Models;
using global::EksimSafeCopy.DocumentEngine.Ingestion;
using global::EksimSafeCopy.DocumentEngine.Security;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

public sealed class ImageDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Png;
    public override string[] SupportedExtensions => new[] { ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".bmp" };

    public ImageDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }

    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var format = DetectFormatFromExtension(filePath);
            using var image = Image.Load<Rgba32>(filePath);
            
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = image.Width,
                Height = image.Height,
                DpiX = image.Metadata.HorizontalResolution > 0 ? image.Metadata.HorizontalResolution : 96,
                DpiY = image.Metadata.VerticalResolution > 0 ? image.Metadata.VerticalResolution : 96,
                Text = string.Empty,
                TextBlocks = Array.Empty<TextBlock>(),
                IsScanned = true,
                Images = new List<ImageReference>
                {
                    new ImageReference
                    {
                        Format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                        Width = image.Width,
                        Height = image.Height,
                        Data = ReadImageBytes(filePath),
                        BoundingBox = new BoundingBox(0, 0, image.Width, image.Height, image.Width, image.Height)
                    }
                }
            };

            var document = new Document
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = format,
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

    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var image = Image.Load<Rgba32>(stream);
            
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = image.Width,
                Height = image.Height,
                DpiX = image.Metadata.HorizontalResolution > 0 ? image.Metadata.HorizontalResolution : 96,
                DpiY = image.Metadata.VerticalResolution > 0 ? image.Metadata.VerticalResolution : 96,
                Text = string.Empty,
                TextBlocks = Array.Empty<TextBlock>(),
                IsScanned = true,
                Images = new List<ImageReference>
                {
                    new ImageReference
                    {
                        Format = "unknown",
                        Width = image.Width,
                        Height = image.Height,
                        Data = ReadStreamBytes(stream),
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
}