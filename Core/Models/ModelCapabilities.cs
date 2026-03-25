namespace Core.Models;

public sealed record ModelCapabilities(
    bool Reasoning = false,
    bool ToolCalling = false,
    List<MediaFormat>? InputFormats = null,
    List<MediaFormat>? OutputFormats = null);
