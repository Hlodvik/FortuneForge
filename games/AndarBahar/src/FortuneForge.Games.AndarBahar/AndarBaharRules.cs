using System.Collections.ObjectModel;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar;

public enum AndarBaharSide { Andar, Bahar }
public enum AndarBaharDisposition { Win, Loss, NotPlaced }

/// <summary>Number is one based and excludes the central Joker.</summary>
public sealed record AndarBaharDeal(int Number, AndarBaharSide Side, PlayingCard Card);

/// <summary>A validated, immutable public deal. It never contains the unrevealed deck.</summary>
public sealed class AndarBaharRound
{
    internal AndarBaharRound(PlayingCard joker, IReadOnlyList<AndarBaharDeal> deals)
    {
        Joker = joker;
        Deals = new ReadOnlyCollection<AndarBaharDeal>(deals.ToArray());
    }

    public PlayingCard Joker { get; }
    public IReadOnlyList<AndarBaharDeal> Deals { get; }
    public AndarBaharSide Winner => Deals[^1].Side;
    public PlayingCard WinningCard => Deals[^1].Card;
    public int CardsDealt => Deals.Count;
}

public readonly record struct AndarBaharWagers(long AndarCents, long BaharCents)
{
    public long TotalCents => checked(AndarCents + BaharCents);

    public long For(AndarBaharSide side) => side switch
    {
        AndarBaharSide.Andar => AndarCents,
        AndarBaharSide.Bahar => BaharCents,
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };
}

public sealed record AndarBaharSettlement(
    AndarBaharSide Side, long StakeCents, AndarBaharDisposition Disposition, long TotalReturnCents)
{
    /// <summary>Net change after subtracting the stake; losses are negative.</summary>
    public long ProfitCents => checked(TotalReturnCents - StakeCents);
}

/// <summary>
/// Single 52-card deck; the first card is the central "Joker" (a standard card).
/// Remaining cards are dealt Andar, Bahar, Andar, Bahar until the first rank match.
/// A winning Andar bet returns its stake plus 9/10 profit; Bahar returns stake plus equal profit.
/// </summary>
public static class AndarBaharRules
{
    public const int DeckSize = 52;
    public const int MaximumCardsDealt = 49;
    public const long StakeIncrementCents = 10;
    public const int AndarProbabilityNumerator = 429;
    public const int BaharProbabilityNumerator = 404;
    public const int ProbabilityDenominator = 833;

    /// <summary>
    /// Evaluates a full deck supplied in top-card-first order. Randomness belongs to the server.
    /// Every card, including the hidden tail, is validated before any outcome is returned.
    /// </summary>
    public static AndarBaharRound Deal(IReadOnlyList<PlayingCard> fullDeck)
    {
        ArgumentNullException.ThrowIfNull(fullDeck);
        if (fullDeck.Count != DeckSize)
            throw new ArgumentException("Andar Bahar requires one complete 52-card deck.", nameof(fullDeck));

        // Snapshot before validation so caller mutations cannot alter the evaluated deck/result.
        var cards = fullDeck.ToArray();
        var seen = new HashSet<PlayingCard>();
        foreach (var card in cards)
        {
            if (!Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit))
                throw new ArgumentException("The deck contains a non-standard playing card.", nameof(fullDeck));
            if (!seen.Add(card))
                throw new ArgumentException("The deck must contain each standard playing card exactly once.", nameof(fullDeck));
        }

        var joker = cards[0];
        var deals = new List<AndarBaharDeal>(MaximumCardsDealt);
        for (var number = 1; number <= MaximumCardsDealt; number++)
        {
            var side = SideForDeal(number);
            var card = cards[number];
            deals.Add(new AndarBaharDeal(number, side, card));
            if (card.Rank == joker.Rank)
                return new AndarBaharRound(joker, deals);
        }

        // A complete valid deck has 48 nonmatching cards and three matching cards.
        throw new InvalidOperationException("A complete deck must match the Joker by deal 49.");
    }

    public static AndarBaharSide SideForDeal(int number)
    {
        if (number is < 1 or > MaximumCardsDealt)
            throw new ArgumentOutOfRangeException(nameof(number), "Deal number must be from 1 through 49.");
        return number % 2 == 1 ? AndarBaharSide.Andar : AndarBaharSide.Bahar;
    }

    public static void ValidateWagers(AndarBaharWagers wagers)
    {
        ValidateStake(wagers.AndarCents);
        ValidateStake(wagers.BaharCents);
        _ = wagers.TotalCents;
    }

    public static AndarBaharSettlement Settle(AndarBaharSide side, long stakeCents, AndarBaharRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        ValidateStake(stakeCents);
        if (stakeCents == 0) return new(side, 0, AndarBaharDisposition.NotPlaced, 0);
        if (round.Winner != side) return new(side, stakeCents, AndarBaharDisposition.Loss, 0);

        // Divide before multiply so every representable return is accepted without intermediate overflow.
        var profitCents = side == AndarBaharSide.Andar ? checked(stakeCents / 10 * 9) : stakeCents;
        return new(side, stakeCents, AndarBaharDisposition.Win, checked(stakeCents + profitCents));
    }

    public static IReadOnlyList<AndarBaharSettlement> Settle(AndarBaharWagers wagers, AndarBaharRound round)
    {
        ValidateWagers(wagers);
        return Array.AsReadOnly(new[]
        {
            Settle(AndarBaharSide.Andar, wagers.AndarCents, round),
            Settle(AndarBaharSide.Bahar, wagers.BaharCents, round),
        });
    }

    private static void ValidateStake(long stakeCents)
    {
        if (stakeCents < 0 || stakeCents % StakeIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(stakeCents), "Stakes must be non-negative multiples of 10 cents so all payouts are exact.");
    }
}

public static class AndarBaharModule
{
    public static GameDescriptor Descriptor { get; } = new(
        "andar-bahar", "Andar Bahar", GameCategory.Casino, "0.1.0",
        "/games/andar-bahar", "/api/games/andar-bahar",
        GameCapability.Credits | GameCapability.FreePlay | GameCapability.History);
}
