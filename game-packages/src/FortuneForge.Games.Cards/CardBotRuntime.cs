using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Games.Cards;

public static class CardBotSkillLevels
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

public sealed class CardBotGameOptions
{
    public bool Enabled { get; set; }
    public int MaxBotsPerMatch { get; set; } = 5;
    public int HumanWaitGraceMilliseconds { get; set; } = 5_000;
    public int MinimumThinkDelayMilliseconds { get; set; } = 350;
    public int MaximumThinkDelayMilliseconds { get; set; } = 1_200;
    public double ThreeStarErrorRate { get; set; } = 0.12;
    public double FourStarImperfectionRate { get; set; } = 0.03;
}

public sealed class DeterministicBotRandom(ulong seed, string stream)
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

public static class CardBotSeed
{
    public static ulong Create()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }
}

public sealed class BotIdentityFactory
{
    private static readonly string[] Adjectives =
        ["Amber", "Brisk", "Calm", "Clever", "Copper", "Gentle", "Jolly", "Lively", "Mellow", "Silver", "Sunny", "Swift"];
    private static readonly string[] Nouns =
        ["Badger", "Cardinal", "Comet", "Dolphin", "Finch", "Juniper", "Lantern", "Otter", "Panda", "Pebble", "Robin", "Willow"];

    public IReadOnlyList<BotIdentity> Create(ulong seed, int count, int skillLevel)
    {
        CardBotSkillLevels.Validate(skillLevel);
        if (count < 0 || count > 8) throw new ArgumentOutOfRangeException(nameof(count));

        var random = new DeterministicBotRandom(seed, "bot-identities-v1");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BotIdentity>(count);
        for (var index = 0; index < count; index++)
        {
            var baseName = $"{random.Choose(Adjectives)}{random.Choose(Nouns)}";
            var displayName = baseName;
            var suffix = 2;
            while (!used.Add(displayName)) displayName = $"{baseName}{suffix++}";
            result.Add(new BotIdentity($"bot-{index + 1}", displayName, skillLevel));
        }
        return result;
    }
}

public sealed record BotIdentity(string SeatId, string DisplayName, int SkillLevel);
