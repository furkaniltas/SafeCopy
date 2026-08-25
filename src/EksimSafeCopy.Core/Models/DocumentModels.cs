namespace EksimSafeCopy.Core.Models;

using EksimSafeCopy.Core.Abstractions;

/// <summary>
/// Represents a document processed by Eksim SafeCopy.
/// Immutable domain model - never contains the original file content.
/// </summary>
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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Document name cannot be empty", nameof(name));
        if (pages == null)
            throw new ArgumentNullException(nameof(pages));

        Name = name;
        Format = format;
        Pages = pages.ToList().AsReadOnly();
    }

    public Document WithPages(IReadOnlyList<DocumentPage> pages)
        => new(Name, Format, pages) { Id = Id, Metadata = Metadata, Source = Source };

    public Document WithMetadata(DocumentMetadata metadata)
        => new(Name, Format, Pages) { Id = Id, Metadata = metadata ?? new(), Source = Source };

    public Document WithSource(SourceReference source)
        => new(Name, Format, Pages) { Id = Id, Metadata = Metadata, Source = source };
}

/// <summary>
/// Represents a single page within a document.
/// Immutable - coordinates and content cannot be modified after creation.
/// </summary>
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
    public CoordinateSystem? CoordinateSystem { get; init; }

    public DocumentPage() { }

    public DocumentPage(int width, int height, int pageNumber, double dpiX, double dpiY)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pageNumber < 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (dpiX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiX));
        if (dpiY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiY));
        
        Width = width;
        Height = height;
        PageNumber = pageNumber;
        DpiX = dpiX;
        DpiY = dpiY;
    }
}

/// <summary>
/// Represents a block of text within a page.
/// Can span multiple lines and contain multiple text spans.
/// </summary>
public sealed class TextBlock
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextSpan> Spans { get; init; } = Array.Empty<TextSpan>();
    public TextBlockType Type { get; init; } = TextBlockType.Paragraph;
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;
    public int PageNumber { get; init; }
    public int OrderIndex { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();

    public TextBlock() { }

    public TextBlock(int orderIndex, int pageNumber)
    {
        if (orderIndex < 0) throw new ArgumentOutOfRangeException(nameof(orderIndex));
        if (pageNumber < 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        
        OrderIndex = orderIndex;
        PageNumber = pageNumber;
    }
}

/// <summary>
/// Represents a contiguous span of text within a TextBlock.
/// Used for precise location of detected PII within text.
/// </summary>
public sealed class TextSpan
{
    public int StartIndex { get; init; }
    public int Length { get; init; }
    public string Text { get; init; } = string.Empty;
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public FontInfo? Font { get; init; }
    public TextStyle Style { get; init; } = new();
    public int BlockId { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();

    public TextSpan() { }

    public TextSpan(int startIndex, int length)
    {
        if (startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        
        StartIndex = startIndex;
        Length = length;
    }

    public int EndIndex => StartIndex + Length;

    public bool Overlaps(TextSpan other)
        => StartIndex < other.EndIndex && other.StartIndex < EndIndex;
}

/// <summary>
/// Represents a rectangular region on a page.
/// Value object with validation - immutable after creation.
/// </summary>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    public static readonly BoundingBox Empty = new();

    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double PageWidth { get; init; }
    public double PageHeight { get; init; }

    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double Area => Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public BoundingBox() { }

    public BoundingBox(double x, double y, double width, double height, double pageWidth = 0, double pageHeight = 0)
    {
        ValidateDimensions(width, height, pageWidth, pageHeight);
        X = x; Y = y; Width = width; Height = height;
        PageWidth = pageWidth; PageHeight = pageHeight;
    }

    private static void ValidateDimensions(double width, double height, double pageWidth, double pageHeight)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
            throw new ArgumentException("Width cannot be NaN or Infinity", nameof(width));
        if (double.IsNaN(height) || double.IsInfinity(height))
            throw new ArgumentException("Height cannot be NaN or Infinity", nameof(height));
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative");
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative");
        if (pageWidth < 0) throw new ArgumentOutOfRangeException(nameof(pageWidth), "PageWidth cannot be negative");
        if (pageHeight < 0) throw new ArgumentOutOfRangeException(nameof(pageHeight), "PageHeight cannot be negative");
    }

    public double RightEdge => X + Width;
    public double BottomEdge => Y + Height;

    public bool Contains(double x, double y)
        => !IsEmpty && x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool Contains(Point point) => Contains(point.X, point.Y);

    public bool Intersects(BoundingBox other)
    {
        if (IsEmpty || other.IsEmpty) return false;
        // Use < and > instead of <= and >= to consider edge-touching as intersecting
        return !(Right < other.X || X > other.Right || Bottom < other.Y || Y > other.Bottom);
    }

    public BoundingBox Intersect(BoundingBox other)
    {
        if (!Intersects(other)) return Empty;

        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top, PageWidth, PageHeight);
    }

    public BoundingBox Union(BoundingBox other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top,
            Math.Max(PageWidth, other.PageWidth), Math.Max(PageHeight, other.PageHeight));
    }

    public BoundingBox Expand(double margin)
        => new(X - margin, Y - margin, Width + 2 * margin, Height + 2 * margin, PageWidth, PageHeight);

    public BoundingBox Normalize(double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0) throw new ArgumentException("Page dimensions must be positive");
        return new BoundingBox(X / pageWidth, Y / pageHeight, Width / pageWidth, Height / pageHeight, 1, 1);
    }

    public BoundingBox Denormalize(double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0) throw new ArgumentException("Page dimensions must be positive");
        return new BoundingBox(X * pageWidth, Y * pageHeight, Width * pageWidth, Height * pageHeight, pageWidth, pageHeight);
    }

    public BoundingBox Transform(CoordinateTransform transform)
        => transform.Transform(this);

    public bool IsValid => !IsEmpty && 
        !double.IsNaN(X) && !double.IsNaN(Y) && 
        !double.IsNaN(Width) && !double.IsNaN(Height) &&
        !double.IsInfinity(X) && !double.IsInfinity(Y) &&
        !double.IsInfinity(Width) && !double.IsInfinity(Height);

    public bool IsWithinPageBounds
    {
        get
        {
            if (PageWidth <= 0 || PageHeight <= 0) return true;
            return X >= 0 && Y >= 0 && Right <= PageWidth && Bottom <= PageHeight;
        }
    }

    public bool Equals(BoundingBox other)
        => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height &&
           PageWidth == other.PageWidth && PageHeight == other.PageHeight;

    public override bool Equals(object? obj) => obj is BoundingBox bb && Equals(bb);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height, PageWidth, PageHeight);

    public static bool operator ==(BoundingBox left, BoundingBox right) => left.Equals(right);
    public static bool operator !=(BoundingBox left, BoundingBox right) => !left.Equals(right);

    public override string ToString() => $"[{X:F2}, {Y:F2}, {Width:F2}, {Height:F2}]";
}

/// <summary>
/// Represents a point in 2D space.
/// </summary>
public readonly struct Point : IEquatable<Point>
{
    public double X { get; init; }
    public double Y { get; init; }

    public Point(double x, double y) { X = x; Y = y; }

    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Point left, Point right) => left.Equals(right);
    public static bool operator !=(Point left, Point right) => !left.Equals(right);
}

/// <summary>
/// Defines the coordinate system used by a document or page.
/// PDF, Office, and OCR engines may use different coordinate systems.
/// </summary>
public sealed class CoordinateSystem
{
    public CoordinateOrigin Origin { get; init; } = CoordinateOrigin.TopLeft;
    public AxisDirection AxisX { get; init; } = AxisDirection.Right;
    public AxisDirection AxisY { get; init; } = AxisDirection.Down;
    public CoordinateUnit Unit { get; init; } = CoordinateUnit.Points;
    public int PageWidth { get; init; }
    public int PageHeight { get; init; }

    public CoordinateSystem() { }

    public CoordinateSystem(int pageWidth, int pageHeight, CoordinateOrigin origin = CoordinateOrigin.TopLeft, CoordinateUnit unit = CoordinateUnit.Points)
    {
        if (pageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pageWidth));
        if (pageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pageHeight));
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        Origin = origin;
        Unit = unit;
    }

    public BoundingBox Normalize(BoundingBox box)
        => box.Normalize(PageWidth, PageHeight);

    public BoundingBox Denormalize(BoundingBox box)
        => box.Denormalize(PageWidth, PageHeight);

    public Point ConvertPoint(Point point, CoordinateSystem target)
    {
        var normalizedX = (double)X / PageWidth;
        var normalizedY = (double)Y / PageHeight;
        return new Point(normalizedX * target.PageWidth, normalizedY * target.PageHeight);
    }

    public BoundingBox ConvertTo(BoundingBox box, CoordinateSystem target)
        => box.Normalize(PageWidth, PageHeight).Denormalize(target.PageWidth, target.PageHeight);

    public int X => Origin switch
    {
        CoordinateOrigin.TopLeft or CoordinateOrigin.BottomLeft => 0,
        CoordinateOrigin.TopRight or CoordinateOrigin.BottomRight => PageWidth,
        _ => 0
    };

    public int Y => Origin switch
    {
        CoordinateOrigin.TopLeft or CoordinateOrigin.TopRight => 0,
        CoordinateOrigin.BottomLeft or CoordinateOrigin.BottomRight => PageHeight,
        _ => 0
    };
}

public enum CoordinateOrigin
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

public enum AxisDirection
{
    Right,
    Left,
    Up,
    Down
}

public enum CoordinateUnit
{
    Points,
    Pixels,
    Inches,
    Millimeters,
    Normalized
}

/// <summary>
/// Transform for converting coordinates between systems.
/// </summary>
public readonly struct CoordinateTransform
{
    public double ScaleX { get; init; }
    public double ScaleY { get; init; }
    public double TranslateX { get; init; }
    public double TranslateY { get; init; }
    public double RotateDegrees { get; init; }

    public CoordinateTransform()
    {
        ScaleX = 1;
        ScaleY = 1;
        TranslateX = 0;
        TranslateY = 0;
        RotateDegrees = 0;
    }

    public CoordinateTransform(double scaleX, double scaleY, double translateX = 0, double translateY = 0, double rotateDegrees = 0)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
        TranslateX = translateX;
        TranslateY = translateY;
        RotateDegrees = rotateDegrees;
    }

    public static CoordinateTransform Identity => new();

    public BoundingBox Transform(BoundingBox box)
    {
        var x = box.X * ScaleX + TranslateX;
        var y = box.Y * ScaleY + TranslateY;
        var w = box.Width * ScaleX;
        var h = box.Height * ScaleY;
        return new BoundingBox(x, y, w, h, box.PageWidth, box.PageHeight);
    }

    public Point Transform(Point point)
        => new(point.X * ScaleX + TranslateX, point.Y * ScaleY + TranslateY);

    public static CoordinateTransform CreateScale(double scaleX, double scaleY)
        => new() { ScaleX = scaleX, ScaleY = scaleY };

    public static CoordinateTransform CreateTranslation(double dx, double dy)
        => new() { TranslateX = dx, TranslateY = dy };

    public static CoordinateTransform CreateRotation(double degrees)
        => new() { RotateDegrees = degrees };
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

    public DocumentMetadata WithFileInfo(long fileSize, string fileHash)
    {
        return new DocumentMetadata
        {
            Title = Title,
            Author = Author,
            Subject = Subject,
            Keywords = Keywords,
            Creator = Creator,
            Producer = Producer,
            Created = Created,
            Modified = Modified,
            FileSize = fileSize,
            FileHash = fileHash,
            CustomProperties = CustomProperties
        };
    }
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
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
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
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
}

public sealed class OcrLine
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
}

public sealed class OcrParagraph
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
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
    public BoundingBox Location { get; init; } = BoundingBox.Empty;
    public int PageNumber { get; init; }
    public SourceReference Source { get; init; } = new();
    public DetectionSource DetectionSource { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
    public DetectionState State { get; set; } = DetectionState.Detected;
    public string? Reason { get; init; }
    public string? Explanation { get; init; }
    public TextSpan? TextSpan { get; init; }

    public Detection() { }

    public Detection(double confidence)
    {
        if (confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1");
        Confidence = confidence;
    }
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
    public BoundingBox Location { get; init; } = BoundingBox.Empty;
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