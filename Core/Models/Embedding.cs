namespace Core.Models;

public sealed record Embedding(int Index, float[] Vector)
{
    public bool Equals(Embedding? other) =>
        other is not null && Index == other.Index && Vector.AsSpan().SequenceEqual(other.Vector);

    public override int GetHashCode() =>
        HashCode.Combine(Index, Vector.Length);
}
