namespace TaskFlow.Api.Common;

public enum ResultStatus { Ok, NotFound, Conflict, Validation, Unauthorized, Error }

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

    // Named InternalError, not Error: the record's own third positional parameter is already a
    // property named `Error` (the message text every other factory also populates), so a static
    // factory literally named `Error` collides with it (confirmed via `dotnet build`: CS0102 "The
    // type 'Result<T>' already contains a definition for 'Error'"). ResultStatus.Error is unaffected
    // (a different type, no collision) and keeps that name because it has no existing callers to
    // rename around and matches the sprint doc's own terminology for this status.
    public static Result<T> InternalError(string message) => new(ResultStatus.Error, default, message);
}