namespace EksimSafeCopy.Core.Abstractions;

using EksimSafeCopy.Core.Models;

public interface IOperationContext
{
    CancellationToken CancellationToken { get; }
    TimeSpan? Timeout { get; }
    IReadOnlyDictionary<string, object> Properties { get; }
    IOperationCallback? Callback { get; }
}

public interface IOperationCallback
{
    void ReportProgress(double percent, string? message = null);
    void ReportWarning(string message);
    void ReportError(string message);
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
    public IOperationCallback? Callback { get; }
    
    public OperationContext(
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        IOperationCallback? callback = null,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        CancellationToken = cancellationToken;
        Timeout = timeout;
        Callback = callback;
        Properties = properties ?? new Dictionary<string, object>();
    }
    
    public OperationContext WithCancellation(CancellationToken token)
        => new(token, Timeout, Callback, Properties);
    
    public OperationContext WithTimeout(TimeSpan timeout)
        => new(CancellationToken, timeout, Callback, Properties);
    
    public OperationContext WithCallback(IOperationCallback? callback)
        => new(CancellationToken, Timeout, callback, Properties);
    
    public OperationContext WithProperty(string key, object value)
    {
        var props = new Dictionary<string, object>(Properties) { [key] = value };
        return new(CancellationToken, Timeout, Callback, props);
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