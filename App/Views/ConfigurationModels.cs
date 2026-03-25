using System.Collections.Generic;
using Core.Models;

namespace App.Views;

internal sealed class ProviderDto
{
    public int Id { get; init; }
    public string ProviderType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public List<string> EnabledModelIds { get; init; } = [];

    public string DisplayName => $"{Name} ({ProviderType})";
    public string BaseUrlLabel => string.IsNullOrWhiteSpace(BaseUrl) ? "Missing base URL" : BaseUrl;

    public override string ToString() => DisplayName;
}

internal sealed class RouteCandidateDto
{
    public int ProviderId { get; init; }
    public string ModelId { get; init; } = string.Empty;
}

internal sealed class RouteDto
{
    public int Id { get; init; }
    public string VirtualModelId { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
    public List<RouteCandidateDto> Candidates { get; init; } = [];
    public bool Reasoning { get; init; }
    public bool ToolCalling { get; init; }
    public int InputFormatsMask { get; init; }
    public int OutputFormatsMask { get; init; }
}
