using System;
using System.Collections.Generic;
using System.Linq;
using Demo.Models;

namespace Demo.Search;

public sealed class ConversationSearchService
{
    public ConversationSearchService()
    {
    }

    public IReadOnlyList<ConversationSearchResult> Search(
        IEnumerable<Conversation> conversations,
        string? queryText,
        int maxSuggestions,
        int snippetLength)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        if (maxSuggestions <= 0)
            throw new InvalidOperationException("Max suggestions must be greater than zero.");
        if (snippetLength <= 0)
            throw new InvalidOperationException("Snippet length must be greater than zero.");

        var query = queryText?.Trim() ?? string.Empty;
        var results = conversations
            .Select(conversation => EvaluateConversation(conversation, query, snippetLength))
            .Where(item => string.IsNullOrWhiteSpace(query) || item.TitleMatches || item.ContentMatches)
            .OrderByDescending(item => item.TitleMatches)
            .ThenByDescending(item => item.Result.UpdatedAt)
            .Take(maxSuggestions)
            .Select(item => item.Result)
            .ToList();

        return results;
    }

    private EvaluationResult EvaluateConversation(Conversation conversation, string query, int snippetLength)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var titleMatches = Contains(conversation.Title, query);
        var contentMatches = conversation.Messages.Any(message => Contains(message.Content, query));
        var subtitle = BuildSubtitle(conversation, query, snippetLength);

        return new EvaluationResult(
            new ConversationSearchResult(
                conversation.Id,
                conversation.Title,
                subtitle,
                titleMatches,
                conversation.UpdatedAt),
            titleMatches,
            contentMatches);
    }

    private static string BuildSubtitle(
        Conversation conversation,
        string query,
        int snippetLength)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            var matchedMessage = conversation.Messages
                .FirstOrDefault(message => Contains(message.Content, query));
            if (matchedMessage is not null)
                return BuildSnippet(matchedMessage.Content, query, snippetLength);

            if (Contains(conversation.Title, query))
                return BuildSnippet(conversation.Title, query, snippetLength);
        }

        for (var index = conversation.Messages.Count - 1; index >= 0; index--)
        {
            var content = conversation.Messages[index].Content;
            if (!string.IsNullOrWhiteSpace(content))
                return BuildSnippet(content, query, snippetLength);
        }

        return conversation.UpdatedAt.LocalDateTime.ToString("g");
    }

    private static bool Contains(string? source, string query)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
            return false;

        return source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSnippet(string content, string query, int snippetLength)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = content.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length == 0)
            return normalized;

        if (normalized.Length <= snippetLength)
            return normalized;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var index = normalized.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var halfWindow = snippetLength / 2;
                var start = Math.Max(0, index - halfWindow);
                var length = Math.Min(snippetLength, normalized.Length - start);
                var segment = normalized.Substring(start, length);
                return start > 0 ? $"...{segment}" : segment;
            }
        }

        return normalized[..snippetLength];
    }

    private sealed record EvaluationResult(
        ConversationSearchResult Result,
        bool TitleMatches,
        bool ContentMatches);
}
