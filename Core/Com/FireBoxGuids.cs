namespace Core.Com;

/// <summary>
/// Well-known GUIDs for the FireBox COM server.
/// </summary>
public static class FireBoxGuids
{
    // Type library (generated from FireBox.idl via MIDL)
    public const string TypeLib = "dc51fb4b-bd5d-4613-8786-e154926b7035";
    public const string TypeLibVersion = "2.0";

    // Control — used by App to manage the service
    public const string ControlInterface = "e0df9d43-d19e-4125-9c9e-277020a62cb4";
    public const string ControlClass = "bce2f493-0f9b-473b-8df5-727bbf82cf7f";

    // Capability — used by third-party programs to access service features
    public const string CapabilityInterface = "9d611e53-2f5a-4987-870f-e58f58e4a7f2";
    public const string CapabilityClass = "b2a4e3a1-7254-4876-aca8-81f46b4f78a1";

    // Stream callback — implemented by client, called by service
    public const string StreamCallbackInterface = "a3c7f1d2-8b4e-4f6a-9d1c-5e2b8a0f3c7d";

    // Struct GUIDs for IRecordInfo (SAFEARRAY of UDT)
    public const string UsageStructGuid = "d1a2b3c4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";
    public const string ProviderSelectionStructGuid = "e2b3c4d5-f6a7-4b8c-9d0e-1f2a3b4c5d6e";
    public const string FireBoxErrorStructGuid = "f3c4d5e6-a7b8-4c9d-0e1f-2a3b4c5d6e7f";
    public const string ChatMessageStructGuid = "a4d5e6f7-b8c9-4d0e-1f2a-3b4c5d6e7f80";
    public const string ChatAttachmentStructGuid = "b5e6f7a8-c9d0-4e1f-2a3b-4c5d6e7f8091";
    public const string ChatCompletionResponseStructGuid = "c6f7a8b9-d0e1-4f2a-3b4c-5d6e7f8091a2";
    public const string ChatCompletionResultStructGuid = "d7a8b9c0-e1f2-4a3b-4c5d-6e7f8091a2b3";
    public const string ChatStreamEventStructGuid = "e8b9c0d1-f2a3-4b4c-5d6e-7f8091a2b3c4";
    public const string EmbeddingResultStructGuid = "f9c0d1e2-a3b4-4c5d-6e7f-8091a2b3c4d5";
    public const string FunctionCallResultStructGuid = "a0d1e2f3-b4c5-4d6e-7f80-91a2b3c4d5e6";
    public const string VirtualModelInfoStructGuid = "b1e2f3a4-c5d6-4e7f-8091-a2b3c4d5e6f7";
    public const string ModelCandidateInfoStructGuid = "c2f3a4b5-d6e7-4f80-91a2-b3c4d5e6f7a8";
}
