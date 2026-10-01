using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Bots;

internal static class ManagedPlayerSkillLevels
{
    public const int Poor = 2;
    public const int Average = 3;
    public const int Strong = 4;

    public static void Validate(int value)
    {
        if (value is not Poor and not Average and not Strong)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Difficulty must be exactly 2, 3, or 4.");
        }
    }
}

internal sealed class ManagedPlayerDecisionOptions
{
    public double AverageErrorRate { get; init; } = 0.12;
    public double StrongImperfectionRate { get; init; } = 0.03;
}

internal sealed class DeterministicManagedPlayerRandom(ulong seed, string stream)
{
    private ulong counter;

    public int Next(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public double NextDouble() =>
        (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public T Choose<T>(IReadOnlyList<T> values) =>
        values.Count == 0
            ? throw new ArgumentException("At least one value is required.", nameof(values))
            : values[Next(values.Count)];

    private ulong NextUInt64()
    {
        Span<byte> input = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(input, seed);
        BinaryPrimitives.WriteUInt64LittleEndian(input[8..], counter++);
        var streamBytes = Encoding.UTF8.GetBytes(stream);
        var payload = new byte[input.Length + streamBytes.Length];
        input.CopyTo(payload);
        streamBytes.CopyTo(payload.AsSpan(input.Length));
        return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(payload));
    }
}
