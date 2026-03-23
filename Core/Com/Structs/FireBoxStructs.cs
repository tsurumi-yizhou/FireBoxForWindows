using System.Runtime.InteropServices;

namespace Core.Com.Structs;

[Guid(FireBoxGuids.UsageStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct UsageStruct
{
    public long PromptTokens;
    public long CompletionTokens;
    public long TotalTokens;
}

[Guid(FireBoxGuids.ProviderSelectionStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ProviderSelectionStruct
{
    public int ProviderId;
    public IntPtr ProviderType;  // BSTR
    public IntPtr ProviderName;  // BSTR
    public IntPtr ModelId;       // BSTR
}

[Guid(FireBoxGuids.FireBoxErrorStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct FireBoxErrorStruct
{
    public int Code;
    public IntPtr Message;         // BSTR
    public IntPtr ProviderType;    // BSTR
    public IntPtr ProviderModelId; // BSTR
}

[Guid(FireBoxGuids.ChatMessageStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ChatMessageStruct
{
    public IntPtr Role;    // BSTR
    public IntPtr Content; // BSTR
}

[Guid(FireBoxGuids.ChatAttachmentStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ChatAttachmentStruct
{
    public int MessageIndex;
    public int MediaFormat;    // 0=Image, 1=Video, 2=Audio
    public IntPtr MimeType;    // BSTR
    public IntPtr FileName;    // BSTR
    public long SizeBytes;
    public IntPtr Base64Data;  // BSTR
}

[Guid(FireBoxGuids.ChatCompletionResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ChatCompletionResultStruct
{
    public short HasResponse; // VARIANT_BOOL: -1 = true, 0 = false
    // Response fields (valid when HasResponse != 0)
    public IntPtr VirtualModelId;  // BSTR
    public IntPtr MessageRole;     // BSTR
    public IntPtr MessageContent;  // BSTR
    public IntPtr ReasoningText;   // BSTR
    public int SelectionProviderId;
    public IntPtr SelectionProviderType; // BSTR
    public IntPtr SelectionProviderName; // BSTR
    public IntPtr SelectionModelId;      // BSTR
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    public IntPtr FinishReason;    // BSTR
    // Error fields (valid when HasResponse == 0)
    public int ErrorCode;
    public IntPtr ErrorMessage;         // BSTR
    public IntPtr ErrorProviderType;    // BSTR
    public IntPtr ErrorProviderModelId; // BSTR
}

[Guid(FireBoxGuids.EmbeddingResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct EmbeddingResultStruct
{
    public short HasResponse; // VARIANT_BOOL
    public long EmbeddingRequestId; // unique ID for GetEmbeddingVectors
    public IntPtr VirtualModelId; // BSTR
    public int SelectionProviderId;
    public IntPtr SelectionProviderType; // BSTR
    public IntPtr SelectionProviderName; // BSTR
    public IntPtr SelectionModelId;      // BSTR
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    // Error fields
    public int ErrorCode;
    public IntPtr ErrorMessage;         // BSTR
    public IntPtr ErrorProviderType;    // BSTR
    public IntPtr ErrorProviderModelId; // BSTR
    // Embedding vectors returned via separate GetEmbeddingVectors call
}

[Guid(FireBoxGuids.FunctionCallResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct FunctionCallResultStruct
{
    public short HasResponse; // VARIANT_BOOL
    public IntPtr VirtualModelId; // BSTR
    public IntPtr OutputJson;     // BSTR
    public int SelectionProviderId;
    public IntPtr SelectionProviderType; // BSTR
    public IntPtr SelectionProviderName; // BSTR
    public IntPtr SelectionModelId;      // BSTR
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    public IntPtr FinishReason;   // BSTR
    // Error fields
    public int ErrorCode;
    public IntPtr ErrorMessage;         // BSTR
    public IntPtr ErrorProviderType;    // BSTR
    public IntPtr ErrorProviderModelId; // BSTR
}

[Guid(FireBoxGuids.VirtualModelInfoStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct VirtualModelInfoStruct
{
    public IntPtr VirtualModelId; // BSTR
    public IntPtr Strategy;       // BSTR
    public short Reasoning;       // VARIANT_BOOL
    public short ToolCalling;     // VARIANT_BOOL
    public int InputFormatsMask;  // bitmask: 1=Image, 2=Video, 4=Audio
    public int OutputFormatsMask;
    public short Available;       // VARIANT_BOOL
}

[Guid(FireBoxGuids.ModelCandidateInfoStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ModelCandidateInfoStruct
{
    public int ProviderId;
    public IntPtr ProviderType;  // BSTR
    public IntPtr ProviderName;  // BSTR
    public IntPtr BaseUrl;       // BSTR
    public IntPtr ModelId;       // BSTR
    public short EnabledInConfig;      // VARIANT_BOOL
    public short CapabilitySupported;  // VARIANT_BOOL
}
