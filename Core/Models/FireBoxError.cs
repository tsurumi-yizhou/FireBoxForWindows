namespace Core.Models;

public sealed record FireBoxError(
    int Code,
    string Message,
    string? ProviderType = null,
    string? ProviderModelId = null)
{
    public const int Security = 1;
    public const int InvalidArgument = 2;
    public const int NoRoute = 3;
    public const int NoCandidate = 4;
    public const int ProviderError = 5;
    public const int Timeout = 6;
    public const int Internal = 7;
    public const int Cancelled = 8;
}
