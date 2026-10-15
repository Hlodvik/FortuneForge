namespace FortuneForge.Games.HouseHoldem;

public static class HouseHoldemPhases
{
    public const string Preflop = "preflop";
    public const string Flop = "flop";
    public const string River = "river";
    public const string Settled = "settled";
}

public static class HouseHoldemActions
{
    public const string Check = "check";
    public const string Play3X = "play-3x";
    public const string Play4X = "play-4x";
    public const string Play2X = "play-2x";
    public const string Play1X = "play-1x";
    public const string Fold = "fold";
}

/// <summary>The full, private round state. Never send this object to a client; use ToPlayerView.</summary>
public sealed class HouseHoldemRound
{
    public required IReadOnlyList<string> PlayerCards { get; init; }
    public required IReadOnlyList<string> DealerCards { get; init; }
    public required IReadOnlyList<string> CommunityCards { get; init; }
    public required long AnteCents { get; init; }
    public required long BlindCents { get; init; }
    public required long TripsCents { get; init; }
    public long PlayWagerCents { get; set; }
    public string Phase { get; set; } = HouseHoldemPhases.Preflop;
    public bool Folded { get; set; }
    public HouseHoldemSettlement? Settlement { get; set; }
    public long TotalWagerCents => checked(AnteCents + BlindCents + TripsCents + PlayWagerCents);
    public long PayoutCents => Settlement?.PayoutCents ?? 0;
}

/// <summary>Amounts include returned stake on wins and pushes; net = payout - total wager.</summary>
public sealed record HouseHoldemSettlement(
    string Outcome,
    string PlayerHandRank,
    string DealerHandRank,
    bool DealerQualifies,
    long AntePayoutCents,
    long BlindPayoutCents,
    long PlayPayoutCents,
    long TripsPayoutCents)
{
    public long PayoutCents => checked(AntePayoutCents + BlindPayoutCents + PlayPayoutCents + TripsPayoutCents);
}

public sealed record HouseHoldemVisibleCard(string? Code, bool Hidden);

public sealed record HouseHoldemPlayerView(
    string Phase,
    IReadOnlyList<HouseHoldemVisibleCard> PlayerCards,
    IReadOnlyList<HouseHoldemVisibleCard> DealerCards,
    IReadOnlyList<HouseHoldemVisibleCard> CommunityCards,
    IReadOnlyList<string> LegalActions,
    bool CanAdvance,
    long AnteCents,
    long BlindCents,
    long TripsCents,
    long PlayWagerCents,
    long TotalWagerCents,
    HouseHoldemSettlement? Settlement);
