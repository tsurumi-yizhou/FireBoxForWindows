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

    [return: MarshalAs(UnmanagedType.BStr)]
    string ListModels();

    void ChatCompletion(
        [MarshalAs(UnmanagedType.BStr)] string modelId,
        [MarshalAs(UnmanagedType.BStr)] string messagesJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        out ChatCompletionResultStruct result);

    long StartChatCompletionStream(
        [MarshalAs(UnmanagedType.BStr)] string modelId,
        [MarshalAs(UnmanagedType.BStr)] string messagesJson,
        float temperature,
        int maxOutputTokens,
        int reasoningEffort,
        IFireBoxStreamCallback callback);

    void CancelChatCompletion(long requestId);

    void CreateEmbeddings(
        [MarshalAs(UnmanagedType.BStr)] string modelId,
        IntPtr inputArray,
        out EmbeddingResultStruct result);

    void CallFunction(
        [MarshalAs(UnmanagedType.BStr)] string modelId,
        [MarshalAs(UnmanagedType.BStr)] string functionName,
        [MarshalAs(UnmanagedType.BStr)] string functionDescription,
        [MarshalAs(UnmanagedType.BStr)] string inputJson,
        [MarshalAs(UnmanagedType.BStr)] string inputSchemaJson,
        [MarshalAs(UnmanagedType.BStr)] string outputSchemaJson,
        float temperature,
        int maxOutputTokens,
        out FunctionCallResultStruct result);
}
