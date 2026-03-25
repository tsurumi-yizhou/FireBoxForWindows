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

[Guid(FireBoxGuids.ChatCompletionResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct ChatCompletionResultStruct
{
    public short HasResponse;
    public IntPtr ModelId;
    public IntPtr MessageRole;
    public IntPtr MessageContent;
    public IntPtr ReasoningText;
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    public IntPtr FinishReason;
    public IntPtr ErrorMessage;
}

[Guid(FireBoxGuids.EmbeddingResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct EmbeddingResultStruct
{
    public short HasResponse;
    public IntPtr ModelId;
    public IntPtr IndicesArray;
    public IntPtr FlatVectorsArray;
    public int EmbeddingCount;
    public int VectorDimension;
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    public IntPtr ErrorMessage;
}

[Guid(FireBoxGuids.FunctionCallResultStructGuid)]
[StructLayout(LayoutKind.Sequential)]
public struct FunctionCallResultStruct
{
    public short HasResponse;
    public IntPtr ModelId;
    public IntPtr OutputJson;
    public long UsagePromptTokens;
    public long UsageCompletionTokens;
    public long UsageTotalTokens;
    public IntPtr FinishReason;
    public IntPtr ErrorMessage;
}
