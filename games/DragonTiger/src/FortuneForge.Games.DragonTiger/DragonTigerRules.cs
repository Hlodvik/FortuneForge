using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.DragonTiger;

public enum DragonTigerSide { Dragon, Tiger, Tie }
public enum DragonTigerDisposition { Win, Loss, HalfLoss, NotPlaced }

public sealed record DragonTigerRound(PlayingCard DragonCard, PlayingCard TigerCard)
{
    public int DragonValue => DragonTigerRules.CardValue(DragonCard);
    public int TigerValue => DragonTigerRules.CardValue(TigerCard);
    public DragonTigerSide Outcome => DragonValue > TigerValue ? DragonTigerSide.Dragon
        : DragonValue < TigerValue ? DragonTigerSide.Tiger : DragonTigerSide.Tie;
}

public readonly record struct DragonTigerWagers(long DragonCents, long TigerCents, long TieCents)
{
    public long TotalCents => checked(DragonCents + TigerCents + TieCents);
    public long For(DragonTigerSide side) => side switch
    {
        DragonTigerSide.Dragon => DragonCents, DragonTigerSide.Tiger => TigerCents,
        DragonTigerSide.Tie => TieCents, _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };
}

public sealed record DragonTigerSettlement(
    DragonTigerSide Side, long StakeCents, DragonTigerDisposition Disposition, long TotalReturnCents)
{
    public long ProfitCents => checked(TotalReturnCents - StakeCents);
}

public static class DragonTigerRules
{
    public const int DeckCount = 8;
    public const int ShoeSize = DeckCount * 52;
    public const int ReshuffleAt = 312;

    public static int CardValue(PlayingCard card)
    {
        if (!Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit))
            throw new ArgumentException("Dragon Tiger requires a standard playing card.", nameof(card));
        return (int)card.Rank;
    }

    public static DragonTigerRound Deal(IReadOnlyList<PlayingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count < 2) throw new ArgumentException("Two cards are required for Dragon Tiger.", nameof(cards));
        _ = CardValue(cards[0]);
        _ = CardValue(cards[1]);
        return new(cards[0], cards[1]);
    }

    public static DragonTigerSettlement Settle(DragonTigerSide side, long stakeCents, DragonTigerRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        if (stakeCents < 0 || stakeCents % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(stakeCents), "Stakes must be non-negative even cents so half returns are exact.");
        if (stakeCents == 0) return new(side, 0, DragonTigerDisposition.NotPlaced, 0);
        if (round.Outcome == side)
            return new(side, stakeCents, DragonTigerDisposition.Win, checked(stakeCents * (side == DragonTigerSide.Tie ? 9 : 2)));
        if (round.Outcome == DragonTigerSide.Tie && side != DragonTigerSide.Tie)
            return new(side, stakeCents, DragonTigerDisposition.HalfLoss, stakeCents / 2);
        return new(side, stakeCents, DragonTigerDisposition.Loss, 0);
    }

    public static IReadOnlyList<DragonTigerSettlement> Settle(DragonTigerWagers wagers, DragonTigerRound round) =>
        Enum.GetValues<DragonTigerSide>().Select(side => Settle(side, wagers.For(side), round)).ToArray();
}

public static class DragonTigerModule
{
    public static GameDescriptor Descriptor { get; } = new(
        "dragon-tiger", "Dragon Tiger", GameCategory.Casino, "0.1.0",
        "/games/dragon-tiger", "/api/games/dragon-tiger",
        GameCapability.Credits | GameCapability.FreePlay | GameCapability.History);
}
