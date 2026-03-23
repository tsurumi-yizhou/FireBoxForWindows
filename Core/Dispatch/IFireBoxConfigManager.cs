namespace Core.Dispatch;

public interface IFireBoxConfigManager
{
    string ListProviders();
    int AddProvider(string providerType, string name, string baseUrl, string apiKey);
    void UpdateProvider(int id, string name, string baseUrl, string apiKey, string enabledModelIdsJson, bool isEnabled);
    void DeleteProvider(int id);
    string FetchProviderModels(int providerId);

    string ListRoutes();
    int AddRoute(string virtualModelId, string strategy, string candidatesJson, bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask);
    void UpdateRoute(int id, string virtualModelId, string strategy, string candidatesJson, bool reasoning, bool toolCalling, int inputFormatsMask, int outputFormatsMask);
    void DeleteRoute(int id);

    string GetDailyStats(int year, int month, int day);
    string GetMonthlyStats(int year, int month);

    string ListConnections();
    string ListClientAccess();
    void UpdateClientAccessAllowed(int accessId, bool isAllowed);

    long RegisterConnection(int processId, string processName, string executablePath);
    void UnregisterConnection(long connectionId);
    void IncrementRequestCount(long connectionId);

    bool IsClientAllowed(string processName, string executablePath);
    void RecordClientAccess(int processId, string processName, string executablePath);

    void SetConnectionStreamState(long connectionId, bool hasActiveStream);
}
