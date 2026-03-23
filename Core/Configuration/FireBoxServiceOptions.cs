namespace Core.Configuration;

public sealed class FireBoxServiceOptions
{
    public const string SectionName = "FireBox";

    public string StorageRootPath { get; set; } = string.Empty;
    public string DatabaseFileName { get; set; } = string.Empty;
    public string ComErrorLogFileName { get; set; } = string.Empty;
    public string SingleInstanceMutexName { get; set; } = string.Empty;
    public double AccessDenyCooldownHours { get; set; }
    public Dictionary<string, string> DefaultProviderBaseUrls { get; set; } = [];
    public List<string> AnthropicKnownModels { get; set; } = [];
    public List<string> TrustedClientProcessNames { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StorageRootPath))
            throw new InvalidOperationException($"Missing configuration value: {SectionName}:StorageRootPath");

        if (string.IsNullOrWhiteSpace(DatabaseFileName))
            throw new InvalidOperationException($"Missing configuration value: {SectionName}:DatabaseFileName");

        if (string.IsNullOrWhiteSpace(ComErrorLogFileName))
            throw new InvalidOperationException($"Missing configuration value: {SectionName}:ComErrorLogFileName");

        if (string.IsNullOrWhiteSpace(SingleInstanceMutexName))
            throw new InvalidOperationException($"Missing configuration value: {SectionName}:SingleInstanceMutexName");

        if (AccessDenyCooldownHours <= 0)
            throw new InvalidOperationException($"Configuration value {SectionName}:AccessDenyCooldownHours must be greater than zero.");

        if (DefaultProviderBaseUrls.Count == 0)
            throw new InvalidOperationException($"Missing configuration section: {SectionName}:DefaultProviderBaseUrls");

        if (AnthropicKnownModels.Count == 0)
            throw new InvalidOperationException($"Missing configuration section: {SectionName}:AnthropicKnownModels");
    }

    public string ResolveStorageRootPath()
    {
        var expanded = Environment.ExpandEnvironmentVariables(StorageRootPath.Trim());
        return Path.GetFullPath(expanded);
    }

    public string ResolveDatabasePath() =>
        Path.Combine(ResolveStorageRootPath(), DatabaseFileName);

    public string ResolveComErrorLogPath() =>
        Path.Combine(ResolveStorageRootPath(), ComErrorLogFileName);

    public TimeSpan ResolveAccessDenyCooldown() =>
        TimeSpan.FromHours(AccessDenyCooldownHours);

    public string GetDefaultProviderBaseUrl(string providerType)
    {
        foreach (var pair in DefaultProviderBaseUrls)
        {
            if (string.Equals(pair.Key, providerType, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return string.Empty;
    }

    public bool IsTrustedClientProcessName(string processName) =>
        TrustedClientProcessNames.Any(candidate =>
            string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase));
}