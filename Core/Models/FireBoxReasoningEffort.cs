namespace Core.Models;

public enum ReasoningEffort
{
    Default = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Max = 4,
}

public static class ReasoningEfforts
{
    public static IReadOnlyList<ReasoningEffort> SupportedValues { get; } =
    [
        ReasoningEffort.Default,
        ReasoningEffort.Low,
        ReasoningEffort.Medium,
        ReasoningEffort.High,
        ReasoningEffort.Max,
    ];

    public static ReasoningEffort Normalize(int value)
    {
        if (Enum.IsDefined(typeof(ReasoningEffort), value))
            return (ReasoningEffort)value;

        throw new InvalidOperationException("Reasoning effort must be explicitly set to Default, Low, Medium, High, or Max.");
    }

    public static string ToDisplayName(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Default => "Provider default",
        ReasoningEffort.Low => "Low",
        ReasoningEffort.Medium => "Medium",
        ReasoningEffort.High => "High",
        ReasoningEffort.Max => "Max",
        _ => throw new InvalidOperationException($"Unsupported reasoning effort '{effort}'."),
    };
}
