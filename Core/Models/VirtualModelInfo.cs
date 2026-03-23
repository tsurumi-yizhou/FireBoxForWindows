namespace Core.Models;

public sealed record VirtualModelInfo(
    string VirtualModelId,
    string Strategy,
    ModelCapabilities Capabilities,
    List<ModelCandidateInfo> Candidates,
    bool Available);
