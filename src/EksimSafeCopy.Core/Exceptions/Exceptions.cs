namespace EksimSafeCopy.Core.Exceptions;

using EksimSafeCopy.Core.Abstractions;

public abstract class EksimSafeCopyException : Exception
{
    protected EksimSafeCopyException(string message) : base(message) { }
    protected EksimSafeCopyException(string message, Exception innerException) : base(message, innerException) { }
    public abstract string ErrorCode { get; }
}

public sealed class DocumentLoadException : EksimSafeCopyException
{
    public string FilePath { get; }
    public DocumentFormat Format { get; }
    
    public DocumentLoadException(string filePath, DocumentFormat format, string message) 
        : base($"Failed to load document '{filePath}': {message}") 
    { 
        FilePath = filePath; 
        Format = format; 
    }
    
    public DocumentLoadException(string filePath, DocumentFormat format, string message, Exception inner) 
        : base($"Failed to load document '{filePath}': {message}", inner) 
    { 
        FilePath = filePath; 
        Format = format; 
    }
    
    public override string ErrorCode => "DOCUMENT_LOAD_FAILED";
}

public sealed class DocumentFormatException : EksimSafeCopyException
{
    public string FilePath { get; }
    public DocumentFormat ExpectedFormat { get; }
    public DocumentFormat ActualFormat { get; }
    
    public DocumentFormatException(string filePath, DocumentFormat expected, DocumentFormat actual, string message) 
        : base($"Format mismatch for '{filePath}': expected {expected}, got {actual}. {message}") 
    { 
        FilePath = filePath; 
        ExpectedFormat = expected; 
        ActualFormat = actual; 
    }
    
    public override string ErrorCode => "FORMAT_MISMATCH";
}

public sealed class UnsupportedFormatException : EksimSafeCopyException
{
    public string FilePath { get; }
    public DocumentFormat Format { get; }
    
    public UnsupportedFormatException(string filePath, DocumentFormat format) 
        : base($"Unsupported document format: {format} for file '{filePath}'") 
    { 
        FilePath = filePath; 
        Format = format; 
    }
    
    public override string ErrorCode => "UNSUPPORTED_FORMAT";
}

public sealed class OcrException : EksimSafeCopyException
{
    public string EngineName { get; }
    
    public OcrException(string engineName, string message) 
        : base($"OCR engine '{engineName}' failed: {message}") 
    { 
        EngineName = engineName; 
    }
    
    public OcrException(string engineName, string message, Exception inner) 
        : base($"OCR engine '{engineName}' failed: {message}", inner) 
    { 
        EngineName = engineName; 
    }
    
    public override string ErrorCode => "OCR_FAILED";
}

public sealed class OcrUnavailableException : EksimSafeCopyException
{
    public string Language { get; }
    
    public OcrUnavailableException(string language) 
        : base($"OCR not available for language: {language}") 
    { 
        Language = language; 
    }
    
    public override string ErrorCode => "OCR_UNAVAILABLE";
}

public sealed class RenderingException : EksimSafeCopyException
{
    public DocumentFormat TargetFormat { get; }
    
    public RenderingException(DocumentFormat format, string message) 
        : base($"Rendering to {format} failed: {message}") 
    { 
        TargetFormat = format; 
    }
    
    public RenderingException(DocumentFormat format, string message, Exception inner) 
        : base($"Rendering to {format} failed: {message}", inner) 
    { 
        TargetFormat = format; 
    }
    
    public override string ErrorCode => "RENDERING_FAILED";
}

public sealed class VerificationException : EksimSafeCopyException
{
    public VerificationException(string message) : base(message) { }
    public VerificationException(string message, Exception inner) : base(message, inner) { }
    public override string ErrorCode => "VERIFICATION_FAILED";
}

public sealed class VerificationFailedException : EksimSafeCopyException
{
    public IReadOnlyList<Models.ResidualDetection> ResidualDetections { get; }
    public IReadOnlyList<string> MetadataIssues { get; }
    public IReadOnlyList<string> HiddenContentIssues { get; }
    
    public VerificationFailedException(Models.VerificationResult result) 
        : base($"Verification failed: {result.CriticalResidualCount} critical, {result.TotalResidualCount} total residual detections")
    { 
        ResidualDetections = result.ResidualDetections;
        MetadataIssues = result.MetadataIssues;
        HiddenContentIssues = result.HiddenContentIssues;
    }
    
    public override string ErrorCode => "VERIFICATION_FAILED_RESIDUAL_PII";
}

public sealed class OriginalDocumentModifiedException : EksimSafeCopyException
{
    public string FilePath { get; }
    public string Reason { get; }
    
    public OriginalDocumentModifiedException(string filePath, string reason) 
        : base($"SECURITY VIOLATION: Original document '{filePath}' was modified: {reason}") 
    { 
        FilePath = filePath; 
        Reason = reason; 
    }
    
    public override string ErrorCode => "ORIGINAL_DOCUMENT_MODIFIED";
}

public sealed class OriginalDocumentMissingException : EksimSafeCopyException
{
    public string FilePath { get; }
    
    public OriginalDocumentMissingException(string filePath) 
        : base($"Original document not found: '{filePath}'") 
    { 
        FilePath = filePath; 
    }
    
    public override string ErrorCode => "ORIGINAL_DOCUMENT_MISSING";
}

public sealed class SecurityException : EksimSafeCopyException
{
    public SecurityException(string message) : base(message) { }
    public SecurityException(string message, Exception inner) : base(message, inner) { }
    public override string ErrorCode => "SECURITY_VIOLATION";
}

public sealed class ConfigurationException : EksimSafeCopyException
{
    public string Key { get; }
    
    public ConfigurationException(string key, string message) 
        : base($"Configuration error for key '{key}': {message}") 
    { 
        Key = key; 
    }
    
    public override string ErrorCode => "CONFIGURATION_ERROR";
}

public sealed class TempWorkspaceException : EksimSafeCopyException
{
    public string WorkspacePath { get; }
    
    public TempWorkspaceException(string workspacePath, string message) 
        : base($"Temp workspace error at '{workspacePath}': {message}") 
    { 
        WorkspacePath = workspacePath; 
    }
    
    public override string ErrorCode => "TEMP_WORKSPACE_ERROR";
}

public sealed class CancellationRequestedException : EksimSafeCopyException
{
    public CancellationRequestedException(string operation) 
        : base($"Operation cancelled: {operation}") { }
    public override string ErrorCode => "OPERATION_CANCELLED";
}

public sealed class TimeoutException : EksimSafeCopyException
{
    public string Operation { get; }
    public TimeSpan Timeout { get; }
    
    public TimeoutException(string operation, TimeSpan timeout) 
        : base($"Operation timed out after {timeout.TotalSeconds}s: {operation}") 
    { 
        Operation = operation; 
        Timeout = timeout; 
    }
    
    public override string ErrorCode => "OPERATION_TIMEOUT";
}

public sealed class FileTooLargeException : EksimSafeCopyException
{
    public string FilePath { get; }
    public long FileSize { get; }
    public long MaxSize { get; }
    
    public FileTooLargeException(string filePath, long fileSize, long maxSize) 
        : base($"File '{filePath}' exceeds maximum size: {fileSize} > {maxSize}") 
    { 
        FilePath = filePath; 
        FileSize = fileSize; 
        MaxSize = maxSize; 
    }
    
    public override string ErrorCode => "FILE_TOO_LARGE";
}

public sealed class ZipBombException : EksimSafeCopyException
{
    public string FilePath { get; }
    public long CompressedSize { get; }
    public long UncompressedSize { get; }
    public int Ratio { get; }
    
    public ZipBombException(string filePath, long compressed, long uncompressed, int ratio) 
        : base($"ZIP bomb detected in '{filePath}': {compressed} -> {uncompressed} (ratio {ratio}:1)") 
    { 
        FilePath = filePath; 
        CompressedSize = compressed; 
        UncompressedSize = uncompressed; 
        Ratio = ratio; 
    }
    
    public override string ErrorCode => "ZIP_BOMB_DETECTED";
}

public sealed class PathTraversalException : EksimSafeCopyException
{
    public string AttemptedPath { get; }
    public string BasePath { get; }
    
    public PathTraversalException(string attemptedPath, string basePath) 
        : base($"Path traversal attempt: '{attemptedPath}' outside base '{basePath}'") 
    { 
        AttemptedPath = attemptedPath; 
        BasePath = basePath; 
    }
    
    public override string ErrorCode => "PATH_TRAVERSAL_ATTEMPT";
}