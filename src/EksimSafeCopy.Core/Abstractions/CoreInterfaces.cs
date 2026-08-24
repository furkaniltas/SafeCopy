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