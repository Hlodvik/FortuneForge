using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Games.Abstractions.ManagedPlayers;

public sealed class DeterministicManagedPlayerRandom(ulong seed, string stream)
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
