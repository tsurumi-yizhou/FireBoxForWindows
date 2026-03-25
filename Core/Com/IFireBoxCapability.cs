using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Com.Structs;

namespace Core.Com;

[GeneratedComInterface]
[Guid(FireBoxGuids.CapabilityInterface)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IFireBoxCapability
{
    [return: MarshalAs(UnmanagedType.BStr)]
    string Ping([MarshalAs(UnmanagedType.BStr)] string message);

    void GetVirtualModelCount(out int count);

    void GetVirtualModelAt(
        int index,
        [MarshalAs(UnmanagedType.BStr)] out string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] out string strategy,
        out int reasoning,
        out int toolCalling,
        out int inputFormatsMask,
        out int outputFormatsMask,
        out int available);

    void ListVirtualModels(out IntPtr modelsArray, out int count);

    void GetModelCandidates(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        out IntPtr candidatesArray,
        out int count);

    void ChatCompletion(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string messagesJson,
        [MarshalAs(UnmanagedType.BStr)] string? attachmentsJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        out ChatCompletionResultStruct result);

    long StartChatCompletionStream(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string messagesJson,
        [MarshalAs(UnmanagedType.BStr)] string? attachmentsJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        IFireBoxStreamCallback callback);

    void CancelChatCompletion(long requestId);

    void CreateEmbeddings(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        IntPtr inputArray,
        out EmbeddingResultStruct result);

    void GetEmbeddingVectors(
        long embeddingRequestId,
        out IntPtr indicesArray,
        out IntPtr flatVectorsArray,
        out int vectorDimension);

    void CallFunction(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string functionName,
        [MarshalAs(UnmanagedType.BStr)] string functionDescription,
        [MarshalAs(UnmanagedType.BStr)] string inputJson,
        [MarshalAs(UnmanagedType.BStr)] string inputSchemaJson,
        [MarshalAs(UnmanagedType.BStr)] string outputSchemaJson,
        float temperature,
        int maxOutputTokens,
        out FunctionCallResultStruct result);
}
