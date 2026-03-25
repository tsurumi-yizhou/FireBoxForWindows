namespace Core.Models;

public sealed record Result<T>(
    T? Response,
    string? Error)
{
    public bool IsSuccess => Response is not null;
}
