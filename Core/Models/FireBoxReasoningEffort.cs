namespace Core.Models;

public enum FireBoxReasoningEffort
{
    Default = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public static class FireBoxReasoningEfforts
{
    public static IReadOnlyList<FireBoxReasoningEffort> SupportedValues { get; } =
    [
        FireBoxReasoningEffort.Default,
        FireBoxReasoningEffort.Low,
        FireBoxReasoningEffort.Medium,
        FireBoxReasoningEffort.High,
    ];

    public static FireBoxReasoningEffort Normalize(int value)
    {
        if (Enum.IsDefined(typeof(FireBoxReasoningEffort), value))
            return (FireBoxReasoningEffort)value;

        throw new InvalidOperationException("Reasoning effort must be explicitly set to Default, Low, Medium, or High.");
    }

    public static string ToDisplayName(FireBoxReasoningEffort effort) => effort switch
    {
        FireBoxReasoningEffort.Default => "Provider default",
        FireBoxReasoningEffort.Low => "Low",
        FireBoxReasoningEffort.Medium => "Medium",
        FireBoxReasoningEffort.High => "High",
        _ => throw new InvalidOperationException($"Unsupported reasoning effort '{effort}'."),
    };
}
