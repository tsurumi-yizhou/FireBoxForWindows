using System;

namespace Demo.Search;

public sealed record ConversationSearchResult(
    string ConversationId,
    string Title,
    string Subtitle,
    bool TitleMatches,
    DateTimeOffset UpdatedAt);
