using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Core.Com;

/// <summary>
/// Control interface — used by App to manage the service lifecycle, configuration, and stats.
/// All JSON-based methods use BSTR for serialized data to avoid SAFEARRAY(UDT) complexity
/// on the management interface (which is less performance-critical than the capability interface).
/// </summary>
[GeneratedComInterface]
[Guid(FireBoxGuids.ControlInterface)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IFireBoxControl
{
    [return: MarshalAs(UnmanagedType.BStr)]
    string Ping([MarshalAs(UnmanagedType.BStr)] string message);

    void Shutdown();

    // --- Stats ---

    /// <summary>Returns JSON: { requestCount, promptTokens, completionTokens, totalTokens, estimatedCostUsd }</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDailyStats(int year, int month, int day);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetMonthlyStats(int year, int month);

    // --- Provider CRUD ---

    /// <summary>Returns JSON array of provider configs.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    string ListProviders();

    /// <summary>Returns new provider ID.</summary>
    int AddProvider(
        [MarshalAs(UnmanagedType.BStr)] string providerType,
        [MarshalAs(UnmanagedType.BStr)] string name,
        [MarshalAs(UnmanagedType.BStr)] string baseUrl,
        [MarshalAs(UnmanagedType.BStr)] string apiKey);

    void UpdateProvider(
        int providerId,
        [MarshalAs(UnmanagedType.BStr)] string name,
        [MarshalAs(UnmanagedType.BStr)] string baseUrl,
        [MarshalAs(UnmanagedType.BStr)] string apiKey,
        [MarshalAs(UnmanagedType.BStr)] string enabledModelIdsJson);

    void DeleteProvider(int providerId);

    /// <summary>Returns JSON array of model ID strings.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    string FetchProviderModels(int providerId);

    // --- Route CRUD ---

    [return: MarshalAs(UnmanagedType.BStr)]
    string ListRoutes();

    int AddRoute(
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string strategy,
        [MarshalAs(UnmanagedType.BStr)] string candidatesJson,
        int reasoning, int toolCalling,
        int inputFormatsMask, int outputFormatsMask);

    void UpdateRoute(
        int routeId,
        [MarshalAs(UnmanagedType.BStr)] string virtualModelId,
        [MarshalAs(UnmanagedType.BStr)] string strategy,
        [MarshalAs(UnmanagedType.BStr)] string candidatesJson,
        int reasoning, int toolCalling,
        int inputFormatsMask, int outputFormatsMask);

    void DeleteRoute(int routeId);

    // --- Connections ---

    /// <summary>Returns JSON array of active connections.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    string ListConnections();

    // --- Client Access ---

    [return: MarshalAs(UnmanagedType.BStr)]
    string ListClientAccess();

    void UpdateClientAccessAllowed(int accessId, int isAllowed);
}
