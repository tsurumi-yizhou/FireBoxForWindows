namespace Core.Models;

public static class FireBoxRouteStrategies
{
    public const string Ordered = "Ordered";
    public const string Random = "Random";

    public static string Normalize(string? strategy)
    {
        if (string.Equals(strategy, Ordered, StringComparison.OrdinalIgnoreCase))
            return Ordered;

        if (string.Equals(strategy, Random, StringComparison.OrdinalIgnoreCase))
            return Random;

        throw new InvalidOperationException("Route strategy must be explicitly set to Ordered or Random.");
    }
}
