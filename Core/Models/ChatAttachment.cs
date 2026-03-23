namespace Core.Models;

public sealed record ChatAttachment(
    ModelMediaFormat MediaFormat,
    string MimeType,
    string? FileName,
    byte[] Data,
    long SizeBytes);
