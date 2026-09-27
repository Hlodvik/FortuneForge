using System.Collections.Immutable;

namespace FortuneForge.Games.Keno;

/// <summary>
/// The Fortune Forge Keno prize schedule. Multipliers are total returns for a
/// one-unit wager and follow the familiar 1-to-10-spot lottery Keno structure.
/// </summary>
public sealed class StandardKenoPaytable : IKenoPaytable
{
    public const string Id = "fortuneforge-keno-v1";

    public static StandardKenoPaytable Instance { get; } = new();

    public static ImmutableArray<KenoPrizeTier> Tiers { get; } =
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

        var multiplier = Tiers
            .Where(tier => tier.Spots == ticket.Numbers.Length && tier.Hits == hitCount)
            .Select(tier => tier.Multiplier)
            .SingleOrDefault();
        return new KenoPaytableOutcome(Id, multiplier);
    }
}
