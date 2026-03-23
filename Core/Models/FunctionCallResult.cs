namespace Core.Models;

public sealed record FunctionCallResult(
    FunctionCallResponse? Response,
    FireBoxError? Error)
{
    public bool IsSuccess => Response is not null;
}
