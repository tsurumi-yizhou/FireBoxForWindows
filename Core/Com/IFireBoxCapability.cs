using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Core.Com.Structs;

namespace Core.Com;

/// <summary>
/// Capability interface used by third-party programs to access AI features.
/// Arrays are passed as raw native memory blocks (IntPtr).
/// </summary>
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

    /// <summary>Returns native array buffer of VirtualModelInfoStruct. Caller must free.</summary>
    void ListVirtualModels(out IntPtr modelsArray, out int count);

    /// <summary>Returns native array buffer of ModelCandidateInfoStruct. Caller must free.</summary>
    void GetModelCandidates(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        out IntPtr candidatesArray,
        out int count);

    /// <summary>
    /// Synchronous chat completion.
    /// conversationText: caller-formatted conversation transcript text.
    /// </summary>
    void ChatCompletion(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string conversationText,
        float temperature,
        int maxOutputTokens,
        out ChatCompletionResultStruct result);

    /// <summary>
    /// Start streaming chat completion. Returns requestId for cancellation.
    /// </summary>
    long StartChatCompletionStream(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string conversationText,
        float temperature,
        int maxOutputTokens,
        IFireBoxStreamCallback callback);

    void CancelChatCompletion(long requestId);

    /// <summary>
    /// Create embeddings. inputArray: native array buffer of BSTR values.
    /// Caller allocates and frees input arrays.
    /// </summary>
    void CreateEmbeddings(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        IntPtr inputArray,
        out EmbeddingResultStruct result);

    /// <summary>
    /// Retrieve embedding vectors after CreateEmbeddings.
    /// embeddingRequestId: the ID returned in EmbeddingResultStruct.EmbeddingRequestId.
    /// indicesArray: native array buffer of int, flatVectorsArray: native array buffer of float.
    /// </summary>
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
