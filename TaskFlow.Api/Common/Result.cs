namespace TaskFlow.Api.Common;

public enum ResultStatus { Ok, NotFound, Conflict, Validation, Unauthorized }

/// <summary>
/// Outcome of a service operation, free of any HTTP concept. Controllers translate
/// the status into a status code; services never reference IActionResult.
/// </summary>
public record Result<T>(ResultStatus Status, T? Value, string? Error)
{
    public bool IsSuccess => Status == ResultStatus.Ok;

    public static Result<T> Ok(T value)               => new(ResultStatus.Ok, value, null);
    public static Result<T> NotFound(string error)    => new(ResultStatus.NotFound, default, error);
    public static Result<T> Conflict(string error)    => new(ResultStatus.Conflict, default, error);
    public static Result<T> Invalid(string error)     => new(ResultStatus.Validation, default, error);
    public static Result<T> Unauthorized(string error) => new(ResultStatus.Unauthorized, default, error);
}