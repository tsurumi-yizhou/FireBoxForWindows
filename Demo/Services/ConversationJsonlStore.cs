using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Models;
using Demo.Models;

namespace Demo.Services;

public sealed class ConversationJsonlStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _directoryPath;
    private readonly string _legacyDirectoryPath;

    public ConversationJsonlStore(string? directoryPath = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agents",
            "sessions");
        _legacyDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireBox",
            "Demo",
            "Conversations");

        Directory.CreateDirectory(_directoryPath);
    }

    public IReadOnlyList<Conversation> LoadAll()
    {
        Directory.CreateDirectory(_directoryPath);

        var conversations = new List<Conversation>();
        var loadedConversationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadDirectory(_directoryPath, conversations, loadedConversationIds);
        if (Directory.Exists(_legacyDirectoryPath))
            LoadDirectory(_legacyDirectoryPath, conversations, loadedConversationIds);

        return conversations
            .OrderByDescending(static conversation => conversation.UpdatedAt)
            .ThenByDescending(static conversation => conversation.CreatedAt)
            .ToList();
    }

    public void SaveConversation(Conversation conversation)
    {
        Directory.CreateDirectory(_directoryPath);

        var path = GetConversationPath(conversation);
        var tempPath = $"{path}.tmp";

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            WriteEvent(
                writer,
                conversation.CreatedAt,
                "conversation",
                new ConversationPayload(
                    Id: conversation.Id,
                    SessionFileStem: conversation.SessionFileStem,
                    Title: string.IsNullOrWhiteSpace(conversation.Title) ? "New Chat" : conversation.Title,
                    UpdatedAt: conversation.UpdatedAt));

            foreach (var message in conversation.Messages)
            {
                if (ShouldSkipMessage(message))
                    continue;

                WriteEvent(
                    writer,
                    message.CreatedAt,
                    "message",
                    new MessagePayload(
                        Id: message.Id,
                        Role: message.Role,
                        Content: message.Content,
                        Attachments: message.Attachments?.Select(static attachment => new AttachmentPayload(
                            attachment.MediaFormat,
                            attachment.MimeType,
                            attachment.FileName,
                            attachment.Data,
                            attachment.SizeBytes)).ToList(),
                        ReasoningContent: message.ReasoningContent,
                        ErrorMessage: message.ErrorMessage,
                        IsStreaming: message.IsStreaming));
            }
        }

        File.Move(tempPath, path, true);
        DeleteLegacyConversationFile(conversation.Id);
    }

    public void DeleteConversation(Conversation conversation)
    {
        var path = GetConversationPath(conversation);
        if (File.Exists(path))
            File.Delete(path);

        DeleteLegacyConversationFile(conversation.Id);
    }

    private void LoadDirectory(
        string directoryPath,
        List<Conversation> conversations,
        HashSet<string> loadedConversationIds)
    {
        foreach (var path in Directory.EnumerateFiles(directoryPath, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var conversation = LoadConversation(path);
                if (conversation is null || !loadedConversationIds.Add(conversation.Id))
                    continue;

                conversations.Add(conversation);
            }
            catch
            {
                // Ignore a bad transcript so one invalid file does not block the rest.
            }
        }
    }

    private Conversation? LoadConversation(string path)
    {
        Conversation? conversation = null;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var eventEnvelope = JsonSerializer.Deserialize<JsonlEventEnvelope>(line, s_jsonOptions);
            if (!string.IsNullOrWhiteSpace(eventEnvelope?.Type))
            {
                if (TryReadConversationFromEvent(path, eventEnvelope, ref conversation))
                    continue;

                if (TryReadMessageFromEvent(eventEnvelope, conversation))
                    continue;
            }

            var legacyEnvelope = JsonSerializer.Deserialize<LegacyEnvelope>(line, s_jsonOptions);
            if (legacyEnvelope?.Kind is null)
                continue;

            if (string.Equals(legacyEnvelope.Kind, "conversation", StringComparison.OrdinalIgnoreCase))
            {
                var legacyMeta = JsonSerializer.Deserialize<LegacyConversationRecord>(line, s_jsonOptions);
                if (legacyMeta is null || string.IsNullOrWhiteSpace(legacyMeta.Id))
                    return null;

                var createdAt = legacyMeta.CreatedAt == default ? DateTimeOffset.Now : legacyMeta.CreatedAt;
                conversation = new Conversation
                {
                    Id = legacyMeta.Id,
                    SessionFileStem = ResolveLegacySessionFileStem(path, legacyMeta, createdAt),
                    Title = string.IsNullOrWhiteSpace(legacyMeta.Title) ? "New Chat" : legacyMeta.Title,
                    CreatedAt = createdAt,
                    UpdatedAt = legacyMeta.UpdatedAt ?? createdAt,
                };
                continue;
            }

            if (!string.Equals(legacyEnvelope.Kind, "message", StringComparison.OrdinalIgnoreCase) || conversation is null)
                continue;

            var legacyMessage = JsonSerializer.Deserialize<LegacyMessageRecord>(line, s_jsonOptions);
            if (legacyMessage is null)
                continue;

            conversation.Messages.Add(ToChatUiMessage(legacyMessage, conversation.CreatedAt));
        }

        return conversation;
    }

    private static void WriteEvent<TPayload>(
        StreamWriter writer,
        DateTimeOffset timestamp,
        string type,
        TPayload payload)
    {
        var record = new JsonlEventRecord<TPayload>(timestamp, type, payload);
        writer.WriteLine(JsonSerializer.Serialize(record, s_jsonOptions));
    }

    private bool TryReadConversationFromEvent(
        string path,
        JsonlEventEnvelope envelope,
        ref Conversation? conversation)
    {
        if (!string.Equals(envelope.Type, "conversation", StringComparison.OrdinalIgnoreCase))
            return false;

        var payload = envelope.Payload.Deserialize<ConversationPayload>(s_jsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
        {
            conversation = null;
            return true;
        }

        var createdAt = envelope.Timestamp == default ? DateTimeOffset.Now : envelope.Timestamp;
        conversation = new Conversation
        {
            Id = payload.Id,
            SessionFileStem = ResolveSessionFileStem(path, payload, createdAt),
            Title = string.IsNullOrWhiteSpace(payload.Title) ? "New Chat" : payload.Title,
            CreatedAt = createdAt,
            UpdatedAt = payload.UpdatedAt ?? createdAt,
        };
        return true;
    }

    private static bool TryReadMessageFromEvent(JsonlEventEnvelope envelope, Conversation? conversation)
    {
        if (!string.Equals(envelope.Type, "message", StringComparison.OrdinalIgnoreCase) || conversation is null)
            return false;

        var payload = envelope.Payload.Deserialize<MessagePayload>(s_jsonOptions);
        if (payload is null)
            return true;

        var createdAt = envelope.Timestamp == default ? conversation.CreatedAt : envelope.Timestamp;
        conversation.Messages.Add(ToChatUiMessage(payload, createdAt));
        return true;
    }

    private string ResolveSessionFileStem(string path, ConversationPayload payload, DateTimeOffset createdAt)
    {
        if (!string.IsNullOrWhiteSpace(payload.SessionFileStem))
            return payload.SessionFileStem;

        if (string.Equals(Path.GetDirectoryName(path), _directoryPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(path);

        return Conversation.CreateSessionFileStem(createdAt);
    }

    private string ResolveLegacySessionFileStem(string path, LegacyConversationRecord payload, DateTimeOffset createdAt)
    {
        if (!string.IsNullOrWhiteSpace(payload.SessionFileStem))
            return payload.SessionFileStem;

        if (string.Equals(Path.GetDirectoryName(path), _directoryPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(path);

        return Conversation.CreateSessionFileStem(createdAt);
    }

    private string GetConversationPath(Conversation conversation) =>
        Path.Combine(_directoryPath, $"{conversation.SessionFileStem}.jsonl");

    private void DeleteLegacyConversationFile(string conversationId)
    {
        var legacyPath = Path.Combine(_legacyDirectoryPath, $"{conversationId}.jsonl");
        if (File.Exists(legacyPath))
            File.Delete(legacyPath);
    }

    private static ChatUiMessage ToChatUiMessage(MessagePayload payload, DateTimeOffset createdAt) =>
        new()
        {
            Id = payload.Id,
            CreatedAt = createdAt,
            Role = payload.Role ?? string.Empty,
            Content = payload.Content ?? string.Empty,
            Attachments = payload.Attachments?.Select(static attachment => new ChatAttachment(
                attachment.MediaFormat,
                attachment.MimeType ?? "application/octet-stream",
                attachment.FileName,
                attachment.Data ?? [],
                attachment.SizeBytes)).ToList(),
            ReasoningContent = payload.ReasoningContent,
            ErrorMessage = payload.ErrorMessage,
            IsStreaming = false,
        };

    private static ChatUiMessage ToChatUiMessage(LegacyMessageRecord payload, DateTimeOffset createdAt) =>
        new()
        {
            Id = payload.Id,
            CreatedAt = createdAt,
            Role = payload.Role ?? string.Empty,
            Content = payload.Content ?? string.Empty,
            Attachments = payload.Attachments?.Select(static attachment => new ChatAttachment(
                attachment.MediaFormat,
                attachment.MimeType ?? "application/octet-stream",
                attachment.FileName,
                attachment.Data ?? [],
                attachment.SizeBytes)).ToList(),
            ReasoningContent = payload.ReasoningContent,
            ErrorMessage = payload.ErrorMessage,
            IsStreaming = false,
        };

    private static bool ShouldSkipMessage(ChatUiMessage message) =>
        message.IsStreaming &&
        string.IsNullOrWhiteSpace(message.Content) &&
        string.IsNullOrWhiteSpace(message.ErrorMessage) &&
        string.IsNullOrWhiteSpace(message.ReasoningContent);

    private sealed record JsonlEventRecord<TPayload>(
        DateTimeOffset Timestamp,
        string Type,
        TPayload Payload);

    private sealed record JsonlEventEnvelope(
        DateTimeOffset Timestamp,
        string? Type,
        JsonElement Payload);

    private sealed record ConversationPayload(
        string Id,
        string? SessionFileStem,
        string Title,
        DateTimeOffset? UpdatedAt);

    private sealed record MessagePayload(
        long Id,
        string? Role,
        string? Content,
        List<AttachmentPayload>? Attachments,
        string? ReasoningContent,
        string? ErrorMessage,
        bool IsStreaming);

    private sealed record AttachmentPayload(
        ModelMediaFormat MediaFormat,
        string? MimeType,
        string? FileName,
        byte[]? Data,
        long SizeBytes);

    private sealed record LegacyEnvelope(string? Kind);

    private sealed record LegacyConversationRecord(
        string Kind,
        string Id,
        string? SessionFileStem,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record LegacyMessageRecord(
        string Kind,
        long Id,
        string? Role,
        string? Content,
        List<AttachmentPayload>? Attachments,
        string? ReasoningContent,
        string? ErrorMessage,
        bool IsStreaming);
}
