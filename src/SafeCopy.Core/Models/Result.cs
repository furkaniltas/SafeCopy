namespace SafeCopy.Core.Models;

public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = default;
        _isSuccess = true;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
        _isSuccess = false;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;

    public T Value
    {
        get
        {
            if (_isSuccess) return _value!;
            throw new InvalidOperationException("Cannot access Value on failed result");
        }
    }

    public Error Error
    {
        get
        {
            if (!_isSuccess) return _error;
            throw new InvalidOperationException("Cannot access Error on successful result");
        }
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        => _isSuccess ? onSuccess(_value!) : onFailure(_error);

    public void Match(Action<T> onSuccess, Action<Error> onFailure)
    {
        if (_isSuccess) onSuccess(_value!);
        else onFailure(_error);
    }
}

public readonly struct Result
{
    private readonly Error _error;
    private readonly bool _isSuccess;

    public Result() { _isSuccess = true; _error = default; }
    private Result(Error error) { _isSuccess = false; _error = error; }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;
    public Error Error => _isSuccess ? throw new InvalidOperationException() : _error;

    public static Result Success() => new();
    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);

    public void Match(Action onSuccess, Action<Error> onFailure)
    {
        if (_isSuccess) onSuccess();
        else onFailure(_error);
    }

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
        => _isSuccess ? onSuccess() : onFailure(_error);
}

public readonly record struct Error(string Code, string Message, object? Metadata = null)
{
    public static Error None => new(string.Empty, string.Empty);
    public static Error Validation(string message, object? metadata = null) => new("VALIDATION_ERROR", message, metadata);
    public static Error NotFound(string message, object? metadata = null) => new("NOT_FOUND", message, metadata);
    public static Error Conflict(string message, object? metadata = null) => new("CONFLICT", message, metadata);
    public static Error Internal(string message, object? metadata = null) => new("INTERNAL_ERROR", message, metadata);
    public static Error Unauthorized(string message, object? metadata = null) => new("UNAUTHORIZED", message, metadata);
    public static Error Forbidden(string message, object? metadata = null) => new("FORBIDDEN", message, metadata);
    public static Error Timeout(string message, object? metadata = null) => new("TIMEOUT", message, metadata);
    public static Error Cancelled(string message, object? metadata = null) => new("CANCELLED", message, metadata);
    public static Error IoError(string message, object? metadata = null) => new("IO_ERROR", message, metadata);
    public static Error FormatError(string message, object? metadata = null) => new("FORMAT_ERROR", message, metadata);
    public static Error SecurityError(string message, object? metadata = null) => new("SECURITY_ERROR", message, metadata);
}