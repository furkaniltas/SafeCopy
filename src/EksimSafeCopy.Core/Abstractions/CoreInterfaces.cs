namespace EksimSafeCopy.Core.Abstractions;

using EksimSafeCopy.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

public interface IDocumentEngine
{
    Result<Document> Load(string filePath, CancellationToken cancellationToken = default);
    Result<Document> Load(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default);
    Task<Result<Document>> LoadAsync(string filePath, CancellationToken cancellationToken = default);
    Task<Result<Document>> LoadAsync(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default);
    
    Result<DocumentFormat> DetectFormat(string filePath);
    Result<DocumentFormat> DetectFormat(Stream stream);
    
    Result<IReadOnlyList<DocumentFormat>> GetSupportedFormats();
}

public interface IDetectionEngine
{
    Result<IReadOnlyList<Detection>> Detect(Document document, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Detection>>> DetectAsync(Document document, CancellationToken cancellationToken = default);
    
    Result<IReadOnlyList<IDetector>> GetDetectors();
    Result<IDetector?> GetDetector(DetectionType type);
}

public interface IDetector
{
    DetectionType Type { get; }
    string Name { get; }
    string Description { get; }
    bool IsEnabled { get; set; }
    double ConfidenceThreshold { get; set; }
    
    Result<IReadOnlyList<Detection>> Detect(Document document, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Detection>>> DetectAsync(Document document, CancellationToken cancellationToken = default);
}

public interface IOcrEngine
{
    bool IsAvailable { get; }
    string EngineName { get; }
    IReadOnlyList<string> SupportedLanguages { get; }
    
    Result<OcrResult> Recognize(Stream imageStream, string language, CancellationToken cancellationToken = default);
    Task<Result<OcrResult>> RecognizeAsync(Stream imageStream, string language, CancellationToken cancellationToken = default);
    
    Result<OcrResult> Recognize(byte[] imageData, string language, CancellationToken cancellationToken = default);
    Task<Result<OcrResult>> RecognizeAsync(byte[] imageData, string language, CancellationToken cancellationToken = default);
}

public interface IRenderer
{
    DocumentFormat TargetFormat { get; }
    
    Result<byte[]> Render(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default);
    Task<Result<byte[]>> RenderAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default);
    
    Result RenderToFile(Document document, IReadOnlyList<Detection> detections, RenderOptions options, string outputPath, CancellationToken cancellationToken = default);
    Task<Result> RenderToFileAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, string outputPath, CancellationToken cancellationToken = default);
}

public interface IVerificationEngine
{
    Result<VerificationResult> Verify(string filePath, DocumentFormat format, CancellationToken cancellationToken = default);
    Task<Result<VerificationResult>> VerifyAsync(string filePath, DocumentFormat format, CancellationToken cancellationToken = default);
    
    Result<VerificationResult> Verify(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default);
    Task<Result<VerificationResult>> VerifyAsync(Stream stream, DocumentFormat format, CancellationToken cancellationToken = default);

    // Partial masking aware verification
    Result<VerificationResult> Verify(string filePath, DocumentFormat format, IReadOnlyList<Detection> originalDetections, RenderOptions options, CancellationToken cancellationToken = default) => Verify(filePath, format, cancellationToken);
    Task<Result<VerificationResult>> VerifyAsync(string filePath, DocumentFormat format, IReadOnlyList<Detection> originalDetections, RenderOptions options, CancellationToken cancellationToken = default) => VerifyAsync(filePath, format, cancellationToken);
}

public sealed class RedactionOperation
{
    public string DetectionId { get; init; } = string.Empty;
    public DetectionType DetectionType { get; init; }
    public int PageNumber { get; init; }
    public TextSpan? TextSpan { get; init; }
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public CoordinateSystem? CoordinateSystem { get; init; }
    public RedactionStrategy Strategy { get; init; } = RedactionStrategy.TypeLabel;
    public string? ReplacementText { get; init; }
    public double Confidence { get; init; }
    public RedactionOperationState State { get; set; } = RedactionOperationState.Pending;
}

public enum RedactionStrategy
{
    FullRedaction,
    TypeLabel,
    PartialMask,
    Placeholder,
    Custom
}

public enum RedactionOperationState
{
    Pending,
    Applied,
    Failed,
    Skipped
}

public sealed class RedactionPlan
{
    public string DocumentId { get; init; } = string.Empty;
    public IReadOnlyList<RedactionOperation> Operations { get; init; } = Array.Empty<RedactionOperation>();
    public DocumentFormat Format { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public interface IRedactionPlanner
{
    Result<RedactionPlan> CreatePlan(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default);
    Task<Result<RedactionPlan>> CreatePlanAsync(Document document, IReadOnlyList<Detection> detections, RenderOptions options, CancellationToken cancellationToken = default);
}

public interface IRedactionStrategy
{
    RedactionStrategy Type { get; }
    string GetReplacementText(DetectionType type, RenderOptions options);
    string GetReplacementText(DetectionType type, string originalValue, RenderOptions options) => GetReplacementText(type, options);
    bool SupportsFormat(DocumentFormat format);
}

public interface IRedactor
{
    DocumentFormat TargetFormat { get; }
    Result<byte[]> Redact(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default);
    Task<Result<byte[]>> RedactAsync(byte[] documentBytes, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default);
    
    Result<byte[]> RedactToFile(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default);
    Task<Result<byte[]>> RedactToFileAsync(string inputPath, string outputPath, RedactionPlan plan, RenderOptions options, CancellationToken cancellationToken = default);
}

// Use Microsoft.Extensions.DependencyInjection abstractions
// IServiceCollection, IServiceProvider, ServiceDescriptor, ServiceLifetime

// Use Microsoft.Extensions.Configuration abstractions
// IConfiguration, IConfigurationSection, IConfigurationProvider

public interface IFileSystem
{
    Result<bool> Exists(string path);
    Result<Stream> OpenRead(string path);
    Result<Stream> OpenWrite(string path, bool overwrite = false);
    Result<long> GetSize(string path);
    Result<DateTime> GetLastWriteTimeUtc(string path);
    Result<string> ComputeHash(string path, HashAlgorithm algorithm = HashAlgorithm.SHA256);
    Result<IReadOnlyList<string>> EnumerateFiles(string directory, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly);
    Result CreateDirectory(string path);
    Result DeleteFile(string path);
    Result DeleteDirectory(string path, bool recursive = false);
    Result CopyFile(string source, string destination, bool overwrite = false);
    Result MoveFile(string source, string destination);
    Result<bool> IsReadOnly(string path);
    Result SetReadOnly(string path, bool readOnly);
    Result<string> GetTempFileName(string? extension = null);
    Result<string> GetTempDirectory();
}

public interface ITempWorkspace : IDisposable, IAsyncDisposable
{
    string RootPath { get; }
    string InputPath { get; }
    string ExtractedPath { get; }
    string OcrPath { get; }
    string OutputPath { get; }
    string VerificationPath { get; }
    
    Result<string> CreateInputFile(string originalName);
    Result<string> CreateExtractedFile(string name);
    Result<string> CreateOcrFile(string name);
    Result<string> CreateOutputFile(string name);
    Result<string> CreateVerificationFile(string name);
    
    Result Cleanup();
    Task<Result> CleanupAsync();
    
    Result<string> GetUniqueFileName(string directory, string prefix, string extension);
}

public enum DocumentFormat
{
    Unknown,
    Pdf,
    Docx,
    Xlsx,
    Txt,
    Udf,
    Png,
    Jpeg,
    Tiff,
    Bmp
}

public enum HashAlgorithm
{
    SHA256,
    SHA512,
    MD5
}

public enum SearchOption
{
    TopDirectoryOnly,
    AllDirectories
}