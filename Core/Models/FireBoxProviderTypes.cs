namespace Core.Models;

public static class FireBoxProviderTypes
{
    public const string OpenAI = "OpenAI";
    public const string Anthropic = "Anthropic";
    public const string Gemini = "Gemini";

    public static IReadOnlyList<string> SupportedValues { get; } =
    [
        OpenAI,
        Anthropic,
        Gemini,
    ];
}