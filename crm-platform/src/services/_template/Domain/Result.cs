namespace CrmPlatform.ServiceTemplate.Domain;

/// <summary>
/// Discriminated union return type. Never throw exceptions across service boundaries.
/// All command handlers and application services must return Result&lt;T&gt;.
///
/// Usage:
///   return Result.Ok(entity);
///   return Result.Fail&lt;Lead&gt;("Lead not found", ResultErrorCode.NotFound);
///   if (result.IsFailure) return Results.NotFound(result.Error);
/// </summary>
public sealed class Result<T>
{
    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ResultError? Error { get; }

    private Result(T value)
    {
        Value = value;
        IsSuccess = true;
    }

    private Result(ResultError error)
    {
        Error = error;
        IsSuccess = false;
    }

    internal static Result<T> Ok(T value) => new(value);
    internal static Result<T> Fail(ResultError error) => new(error);
}

public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ResultError? Error { get; }

    private Result(bool success, ResultError? error = null)
    {
        IsSuccess = success;
        Error = error;
    }

    public static Result Ok() => new(true);
    public static Result Fail(string message, ResultErrorCode code = ResultErrorCode.General) =>
        new(false, new ResultError(message, code));

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string message, ResultErrorCode code = ResultErrorCode.General) =>
        Result<T>.Fail(new ResultError(message, code));
}

public sealed record ResultError(string Message, ResultErrorCode Code);

public enum ResultErrorCode
{
    General,
    NotFound,
    Conflict,
    Forbidden,
    ValidationError,
    ExternalServiceError,
    TenantMismatch,
}
