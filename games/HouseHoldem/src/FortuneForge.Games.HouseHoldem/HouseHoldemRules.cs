using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Games.HouseHoldem;

/// <summary>Single-player, server-authoritative house Hold'em rules with the UTH-01 Trips schedule.</summary>
public static class HouseHoldemRules
{
    public const long MinimumBetCents = 100;
    public const long MaximumAnteCents = 10_000;
    public const long MaximumTripsCents = 50_000;
    public const long BetIncrementCents = 100;
    private const long PreviousMaximumAnteCents = 50_000;

    public static IReadOnlyList<string> CreateShuffledDeck()
    {
        var deck = StandardDeck.Create().Select(card => card.Code).ToArray();
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var replacement = RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[replacement]) = (deck[replacement], deck[index]);
        }
        return deck;
    }

    public static HouseHoldemRound StartRound(IReadOnlyList<string> shuffledDeck, long anteCents, long tripsCents = 0) =>
        CreateRound(shuffledDeck, anteCents, tripsCents, MaximumAnteCents);

    // Existing hands retain their original stakes when replayed from the ledger.
    public static HouseHoldemRound RestoreRound(IReadOnlyList<string> shuffledDeck, long anteCents, long tripsCents = 0) =>
        CreateRound(shuffledDeck, anteCents, tripsCents, PreviousMaximumAnteCents);

    private static HouseHoldemRound CreateRound(IReadOnlyList<string> shuffledDeck, long anteCents, long tripsCents, long maximumAnteCents)
    {
        ArgumentNullException.ThrowIfNull(shuffledDeck);
        ValidateBet(anteCents, optional: false, maximumAnteCents, nameof(anteCents));
        ValidateBet(tripsCents, optional: true, MaximumTripsCents, nameof(tripsCents));
        if (shuffledDeck.Count != 52 ||
            !shuffledDeck.Select(CardCode.Parse).ToHashSet().SetEquals(StandardDeck.Create()))
            throw new ArgumentException("A round requires one complete, distinct 52-card deck.", nameof(shuffledDeck));

        // Deliver the two hole cards alternately, then the five community cards.
        return new HouseHoldemRound
        {
            PlayerCards = [shuffledDeck[0], shuffledDeck[2]],
            DealerCards = [shuffledDeck[1], shuffledDeck[3]],
            CommunityCards = shuffledDeck.Skip(4).Take(5).ToArray(),
            AnteCents = anteCents,
            BlindCents = anteCents,
            TripsCents = tripsCents,
        };
    }

    public static IReadOnlyList<string> LegalActions(HouseHoldemRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (round.Settlement is not null || round.PlayWagerCents > 0) return [];
        return round.Phase switch
        {
            HouseHoldemPhases.Preflop => [HouseHoldemActions.Check, HouseHoldemActions.Play3X, HouseHoldemActions.Play4X],
            HouseHoldemPhases.Flop => [HouseHoldemActions.Check, HouseHoldemActions.Play2X],
            HouseHoldemPhases.River => [HouseHoldemActions.Play1X, HouseHoldemActions.Fold],
            _ => [],
        };
    }

    public static long RequiredAdditionalWagerCents(HouseHoldemRound round, string action)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (action is null || !LegalActions(round).Contains(action, StringComparer.Ordinal))
            throw new InvalidOperationException("That action is not legal at this stage of the hand.");
        return action switch
        {
            HouseHoldemActions.Play1X => round.AnteCents,
            HouseHoldemActions.Play2X => checked(round.AnteCents * 2),
            HouseHoldemActions.Play3X => checked(round.AnteCents * 3),
            HouseHoldemActions.Play4X => checked(round.AnteCents * 4),
            _ => 0,
        };
    }

    public static void ApplyAction(HouseHoldemRound round, string action)
    {
        var additionalWager = RequiredAdditionalWagerCents(round, action);
        if (action == HouseHoldemActions.Fold)
        {
            round.Folded = true;
            Settle(round);
            return;
        }
        round.PlayWagerCents = additionalWager;
        round.Phase = round.Phase switch
        {
            HouseHoldemPhases.Preflop => HouseHoldemPhases.Flop,
            HouseHoldemPhases.Flop => HouseHoldemPhases.River,
            HouseHoldemPhases.River => HouseHoldemPhases.Settled,
            _ => throw new InvalidOperationException("This hand has already ended."),
        };
        if (round.Phase == HouseHoldemPhases.Settled) Settle(round);
    }

    /// <summary>Reveal the next stage after a Play wager has already been committed.</summary>
    public static void Advance(HouseHoldemRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (!CanAdvance(round)) throw new InvalidOperationException("This hand is waiting for a player decision or is settled.");
        if (round.Phase == HouseHoldemPhases.Flop) round.Phase = HouseHoldemPhases.River;
        else
        {
            round.Phase = HouseHoldemPhases.Settled;
            Settle(round);
        }
    }

    public static bool CanAdvance(HouseHoldemRound round) =>
        round.Settlement is null && round.PlayWagerCents > 0 &&
        round.Phase is HouseHoldemPhases.Flop or HouseHoldemPhases.River;

    public static HouseHoldemPlayerView ToPlayerView(HouseHoldemRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        var dealerVisible = round.Settlement is not null;
        var communityVisible = round.Phase switch
        {
            HouseHoldemPhases.Flop => 3,
            HouseHoldemPhases.River or HouseHoldemPhases.Settled => 5,
            _ => 0,
        };
        return new HouseHoldemPlayerView(
            round.Phase,
            round.PlayerCards.Select(code => new HouseHoldemVisibleCard(code, false)).ToArray(),
            round.DealerCards.Select(code => new HouseHoldemVisibleCard(dealerVisible ? code : null, !dealerVisible)).ToArray(),
            round.CommunityCards.Select((code, index) => new HouseHoldemVisibleCard(index < communityVisible ? code : null, index >= communityVisible)).ToArray(),
            LegalActions(round), CanAdvance(round), round.AnteCents, round.BlindCents, round.TripsCents,
            round.PlayWagerCents, round.TotalWagerCents, round.Settlement);
    }

    public static HouseHoldemSettlement Settle(HouseHoldemRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (round.Settlement is not null) return round.Settlement;
        if (!round.Folded && (round.Phase != HouseHoldemPhases.Settled || round.PlayWagerCents == 0))
            throw new InvalidOperationException("The community cards and Play wager must be complete before settlement.");
        if (round.Folded && round.Phase != HouseHoldemPhases.River)
            throw new InvalidOperationException("Folding is only available after the river.");

        var playerCards = round.PlayerCards.Concat(round.CommunityCards).ToArray();
        var dealerCards = round.DealerCards.Concat(round.CommunityCards).ToArray();
        var player = TexasHoldemHandEvaluator.Evaluate(playerCards);
        var dealer = TexasHoldemHandEvaluator.Evaluate(dealerCards);
        var playerRank = HandRank(player, playerCards);
        var dealerRank = HandRank(dealer, dealerCards);
        var dealerQualifies = dealer.Category >= 1;
        var tripsOdds = TripsOdds(playerRank);
        var tripsPayout = tripsOdds == 0 ? 0 : checked(round.TripsCents * (tripsOdds + 1));
        var outcome = round.Folded ? "fold" : player.Score > dealer.Score ? "win" : player.Score < dealer.Score ? "lose" : "push";
        var antePayout = round.Folded ? 0 : !dealerQualifies || outcome == "push" ? round.AnteCents
            : outcome == "win" ? checked(round.AnteCents * 2) : 0;
        var playPayout = outcome == "win" ? checked(round.PlayWagerCents * 2)
            : outcome == "push" ? round.PlayWagerCents : 0;
        var blindOdds = BlindOdds(playerRank);
        var blindPayout = outcome switch
        {
            "win" when blindOdds > 0 => checked(round.BlindCents + round.BlindCents * blindOdds / 2),
            "win" or "push" => round.BlindCents,
            _ => 0,
        };
        round.Settlement = new(outcome, playerRank, dealerRank, dealerQualifies,
            antePayout, blindPayout, playPayout, tripsPayout);
        round.Phase = HouseHoldemPhases.Settled;
        return round.Settlement;
    }

    private static void ValidateBet(long amountCents, bool optional, long maximumCents, string parameter)
    {
        if (optional && amountCents == 0) return;
        if (amountCents < MinimumBetCents || amountCents > maximumCents || amountCents % BetIncrementCents != 0)
            throw new ArgumentOutOfRangeException(parameter,
                $"Choose whole-rand wagers between R{MinimumBetCents / 100} and R{maximumCents / 100}.");
    }

    private static string HandRank(HoldemHandValue value, IReadOnlyList<string> cards) =>
        value.Category == 8 && IsRoyalFlush(cards) ? "royal-flush" : value.Name;

    private static bool IsRoyalFlush(IReadOnlyList<string> cardCodes)
    {
        var cards = cardCodes.Select(CardCode.Parse);
        return cards.GroupBy(card => card.Suit).Any(group =>
            group.Select(card => card.Rank).ToHashSet().IsSupersetOf(
                [CardRank.Ten, CardRank.Jack, CardRank.Queen, CardRank.King, CardRank.Ace]));
    }

    // Blind odds are doubled to express 3:2 exactly in integral cents.
    private static int BlindOdds(string rank) => rank switch
    {
        "royal-flush" => 1000,
        "straight-flush" => 100,
        "four-of-a-kind" => 20,
        "full-house" => 6,
        "flush" => 3,
        "straight" => 2,
        _ => 0,
    };

    private static int TripsOdds(string rank) => rank switch
    {
        "royal-flush" => 50,
        "straight-flush" => 40,
        "four-of-a-kind" => 30,
        "full-house" => 9,
        "flush" => 7,
        "straight" => 4,
        "three-of-a-kind" => 3,
        _ => 0,
    };
}
