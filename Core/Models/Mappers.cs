using System.Runtime.InteropServices;
using Core.Com.Structs;

namespace Core.Models;

/// <summary>
/// Converts between COM interop structs (IntPtr/BSTR) and high-level C# records.
/// </summary>
public static class Mappers
{
    // --- BSTR helpers ---

    private static string BstrToString(IntPtr bstr) =>
        bstr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(bstr);

    private static string? BstrToStringNullable(IntPtr bstr) =>
        bstr == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(bstr);

    private static IntPtr StringToBstr(string? value) =>
        value is null ? IntPtr.Zero : Marshal.StringToBSTR(value);

    // --- From COM struct → C# record ---

    public static Usage ToModel(in UsageStruct s) =>
        new(s.PromptTokens, s.CompletionTokens, s.TotalTokens);

    public static ProviderSelection ToModel(in ProviderSelectionStruct s) =>
        new(s.ProviderId, BstrToString(s.ProviderType), BstrToString(s.ProviderName), BstrToString(s.ModelId));

    public static FireBoxError ToModel(in FireBoxErrorStruct s) =>
        new(s.Code, BstrToString(s.Message), BstrToStringNullable(s.ProviderType), BstrToStringNullable(s.ProviderModelId));

    public static ChatMessage ToModel(in ChatMessageStruct s) =>
        new(BstrToString(s.Role), BstrToString(s.Content));

    public static ChatAttachment ToModel(in ChatAttachmentStruct s) =>
        new(
            (ModelMediaFormat)s.MediaFormat,
            BstrToString(s.MimeType),
            BstrToStringNullable(s.FileName),
            s.Base64Data != IntPtr.Zero ? Convert.FromBase64String(BstrToString(s.Base64Data)) : [],
            s.SizeBytes);

    public static VirtualModelInfo ToModel(in VirtualModelInfoStruct s) =>
        new(
            BstrToString(s.VirtualModelId),
            BstrToString(s.Strategy),
            new ModelCapabilities(
                s.Reasoning != 0,
                s.ToolCalling != 0,
                ModelMediaFormatMask.FromMask(s.InputFormatsMask),
                ModelMediaFormatMask.FromMask(s.OutputFormatsMask)),
            [], // Candidates loaded separately via GetModelCandidates
            s.Available != 0);

    public static ModelCandidateInfo ToModel(in ModelCandidateInfoStruct s) =>
        new(
            s.ProviderId,
            BstrToString(s.ProviderType),
            BstrToString(s.ProviderName),
            BstrToString(s.BaseUrl),
            BstrToString(s.ModelId),
            s.EnabledInConfig != 0,
            s.CapabilitySupported != 0);

    public static ChatCompletionResult ToModel(in ChatCompletionResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var response = new ChatCompletionResponse(
                BstrToString(s.VirtualModelId),
                new ChatMessage(BstrToString(s.MessageRole), BstrToString(s.MessageContent)),
                BstrToStringNullable(s.ReasoningText),
                new ProviderSelection(
                    s.SelectionProviderId,
                    BstrToString(s.SelectionProviderType),
                    BstrToString(s.SelectionProviderName),
                    BstrToString(s.SelectionModelId)),
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens),
                BstrToString(s.FinishReason));
            return new ChatCompletionResult(response, null);
        }
        else
        {
            var error = new FireBoxError(
                s.ErrorCode,
                BstrToString(s.ErrorMessage),
                BstrToStringNullable(s.ErrorProviderType),
                BstrToStringNullable(s.ErrorProviderModelId));
            return new ChatCompletionResult(null, error);
        }
    }

    public static EmbeddingResult ToModel(in EmbeddingResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var response = new EmbeddingResponse(
                BstrToString(s.VirtualModelId),
                [], // Vectors loaded separately via GetEmbeddingVectors
                new ProviderSelection(
                    s.SelectionProviderId,
                    BstrToString(s.SelectionProviderType),
                    BstrToString(s.SelectionProviderName),
                    BstrToString(s.SelectionModelId)),
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens));
            return new EmbeddingResult(response, null);
        }
        else
        {
            var error = new FireBoxError(
                s.ErrorCode,
                BstrToString(s.ErrorMessage),
                BstrToStringNullable(s.ErrorProviderType),
                BstrToStringNullable(s.ErrorProviderModelId));
            return new EmbeddingResult(null, error);
        }
    }

    public static FunctionCallResult ToModel(in FunctionCallResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var response = new FunctionCallResponse(
                BstrToString(s.VirtualModelId),
                BstrToString(s.OutputJson),
                new ProviderSelection(
                    s.SelectionProviderId,
                    BstrToString(s.SelectionProviderType),
                    BstrToString(s.SelectionProviderName),
                    BstrToString(s.SelectionModelId)),
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens),
                BstrToString(s.FinishReason));
            return new FunctionCallResult(response, null);
        }
        else
        {
            var error = new FireBoxError(
                s.ErrorCode,
                BstrToString(s.ErrorMessage),
                BstrToStringNullable(s.ErrorProviderType),
                BstrToStringNullable(s.ErrorProviderModelId));
            return new FunctionCallResult(null, error);
        }
    }

    // --- From C# record → COM struct ---

    public static ChatMessageStruct ToStruct(ChatMessage m) =>
        new() { Role = StringToBstr(m.Role), Content = StringToBstr(m.Content) };

    public static ChatAttachmentStruct ToStruct(ChatAttachment a, int messageIndex) =>
        new()
        {
            MessageIndex = messageIndex,
            MediaFormat = (int)a.MediaFormat,
            MimeType = StringToBstr(a.MimeType),
            FileName = StringToBstr(a.FileName),
            SizeBytes = a.SizeBytes,
            Base64Data = StringToBstr(Convert.ToBase64String(a.Data)),
        };

    public static VirtualModelInfoStruct ToStruct(VirtualModelInfo m) =>
        new()
        {
            VirtualModelId = StringToBstr(m.VirtualModelId),
            Strategy = StringToBstr(m.Strategy),
            Reasoning = (short)(m.Capabilities.Reasoning ? -1 : 0),
            ToolCalling = (short)(m.Capabilities.ToolCalling ? -1 : 0),
            InputFormatsMask = ModelMediaFormatMask.ToMask(m.Capabilities.InputFormats),
            OutputFormatsMask = ModelMediaFormatMask.ToMask(m.Capabilities.OutputFormats),
            Available = (short)(m.Available ? -1 : 0),
        };

    public static ModelCandidateInfoStruct ToStruct(ModelCandidateInfo c) =>
        new()
        {
            ProviderId = c.ProviderId,
            ProviderType = StringToBstr(c.ProviderType),
            ProviderName = StringToBstr(c.ProviderName),
            BaseUrl = StringToBstr(c.BaseUrl),
            ModelId = StringToBstr(c.ModelId),
            EnabledInConfig = (short)(c.EnabledInConfig ? -1 : 0),
            CapabilitySupported = (short)(c.CapabilitySupported ? -1 : 0),
        };

    public static void FreeBstrFields(in ChatMessageStruct s)
    {
        Marshal.FreeBSTR(s.Role);
        Marshal.FreeBSTR(s.Content);
    }

    public static void FreeBstrFields(in ChatAttachmentStruct s)
    {
        Marshal.FreeBSTR(s.MimeType);
        Marshal.FreeBSTR(s.FileName);
        Marshal.FreeBSTR(s.Base64Data);
    }

    public static void FreeBstrFields(in VirtualModelInfoStruct s)
    {
        Marshal.FreeBSTR(s.VirtualModelId);
        Marshal.FreeBSTR(s.Strategy);
    }

    public static void FreeBstrFields(in ModelCandidateInfoStruct s)
    {
        Marshal.FreeBSTR(s.ProviderType);
        Marshal.FreeBSTR(s.ProviderName);
        Marshal.FreeBSTR(s.BaseUrl);
        Marshal.FreeBSTR(s.ModelId);
    }

    public static ChatCompletionResultStruct ToStruct(ChatCompletionResult r)
    {
        var s = new ChatCompletionResultStruct();
        if (r.Response is { } resp)
        {
            s.HasResponse = -1; // VARIANT_TRUE
            s.VirtualModelId = StringToBstr(resp.VirtualModelId);
            s.MessageRole = StringToBstr(resp.Message.Role);
            s.MessageContent = StringToBstr(resp.Message.Content);
            s.ReasoningText = StringToBstr(resp.ReasoningText);
            s.SelectionProviderId = resp.Selection.ProviderId;
            s.SelectionProviderType = StringToBstr(resp.Selection.ProviderType);
            s.SelectionProviderName = StringToBstr(resp.Selection.ProviderName);
            s.SelectionModelId = StringToBstr(resp.Selection.ModelId);
            s.UsagePromptTokens = resp.Usage.PromptTokens;
            s.UsageCompletionTokens = resp.Usage.CompletionTokens;
            s.UsageTotalTokens = resp.Usage.TotalTokens;
            s.FinishReason = StringToBstr(resp.FinishReason);
        }
        else if (r.Error is { } err)
        {
            s.HasResponse = 0;
            s.ErrorCode = err.Code;
            s.ErrorMessage = StringToBstr(err.Message);
            s.ErrorProviderType = StringToBstr(err.ProviderType);
            s.ErrorProviderModelId = StringToBstr(err.ProviderModelId);
        }
        return s;
    }

    public static EmbeddingResultStruct ToStruct(EmbeddingResult r)
    {
        var s = new EmbeddingResultStruct();
        if (r.Response is { } resp)
        {
            s.HasResponse = -1;
            s.VirtualModelId = StringToBstr(resp.VirtualModelId);
            s.SelectionProviderId = resp.Selection.ProviderId;
            s.SelectionProviderType = StringToBstr(resp.Selection.ProviderType);
            s.SelectionProviderName = StringToBstr(resp.Selection.ProviderName);
            s.SelectionModelId = StringToBstr(resp.Selection.ModelId);
            s.UsagePromptTokens = resp.Usage.PromptTokens;
            s.UsageCompletionTokens = resp.Usage.CompletionTokens;
            s.UsageTotalTokens = resp.Usage.TotalTokens;
        }
        else if (r.Error is { } err)
        {
            s.HasResponse = 0;
            s.ErrorCode = err.Code;
            s.ErrorMessage = StringToBstr(err.Message);
            s.ErrorProviderType = StringToBstr(err.ProviderType);
            s.ErrorProviderModelId = StringToBstr(err.ProviderModelId);
        }
        return s;
    }

    public static FunctionCallResultStruct ToStruct(FunctionCallResult r)
    {
        var s = new FunctionCallResultStruct();
        if (r.Response is { } resp)
        {
            s.HasResponse = -1;
            s.VirtualModelId = StringToBstr(resp.VirtualModelId);
            s.OutputJson = StringToBstr(resp.OutputJson);
            s.SelectionProviderId = resp.Selection.ProviderId;
            s.SelectionProviderType = StringToBstr(resp.Selection.ProviderType);
            s.SelectionProviderName = StringToBstr(resp.Selection.ProviderName);
            s.SelectionModelId = StringToBstr(resp.Selection.ModelId);
            s.UsagePromptTokens = resp.Usage.PromptTokens;
            s.UsageCompletionTokens = resp.Usage.CompletionTokens;
            s.UsageTotalTokens = resp.Usage.TotalTokens;
            s.FinishReason = StringToBstr(resp.FinishReason);
        }
        else if (r.Error is { } err)
        {
            s.HasResponse = 0;
            s.ErrorCode = err.Code;
            s.ErrorMessage = StringToBstr(err.Message);
            s.ErrorProviderType = StringToBstr(err.ProviderType);
            s.ErrorProviderModelId = StringToBstr(err.ProviderModelId);
        }
        return s;
    }

    /// <summary>
    /// Frees BSTR fields in a struct to prevent memory leaks.
    /// Call after consuming a struct received from COM.
    /// </summary>
    public static void FreeBstrFields(in ChatCompletionResultStruct s)
    {
        Marshal.FreeBSTR(s.VirtualModelId);
        Marshal.FreeBSTR(s.MessageRole);
        Marshal.FreeBSTR(s.MessageContent);
        Marshal.FreeBSTR(s.ReasoningText);
        Marshal.FreeBSTR(s.SelectionProviderType);
        Marshal.FreeBSTR(s.SelectionProviderName);
        Marshal.FreeBSTR(s.SelectionModelId);
        Marshal.FreeBSTR(s.FinishReason);
        Marshal.FreeBSTR(s.ErrorMessage);
        Marshal.FreeBSTR(s.ErrorProviderType);
        Marshal.FreeBSTR(s.ErrorProviderModelId);
    }

    public static void FreeBstrFields(in EmbeddingResultStruct s)
    {
        Marshal.FreeBSTR(s.VirtualModelId);
        Marshal.FreeBSTR(s.SelectionProviderType);
        Marshal.FreeBSTR(s.SelectionProviderName);
        Marshal.FreeBSTR(s.SelectionModelId);
        Marshal.FreeBSTR(s.ErrorMessage);
        Marshal.FreeBSTR(s.ErrorProviderType);
        Marshal.FreeBSTR(s.ErrorProviderModelId);
    }

    public static void FreeBstrFields(in FunctionCallResultStruct s)
    {
        Marshal.FreeBSTR(s.VirtualModelId);
        Marshal.FreeBSTR(s.OutputJson);
        Marshal.FreeBSTR(s.SelectionProviderType);
        Marshal.FreeBSTR(s.SelectionProviderName);
        Marshal.FreeBSTR(s.SelectionModelId);
        Marshal.FreeBSTR(s.FinishReason);
        Marshal.FreeBSTR(s.ErrorMessage);
        Marshal.FreeBSTR(s.ErrorProviderType);
        Marshal.FreeBSTR(s.ErrorProviderModelId);
    }
}
