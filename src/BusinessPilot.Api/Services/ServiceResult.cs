namespace BusinessPilot.Api.Services;

/// <summary>Application-layer outcome; HTTP status mapping stays in API controllers.</summary>
public sealed record ServiceResult<T>(T? Value, string? Error = null, bool NotFound = false, bool Conflict = false)
{
    public bool Succeeded => Error is null && !NotFound && !Conflict;
    public static ServiceResult<T> Success(T value) => new(value);
    public static ServiceResult<T> Failure(string error) => new(default, error);
    public static ServiceResult<T> Missing() => new(default, NotFound: true);
    public static ServiceResult<T> Duplicate(string error) => new(default, error, Conflict: true);
}
