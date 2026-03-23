namespace Core.Models;

public sealed record ChatCompletionResult(
    ChatCompletionResponse? Response,
    FireBoxError? Error)
{
    public bool IsSuccess => Response is not null;
}
