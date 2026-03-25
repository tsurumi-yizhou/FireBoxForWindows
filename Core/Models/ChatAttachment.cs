namespace Core.Models;

public sealed record ChatAttachment(
    MediaFormat MediaFormat,
    string MimeType,
    string? FileName,
    byte[] Data,
    long SizeBytes);
