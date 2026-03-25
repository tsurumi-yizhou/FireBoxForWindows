namespace Core.Models;

public static class CapabilityRequestValidator
{
    private const long MaxAttachmentBytes = 8L * 1024L * 1024L;
    private const int MaxEmbeddingInputCharacters = 200000;

    public static string? Validate(ChatCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
            return "modelId must be non-empty.";

        if (request.Messages is not { Count: > 0 })
            return "messages must be non-empty.";

        foreach (var message in request.Messages)
        {
            if (message is null)
                return "messages must not contain null items.";

            if (!IsValidRole(message.Role))
                return $"message.role must be one of: system, user, assistant. Actual: '{message.Role ?? string.Empty}'.";

            if (message.Attachments is not { Count: > 0 })
                continue;

            foreach (var attachment in message.Attachments)
            {
                if (attachment is null)
                    return "attachments must not contain null items.";

                if (string.IsNullOrWhiteSpace(attachment.MimeType))
                    return "attachment.mimeType must be non-empty.";

                var attachmentSize = attachment.SizeBytes >= 0 ? attachment.SizeBytes : attachment.Data.LongLength;
                if (attachmentSize > MaxAttachmentBytes)
                    return "each attachment is limited to 8 MiB.";
            }
        }

        return null;
    }

    public static string? Validate(EmbeddingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
            return "modelId must be non-empty.";

        if (request.Input is not { Count: > 0 })
            return "input must be non-empty.";

        var totalCharacters = 0;
        foreach (var item in request.Input)
            totalCharacters += item?.Length ?? 0;

        if (totalCharacters > MaxEmbeddingInputCharacters)
            return "total character count across all input items must not exceed 200000.";

        return null;
    }

    public static string? Validate(FunctionCallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
            return "modelId must be non-empty.";

        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return "functionName must be non-empty.";

        if (string.IsNullOrWhiteSpace(request.InputJson))
            return "inputJson must be non-empty.";

        if (string.IsNullOrWhiteSpace(request.InputSchemaJson))
            return "inputSchemaJson must be non-empty.";

        if (string.IsNullOrWhiteSpace(request.OutputSchemaJson))
            return "outputSchemaJson must be non-empty.";

        return null;
    }

    private static bool IsValidRole(string? role) =>
        role is "system" or "user" or "assistant";
}
