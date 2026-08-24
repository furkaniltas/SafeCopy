namespace EksimSafeCopy.Core.Models;

using EksimSafeCopy.Core.Abstractions;

public sealed class Document
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public DocumentFormat Format { get; init; }
    public DocumentMetadata Metadata { get; init; } = new();
    public IReadOnlyList<DocumentPage> Pages { get; init; } = Array.Empty<DocumentPage>();
    public SourceReference Source { get; init; } = new();
    
    public int PageCount => Pages.Count;
    public long TotalCharacters => Pages.Sum(p => p.Text.Length);
    
    public Document() { }
    
    public Document(string name, DocumentFormat format, IReadOnlyList<DocumentPage> pages)
    {
        Name = name;
        Format = format;
        Pages = pages;
    }
}

public sealed class DocumentPage
{
    public int PageNumber { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DpiX { get; init; }
    public double DpiY { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextBlock> TextBlocks { get; init; } = Array.Empty<TextBlock>();
    public IReadOnlyList<ImageReference> Images { get; init; } = Array.Empty<ImageReference>();
    public PageRotation Rotation { get; init; } = PageRotation.None;
    public bool IsScanned { get; init; }
    public OcrInfo? OcrInfo { get; init; }
}

public sealed class TextBlock
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public BoundingBox BoundingBox { get; init; } = new();
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextSpan> Spans { get; init; } = Array.Empty<TextSpan>();
    public TextBlockType Type { get; init; } = TextBlockType.Paragraph;
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;
    public Dictionary<string, object> Properties { get; init; } = new();
}

public sealed class TextSpan
{
    public int StartIndex { get; init; }
    public int Length { get; init; }
    public string Text { get; init; } = string.Empty;
    public BoundingBox BoundingBox { get; init; } = new();
    public FontInfo? Font { get; init; }
    public TextStyle Style { get; init; } = new();
    public Dictionary<string, object> Properties { get; init; } = new();
}

public sealed class BoundingBox
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double PageWidth { get; init; }
    public double PageHeight { get; init; }
    
    public double Right => X + Width;
    public double Bottom => Y + Height;
    
    public BoundingBox() { }
    
    public BoundingBox(double x, double y, double width, double height, double pageWidth = 0, double pageHeight = 0)
    {
        X = x; Y = y; Width = width; Height = height;
        PageWidth = pageWidth; PageHeight = pageHeight;
    }
    
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    public bool Intersects(BoundingBox other) => !(Right < other.X || X > other.Right || Bottom < other.Y || Y > other.Bottom);
    public BoundingBox Union(BoundingBox other) => new(
        Math.Min(X, other.X), Math.Min(Y, other.Y),
        Math.Max(Right, other.Right) - Math.Min(X, other.X),
        Math.Max(Bottom, other.Bottom) - Math.Min(Y, other.Y),
        Math.Max(PageWidth, other.PageWidth), Math.Max(PageHeight, other.PageHeight));
    
    public BoundingBox Normalize(double pageWidth, double pageHeight)
        => new(X / pageWidth, Y / pageHeight, Width / pageWidth, Height / pageHeight, 1, 1);
}

public sealed class DocumentMetadata
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? Creator { get; init; }
    public string? Producer { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }
    public long FileSize { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public Dictionary<string, string> CustomProperties { get; init; } = new();
}

public sealed class SourceReference
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DocumentFormat Format { get; init; }
    public long FileSize { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}

public sealed class ImageReference
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public BoundingBox BoundingBox { get; init; } = new();
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = string.Empty;
    public byte[]? Data { get; init; }
    public string? ExternalPath { get; init; }
}

public sealed class FontInfo
{
    public string Name { get; init; } = string.Empty;
    public double Size { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikeout { get; init; }
    public string Color { get; init; } = "#000000";
}

public sealed class TextStyle
{
    public string? ForegroundColor { get; init; }
    public string? BackgroundColor { get; init; }
    public bool IsHidden { get; init; }
    public bool IsHyperlink { get; init; }
    public string? HyperlinkUrl { get; init; }
}

public sealed class OcrInfo
{
    public string Engine { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public double AverageConfidence { get; init; }
    public DateTime ProcessedAt { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class OcrResult
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
    public IReadOnlyList<OcrLine> Lines { get; init; } = Array.Empty<OcrLine>();
    public IReadOnlyList<OcrParagraph> Paragraphs { get; init; } = Array.Empty<OcrParagraph>();
    public string Language { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}

public sealed class OcrWord
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
}

public sealed class OcrLine
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
}

public sealed class OcrParagraph
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
    public IReadOnlyList<OcrLine> Lines { get; init; } = Array.Empty<OcrLine>();
}

public enum PageRotation
{
    None = 0,
    Rotate90 = 90,
    Rotate180 = 180,
    Rotate270 = 270
}

public enum TextBlockType
{
    Paragraph,
    Heading,
    Table,
    List,
    Header,
    Footer,
    Footnote,
    Endnote,
    TextBox,
    Comment,
    Caption
}

public enum TextDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}

public sealed class Detection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DetectionType Type { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public ConfidenceLevel ConfidenceLevel { get; init; }
    public BoundingBox Location { get; init; } = new();
    public int PageNumber { get; init; }
    public SourceReference Source { get; init; } = new();
    public DetectionSource DetectionSource { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
    public DetectionState State { get; set; } = DetectionState.Detected;
    public string? Reason { get; init; }
    public string? Explanation { get; init; }
}

public enum DetectionType
{
    Unknown,
    TcKimlikNo,
    Phone,
    Email,
    Iban,
    TaxId,
    PassportNo,
    Date,
    LicensePlate,
    TesisatNo,
    AboneNo,
    SayacNo,
    MusteriNo,
    DosyaNo,
    DavaNo,
    FullName,
    Address,
    CreditCard,
    IpAddress,
    Url,
    Custom
}

public enum ConfidenceLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum DetectionSource
{
    NativeText,
    OcrText,
    Metadata,
    EmbeddedObject,
    HiddenContent,
    Manual
}

public enum DetectionState
{
    Detected,
    Selected,
    Deselected,
    Masked,
    FalsePositive
}

public sealed class RenderOptions
{
    public MaskingMode Mode { get; init; } = MaskingMode.FullRedaction;
    public string PlaceholderText { get; init; } = "[REDACTED]";
    public bool UseTypePlaceholder { get; init; } = true;
    public bool MaskRepeatedValues { get; init; } = true;
    public bool SanitizeMetadata { get; init; } = true;
    public bool RemoveHiddenContent { get; init; } = true;
    public Dictionary<DetectionType, string> TypePlaceholders { get; init; } = new()
    {
        { DetectionType.TcKimlikNo, "[TC_KIMLIK_NO]" },
        { DetectionType.Phone, "[PHONE]" },
        { DetectionType.Email, "[EMAIL]" },
        { DetectionType.Iban, "[IBAN]" },
        { DetectionType.FullName, "[NAME]" },
        { DetectionType.Address, "[ADDRESS]" },
        { DetectionType.Date, "[DATE]" },
        { DetectionType.TesisatNo, "[TESISAT_NO]" },
        { DetectionType.AboneNo, "[ABONE_NO]" },
        { DetectionType.SayacNo, "[SAYAC_NO]" },
        { DetectionType.MusteriNo, "[MUSTERI_NO]" },
        { DetectionType.DosyaNo, "[DOSYA_NO]" },
        { DetectionType.DavaNo, "[DAVA_NO]" },
        { DetectionType.CreditCard, "[CREDIT_CARD]" },
        { DetectionType.TaxId, "[TAX_ID]" },
        { DetectionType.PassportNo, "[PASSPORT]" },
        { DetectionType.LicensePlate, "[PLATE]" },
    };
}

public enum MaskingMode
{
    FullRedaction,
    Placeholder,
    PartialMask,
    Custom
}

public sealed class VerificationResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<ResidualDetection> ResidualDetections { get; init; } = Array.Empty<ResidualDetection>();
    public IReadOnlyList<string> MetadataIssues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HiddenContentIssues { get; init; } = Array.Empty<string>();
    public TimeSpan ScanDuration { get; init; }
    public DateTime VerifiedAt { get; init; } = DateTime.UtcNow;
    public int CriticalResidualCount => ResidualDetections.Count(r => r.IsCritical);
    public int TotalResidualCount => ResidualDetections.Count;
}

public sealed class ResidualDetection
{
    public DetectionType Type { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox Location { get; init; } = new();
    public int PageNumber { get; init; }
    public bool IsCritical => Type.IsCritical();
}

public static class DetectionTypeExtensions
{
    public static bool IsCritical(this DetectionType type) => type switch
    {
        DetectionType.TcKimlikNo => true,
        DetectionType.Iban => true,
        DetectionType.TaxId => true,
        DetectionType.PassportNo => true,
        DetectionType.CreditCard => true,
        _ => false
    };
}