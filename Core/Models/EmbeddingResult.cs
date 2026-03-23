namespace Core.Models;

public sealed record EmbeddingResult(
    EmbeddingResponse? Response,
    FireBoxError? Error)
{
    public bool IsSuccess => Response is not null;
}
