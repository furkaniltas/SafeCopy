namespace EksimSafeCopy.Core.Abstractions;

using EksimSafeCopy.Core.Models;

public interface IOperationContext
{
    CancellationToken CancellationToken { get; }
    TimeSpan? Timeout { get; }
    IReadOnlyDictionary<string, object> Properties { get; }
    ILogger Logger { get; }
}

public interface ILogger
{
    void Log(LogLevel level, string message, params object[] args);
    void Log(LogLevel level, Exception exception, string message, params object[] args);
    bool IsEnabled(LogLevel level);
    IDisposable BeginScope(string name, params object[] args);
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

public static class CancellationTokenExtensions
{
    public static CancellationToken WithTimeout(this CancellationToken token, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return token;
        
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        return cts.Token;
    }
    
    public static CancellationToken WithTimeout(this CancellationToken token, int milliseconds)
        => token.WithTimeout(TimeSpan.FromMilliseconds(milliseconds));
    
    public static async Task<Result<T>> WithCancellation<T>(this Task<Result<T>> task, CancellationToken token)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Result<T>.Failure(Error.Cancelled("Operation was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(Error.Internal(ex.Message, ex));
        }
    }
    
    public static async Task<Result> WithCancellation(this Task<Result> task, CancellationToken token)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Result.Failure(Error.Cancelled("Operation was cancelled"));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal(ex.Message, ex));
        }
    }
}

public sealed class OperationContext : IOperationContext
{
    public CancellationToken CancellationToken { get; }
    public TimeSpan? Timeout { get; }
    public IReadOnlyDictionary<string, object> Properties { get; }
    public ILogger Logger { get; }
    
    public OperationContext(
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        CancellationToken = cancellationToken;
        Timeout = timeout;
        Logger = logger ?? NullLogger.Instance;
        Properties = properties ?? new Dictionary<string, object>();
    }
    
    public OperationContext WithCancellation(CancellationToken token)
        => new(token, Timeout, Logger, Properties);
    
    public OperationContext WithTimeout(TimeSpan timeout)
        => new(CancellationToken, timeout, Logger, Properties);
    
    public OperationContext WithLogger(ILogger logger)
        => new(CancellationToken, Timeout, logger, Properties);
    
    public OperationContext WithProperty(string key, object value)
    {
        var props = new Dictionary<string, object>(Properties) { [key] = value };
        return new(CancellationToken, Timeout, Logger, props);
    }
}

public sealed class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new();
    
    private NullLogger() { }
    
    public void Log(LogLevel level, string message, params object[] args) { }
    public void Log(LogLevel level, Exception exception, string message, params object[] args) { }
    public bool IsEnabled(LogLevel level) => false;
    public IDisposable BeginScope(string name, params object[] args) => NullScope.Instance;
    
    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        private NullScope() { }
        public void Dispose() { }
    }
}

public interface ITimeoutPolicy
{
    TimeSpan DefaultTimeout { get; }
    TimeSpan GetTimeout(string operationName);
}

public sealed class TimeoutPolicy : ITimeoutPolicy
{
    private readonly Dictionary<string, TimeSpan> _timeouts = new(StringComparer.OrdinalIgnoreCase);
    
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);
    
    public TimeoutPolicy AddTimeout(string operationName, TimeSpan timeout)
    {
        _timeouts[operationName] = timeout;
        return this;
    }
    
    public TimeSpan GetTimeout(string operationName)
        => _timeouts.TryGetValue(operationName, out var timeout) ? timeout : DefaultTimeout;
}

public static class TimeoutPolicies
{
    public static readonly ITimeoutPolicy DocumentLoad = new TimeoutPolicy()
        .AddTimeout("Pdf.Load", TimeSpan.FromMinutes(2))
        .AddTimeout("Docx.Load", TimeSpan.FromMinutes(1))
        .AddTimeout("Xlsx.Load", TimeSpan.FromMinutes(1))
        .AddTimeout("Txt.Load", TimeSpan.FromSeconds(30))
        .AddTimeout("Image.Load", TimeSpan.FromSeconds(10));
    
    public static readonly ITimeoutPolicy Ocr = new TimeoutPolicy()
        .AddTimeout("Ocr.Recognize", TimeSpan.FromMinutes(3))
        .AddTimeout("Ocr.Preprocess", TimeSpan.FromSeconds(30));
    
    public static readonly ITimeoutPolicy Detection = new TimeoutPolicy()
        .AddTimeout("Detection.Run", TimeSpan.FromMinutes(2));
    
    public static readonly ITimeoutPolicy Rendering = new TimeoutPolicy()
        .AddTimeout("Pdf.Render", TimeSpan.FromMinutes(2))
        .AddTimeout("Docx.Render", TimeSpan.FromMinutes(1))
        .AddTimeout("Xlsx.Render", TimeSpan.FromMinutes(1));
    
    public static readonly ITimeoutPolicy Verification = new TimeoutPolicy()
        .AddTimeout("Verify.Scan", TimeSpan.FromMinutes(2));
}