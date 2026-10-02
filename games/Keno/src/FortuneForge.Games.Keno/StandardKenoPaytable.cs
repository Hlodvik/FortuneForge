using System.Collections.Immutable;

namespace FortuneForge.Games.Keno;

/// <summary>
/// The Fortune Forge Keno prize schedule. Multipliers are total returns for a
/// one-unit wager and follow the published 20-of-80 lottery Keno structure.
/// </summary>
public sealed class StandardKenoPaytable : IKenoPaytable
{
    public const string Id = "fortuneforge-keno-v3";
    public const string PriorId = "fortuneforge-keno-v2";
    public const string LegacyId = "fortuneforge-keno-v1";

    public static StandardKenoPaytable Instance { get; } = new();

    public static ImmutableArray<KenoPrizeTier> Tiers { get; } =
    [
        new(1, 1, 2.5m),
        new(2, 2, 11),
        new(3, 2, 2), new(3, 3, 27),
        new(4, 2, 1), new(4, 3, 5), new(4, 4, 72),
        new(5, 3, 2), new(5, 4, 15), new(5, 5, 465),
        new(6, 3, 1), new(6, 4, 5), new(6, 5, 55), new(6, 6, 1_600),
        new(7, 3, 1), new(7, 4, 2), new(7, 5, 15), new(7, 6, 150), new(7, 7, 5_500),
        new(8, 4, 2), new(8, 5, 10), new(8, 6, 60), new(8, 7, 600), new(8, 8, 15_000),
        new(9, 4, 1), new(9, 5, 4), new(9, 6, 25), new(9, 7, 215), new(9, 8, 3_000), new(9, 9, 50_000),
        new(10, 0, 5), new(10, 5, 2), new(10, 6, 10), new(10, 7, 55), new(10, 8, 500), new(10, 9, 4_500), new(10, 10, 200_000),
    ];

    private static ImmutableArray<KenoPrizeTier> PriorTiers { get; } =
    [
        new(1, 1, 4),
        new(2, 1, 1), new(2, 2, 9),
        new(3, 1, 1), new(3, 2, 2), new(3, 3, 15),
        new(4, 2, 2), new(4, 3, 8), new(4, 4, 50),
        new(5, 2, 2), new(5, 3, 3), new(5, 4, 8), new(5, 5, 100),
        new(6, 2, 1), new(6, 3, 2), new(6, 4, 10), new(6, 5, 20), new(6, 6, 100),
        new(7, 2, 1), new(7, 3, 2), new(7, 4, 3), new(7, 5, 8), new(7, 6, 20), new(7, 7, 1_000),
        new(8, 3, 2), new(8, 4, 3), new(8, 5, 10), new(8, 6, 20), new(8, 7, 50), new(8, 8, 5_000),
        new(9, 3, 2), new(9, 4, 2), new(9, 5, 4), new(9, 6, 8), new(9, 7, 20), new(9, 8, 100), new(9, 9, 25_000),
        new(10, 3, 1), new(10, 4, 2), new(10, 5, 4), new(10, 6, 8), new(10, 7, 20), new(10, 8, 100), new(10, 9, 500), new(10, 10, 100_000),
    ];

    private static ImmutableArray<KenoPrizeTier> LegacyTiers { get; } =
    [
        new(1, 1, 2),
        new(2, 2, 11),
        new(3, 2, 2), new(3, 3, 27),
        new(4, 2, 1), new(4, 3, 5), new(4, 4, 72),
        new(5, 3, 2), new(5, 4, 18), new(5, 5, 410),
        new(6, 3, 1), new(6, 4, 7), new(6, 5, 57), new(6, 6, 1_100),
        new(7, 3, 1), new(7, 4, 5), new(7, 5, 11), new(7, 6, 100), new(7, 7, 2_000),
        new(8, 4, 2), new(8, 5, 15), new(8, 6, 50), new(8, 7, 300), new(8, 8, 10_000),
        new(9, 4, 2), new(9, 5, 5), new(9, 6, 20), new(9, 7, 100), new(9, 8, 2_000), new(9, 9, 25_000),
        new(10, 0, 5), new(10, 5, 2), new(10, 6, 10), new(10, 7, 50), new(10, 8, 500), new(10, 9, 5_000), new(10, 10, 100_000),
    ];

    private StandardKenoPaytable() { }

    public KenoPaytableOutcome Evaluate(KenoTicket ticket, int hitCount)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (hitCount is < 0 || hitCount > ticket.Numbers.Length)
            throw new ArgumentOutOfRangeException(nameof(hitCount));

        var multiplier = MultiplierFor(Id, ticket.Numbers.Length, hitCount);
        return new KenoPaytableOutcome(Id, multiplier);
    }

    public static bool Supports(string paytableId) => paytableId is Id or PriorId or LegacyId;

    public static decimal MultiplierFor(string paytableId, int spots, int hits)
    {
        var tiers = paytableId switch
        {
            Id => Tiers,
            PriorId => PriorTiers,
            LegacyId => LegacyTiers,
            _ => throw new ArgumentOutOfRangeException(nameof(paytableId), "The Keno paytable version is not supported."),
        };

        return tiers
            .Where(tier => tier.Spots == spots && tier.Hits == hits)
            .Select(tier => tier.Multiplier)
            .SingleOrDefault();
    }
}
