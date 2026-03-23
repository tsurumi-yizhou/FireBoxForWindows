namespace Core.Models;

public sealed record ModelCapabilities(
    bool Reasoning = false,
    bool ToolCalling = false,
    List<ModelMediaFormat>? InputFormats = null,
    List<ModelMediaFormat>? OutputFormats = null);
