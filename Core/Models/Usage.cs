namespace Core.Models;

public sealed record Usage(long PromptTokens, long CompletionTokens, long TotalTokens);
