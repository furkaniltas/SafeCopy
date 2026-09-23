namespace SafeCopy.DocumentEngine.Ingestion.Txt;
using global::SafeCopy.Core.Abstractions;
using global::SafeCopy.Core.Models;
using global::SafeCopy.DocumentEngine.Ingestion;
using global::SafeCopy.DocumentEngine.Security;
using System.Text;
public sealed class TxtDocumentIngestor : DocumentIngestorBase
{
    public override DocumentFormat SupportedFormat => DocumentFormat.Txt;
    public override string[] SupportedExtensions => new[] { ".txt" };
    static TxtDocumentIngestor()
    {
        // Register encoding provider for legacy encodings like windows-1254
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }
    public TxtDocumentIngestor(IDocumentSecurityValidator securityValidator, IFileSystem fileSystem)
        : base(securityValidator, fileSystem) { }
    protected override Result<Document> IngestInternal(string filePath, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var encoding = DetectEncoding(filePath);
            var text = File.ReadAllText(filePath, encoding);
            return ProcessTextFile(filePath, text, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"TXT ingestion failed: {ex.Message}", ex));
        }
    }
    protected override Result<Document> IngestFromStreamInternal(Stream stream, IngestionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // Read stream to detect encoding
            var buffer = new byte[Math.Min(stream.Length, 4096)];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            stream.Position = 0;
            var encoding = DetectEncodingFromBytes(buffer);
            using var reader = new StreamReader(stream, encoding, true);
            var text = reader.ReadToEnd();
            return ProcessTextFile("stream.txt", text, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"TXT ingestion failed: {ex.Message}", ex));
        }
    }
    private Result<Document> ProcessTextFile(string filePath, string text, CancellationToken cancellationToken)
    {
        try
        {
            var page = new DocumentPage
            {
                PageNumber = 1,
                Width = 800,
                Height = 600,
                DpiX = 96,
                DpiY = 96,
                Text = text,
                TextBlocks = CreateTextBlocks(text),
                IsScanned = false
            };
            var document = new Document
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Format = DocumentFormat.Txt,
                Pages = new[] { page },
                Metadata = new DocumentMetadata
                {
                    FileSize = new FileInfo(filePath).Length,
                    Created = File.GetCreationTimeUtc(filePath),
                    Modified = File.GetLastWriteTimeUtc(filePath)
                }
            };
            return Result<Document>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<Document>.Failure(Error.Internal($"TXT processing failed: {ex.Message}", ex));
        }
    }
    private IReadOnlyList<TextBlock> CreateTextBlocks(string text)
    {
        var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var blocks = new List<TextBlock>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var block = new TextBlock
            {
                Text = line,
                Type = TextBlockType.Paragraph,
                Direction = TextDirection.LeftToRight,
                OrderIndex = i,
                PageNumber = 1,
                BoundingBox = BoundingBox.Empty,
                Spans = new List<TextSpan>
                {
                    new TextSpan
                    {
                        StartIndex = 0,
                        Length = lines[i].Length,
                        Text = lines[i],
                        BoundingBox = BoundingBox.Empty
                    }
                }
            };
            blocks.Add(block);
        }
        return blocks;
    }
    private Encoding DetectEncoding(string filePath)
    {
        var buffer = new byte[4096];
        using (var stream = File.OpenRead(filePath))
        {
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            Array.Resize(ref buffer, bytesRead);
        }
        return DetectEncodingFromBytes(buffer);
    }
    private Encoding DetectEncodingFromBytes(byte[] buffer)
    {
        // Check for BOM
        if (buffer.Length >= 4)
        {
            // UTF-32 BE: 00 00 FE FF
            if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return Encoding.GetEncoding("UTF-32BE");
            // UTF-32 LE: FF FE 00 00
            if (buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                return Encoding.GetEncoding("UTF-32");
        }
        if (buffer.Length >= 3)
        {
            // UTF-8 BOM: EF BB BF
            if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return new UTF8Encoding(true);
        }
        if (buffer.Length >= 2)
        {
            // UTF-16 BE: FE FF
            if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            // UTF-16 LE: FF FE
            if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Encoding.Unicode;
        }
        // Try to detect UTF-8 without BOM
        if (IsValidUtf8(buffer))
            return new UTF8Encoding(false);
        // Fallback to Windows-1254 (Turkish) or UTF-8
        return Encoding.GetEncoding("windows-1254");
    }
    private bool IsValidUtf8(byte[] buffer)
    {
        int i = 0;
        while (i < buffer.Length)
        {
            if (buffer[i] <= 0x7F)
            {
                i++;
            }
            else if ((buffer[i] & 0xE0) == 0xC0)
            {
                if (i + 1 >= buffer.Length || (buffer[i + 1] & 0xC0) != 0x80) return false;
                i += 2;
            }
            else if ((buffer[i] & 0xF0) == 0xE0)
            {
                if (i + 2 >= buffer.Length || (buffer[i + 1] & 0xC0) != 0x80 || (buffer[i + 2] & 0xC0) != 0x80) return false;
                i += 3;
            }
            else if ((buffer[i] & 0xF8) == 0xF0)
            {
                if (i + 3 >= buffer.Length || (buffer[i + 1] & 0xC0) != 0x80 || (buffer[i + 2] & 0xC0) != 0x80 || (buffer[i + 3] & 0xC0) != 0x80) return false;
                i += 4;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}
