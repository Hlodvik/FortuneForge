namespace FortuneForge.Games.Blackjack;

public static class BlackjackRules
{
    private static readonly string[] Suits = ["clubs", "diamonds", "hearts", "spades"];
    private static readonly string[] Ranks =
        ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];

    public static IReadOnlyList<string> CreateShuffledDeck()
    {
        var deck = Suits
            .SelectMany(suit => Ranks.Select(rank => $"{rank}|{suit}"))
            .ToArray();
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var replacement = System.Security.Cryptography.RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[replacement]) = (deck[replacement], deck[index]);
        }
        return deck;
    }

    public static BlackjackGame Deal(
        string gameId,
        string userId,
        long wagerCents,
        IReadOnlyList<string> deck,
        DateTime nowUtc)
    {
        ValidateDeck(deck);
        var playerCards = new[] { deck[0], deck[2] };
        var dealerCards = new[] { deck[1], deck[3] };
        var player = Score(playerCards);
        var dealer = Score(dealerCards);
        var outcome = player.Blackjack || dealer.Blackjack
            ? NaturalOutcome(player.Blackjack, dealer.Blackjack)
            : null;
        var payoutCents = outcome is null ? 0 : PayoutFor(outcome, wagerCents);

        return new BlackjackGame(
            gameId,
            userId,
            wagerCents,
            wagerCents,
            payoutCents,
            outcome is null ? BlackjackStatuses.Active : BlackjackStatuses.Completed,
            outcome,
            deck.ToArray(),
            4,
            playerCards,
            dealerCards,
            1,
            nowUtc,
            nowUtc);
    }

    public static BlackjackGame ApplyAction(BlackjackGame game, string action, DateTime nowUtc)
    {
        if (game.Status != BlackjackStatuses.Active)
        {
            throw new BlackjackConflictException("This Blackjack hand has already finished.");
        }

        return action.Trim().ToLowerInvariant() switch
        {
            BlackjackActions.Hit => Hit(game, nowUtc),
            BlackjackActions.Stand => FinishDealer(game, game.PlayerCards, game.TotalWagerCents, nowUtc),
            BlackjackActions.Double => Double(game, nowUtc),
            _ => throw new ArgumentException("Choose hit, stand, or double.", nameof(action))
        };
    }

    public static BlackjackHandValue Score(IReadOnlyList<string> cards)
    {
        var score = 0;
        var aces = 0;
        foreach (var card in cards)
        {
            var rank = ParseCard(card).Rank;
            if (rank == "A")
            {
                aces++;
                score += 11;
            }
            else if (rank is "J" or "Q" or "K")
            {
                score += 10;
            }
            else
            {
                score += int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        while (score > 21 && aces > 0)
        {
            score -= 10;
            aces--;
        }

        return new BlackjackHandValue(
            score,
            aces > 0,
            cards.Count == 2 && score == 21,
            score > 21);
    }

    public static (string Rank, string Suit) ParseCard(string code)
    {
        var separator = code.IndexOf('|');
        if (separator <= 0 || separator >= code.Length - 1)
        {
            throw new InvalidOperationException("A stored Blackjack card is invalid.");
        }
        return (code[..separator], code[(separator + 1)..]);
    }

    private static BlackjackGame Hit(BlackjackGame game, DateTime nowUtc)
    {
        var (card, nextIndex) = Draw(game);
        var playerCards = game.PlayerCards.Append(card).ToArray();
        var score = Score(playerCards);
        if (score.Bust)
        {
            return Complete(game, playerCards, game.DealerCards, nextIndex, BlackjackOutcomes.PlayerBust, game.TotalWagerCents, nowUtc);
        }
        if (score.Score == 21)
        {
            return FinishDealer(game with { NextCardIndex = nextIndex }, playerCards, game.TotalWagerCents, nowUtc);
        }
        return game with
        {
            PlayerCards = playerCards,
            NextCardIndex = nextIndex,
            Version = checked(game.Version + 1),
            UpdatedAtUtc = nowUtc
        };
    }

    private static BlackjackGame Double(BlackjackGame game, DateTime nowUtc)
    {
        if (game.PlayerCards.Count != 2)
        {
            throw new BlackjackConflictException("Double is available only before the first hit.");
        }
        var doubledWager = checked(game.WagerCents * 2);
        var (card, nextIndex) = Draw(game);
        var playerCards = game.PlayerCards.Append(card).ToArray();
        if (Score(playerCards).Bust)
        {
            return Complete(game, playerCards, game.DealerCards, nextIndex, BlackjackOutcomes.PlayerBust, doubledWager, nowUtc);
        }
        return FinishDealer(
            game with { NextCardIndex = nextIndex },
            playerCards,
            doubledWager,
            nowUtc);
    }

    private static BlackjackGame FinishDealer(
        BlackjackGame game,
        IReadOnlyList<string> playerCards,
        long totalWagerCents,
        DateTime nowUtc)
    {
        var dealerCards = game.DealerCards.ToList();
        var nextIndex = game.NextCardIndex;
        while (Score(dealerCards).Score < 17)
        {
            if (nextIndex >= game.Deck.Count)
            {
                throw new InvalidOperationException("The Blackjack deck ran out of cards.");
            }
            dealerCards.Add(game.Deck[nextIndex++]);
        }

        var playerScore = Score(playerCards).Score;
        var dealerScore = Score(dealerCards).Score;
        var outcome = dealerScore > 21 || playerScore > dealerScore
            ? BlackjackOutcomes.PlayerWin
            : playerScore == dealerScore
                ? BlackjackOutcomes.Push
                : BlackjackOutcomes.DealerWin;
        return Complete(game, playerCards, dealerCards, nextIndex, outcome, totalWagerCents, nowUtc);
    }

    private static BlackjackGame Complete(
        BlackjackGame game,
        IReadOnlyList<string> playerCards,
        IReadOnlyList<string> dealerCards,
        int nextCardIndex,
        string outcome,
        long totalWagerCents,
        DateTime nowUtc) => game with
        {
            TotalWagerCents = totalWagerCents,
            PayoutCents = PayoutFor(outcome, totalWagerCents),
            Status = BlackjackStatuses.Completed,
            Outcome = outcome,
            PlayerCards = playerCards.ToArray(),
            DealerCards = dealerCards.ToArray(),
            NextCardIndex = nextCardIndex,
            Version = checked(game.Version + 1),
            UpdatedAtUtc = nowUtc
        };

    private static long PayoutFor(string outcome, long wagerCents) => outcome switch
    {
        BlackjackOutcomes.PlayerBlackjack => checked(wagerCents * 5 / 2),
        BlackjackOutcomes.PlayerWin => checked(wagerCents * 2),
        BlackjackOutcomes.Push => wagerCents,
        _ => 0
    };

    private static string NaturalOutcome(bool playerBlackjack, bool dealerBlackjack) =>
        playerBlackjack && dealerBlackjack
            ? BlackjackOutcomes.Push
            : playerBlackjack
                ? BlackjackOutcomes.PlayerBlackjack
                : BlackjackOutcomes.DealerBlackjack;

    private static (string Card, int NextIndex) Draw(BlackjackGame game)
    {
        if (game.NextCardIndex >= game.Deck.Count)
        {
            throw new InvalidOperationException("The Blackjack deck ran out of cards.");
        }
        return (game.Deck[game.NextCardIndex], game.NextCardIndex + 1);
    }

    private static void ValidateDeck(IReadOnlyList<string> deck)
    {
        if (deck.Count != 52 || deck.Distinct(StringComparer.Ordinal).Count() != 52)
        {
            throw new ArgumentException("A Blackjack deck must contain 52 unique cards.", nameof(deck));
        }
        foreach (var card in deck)
        {
            _ = ParseCard(card);
        }
    }
}
