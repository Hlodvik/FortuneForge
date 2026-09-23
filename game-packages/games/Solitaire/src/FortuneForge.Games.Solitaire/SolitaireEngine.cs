namespace FortuneForge.Games.Solitaire;

public static class SolitaireEngine
{
    private static readonly string[] Suits = ["clubs", "diamonds", "hearts", "spades"];

    public static SolitaireGameState CreateGame(uint seed, int drawCount = 3)
    {
        SolitaireRules.ValidateDrawCount(drawCount);
        var cards = new List<SolitaireCard>(52);
        foreach (var suit in Suits)
        {
            for (var rank = 1; rank <= 13; rank++)
            {
                cards.Add(new SolitaireCard($"{suit}-{rank}", suit, rank, false));
            }
        }

        var random = new Mulberry32(seed);
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swapIndex = random.NextIndex(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }

        var tableau = Enumerable.Range(0, 7)
            .Select(_ => new List<SolitaireCard>())
            .ToArray();
        for (var column = 0; column < 7; column++)
        {
            for (var row = 0; row <= column; row++)
            {
                var dealt = cards[^1];
                cards.RemoveAt(cards.Count - 1);
                tableau[column].Add(dealt with { FaceUp = row == column });
            }
        }

        return new SolitaireGameState(
            cards.Select(card => card with { FaceUp = false }).ToArray(),
            [],
            Suits.Select(_ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>()).ToArray(),
            tableau.Select(pile => (IReadOnlyList<SolitaireCard>)pile.ToArray()).ToArray(),
            0,
            0,
            seed,
            "Your move")
        {
            DrawCount = drawCount
        };
    }

    public static SolitaireGameState Apply(SolitaireGameState game, SolitaireCommandRequest request)
    {
        var type = request.Type?.Trim().ToLowerInvariant();
        return type switch
        {
            SolitaireCommandTypes.Draw => Draw(game),
            SolitaireCommandTypes.Flip => Flip(game, request.Column),
            SolitaireCommandTypes.Move => Move(game, request.From, request.StartIndex, request.To),
            _ => throw new SolitaireIllegalMoveException("Choose a draw, flip, or move command.")
        };
    }

    public static bool IsWon(SolitaireGameState game) =>
        game.Foundations.All(foundation => foundation.Count == 13);

    public static SolitaireGameResponse ToResponse(SolitaireGameState game) => new(
        game.Stock.Select(ToResponse).ToArray(),
        game.Waste.Select(ToResponse).ToArray(),
        game.Foundations.Select(pile =>
            (IReadOnlyList<SolitaireCardResponse>)pile.Select(ToResponse).ToArray()).ToArray(),
        game.Tableau.Select(pile =>
            (IReadOnlyList<SolitaireCardResponse>)pile.Select(ToResponse).ToArray()).ToArray(),
        game.DrawCount,
        game.Score,
        game.Moves,
        game.Message);

    private static SolitaireGameState Draw(SolitaireGameState game)
    {
        if (game.Stock.Count == 0)
        {
            if (game.Waste.Count == 0)
            {
                throw new SolitaireIllegalMoveException("There are no cards to draw or recycle.");
            }
            return game with
            {
                Stock = game.Waste.Reverse().Select(card => card with { FaceUp = false }).ToArray(),
                Waste = [],
                Score = Math.Max(0, game.Score - 100),
                Moves = checked(game.Moves + 1),
                Message = game.Score > 0 ? "−100 · Stock recycled" : "Stock recycled"
            };
        }

        var stock = game.Stock.ToList();
        var drawn = new List<SolitaireCard>(game.DrawCount);
        for (var index = 0; index < game.DrawCount && stock.Count > 0; index++)
        {
            var card = stock[^1];
            stock.RemoveAt(stock.Count - 1);
            drawn.Add(card with { FaceUp = true });
        }
        return game with
        {
            Stock = stock,
            Waste = game.Waste.Concat(drawn).ToArray(),
            Moves = checked(game.Moves + 1),
            Message = $"Drew {drawn.Count} {(drawn.Count == 1 ? "card" : "cards")}"
        };
    }

    private static SolitaireGameState Flip(SolitaireGameState game, int? requestedColumn)
    {
        if (requestedColumn is not >= 0 or > 6)
        {
            throw new SolitaireIllegalMoveException("Choose a tableau column from 0 through 6.");
        }
        var column = requestedColumn.Value;
        var pile = game.Tableau[column];
        if (pile.Count == 0 || pile[^1].FaceUp)
        {
            throw new SolitaireIllegalMoveException("Only the top face-down tableau card can be flipped.");
        }
        var nextPile = pile.ToArray();
        nextPile[^1] = nextPile[^1] with { FaceUp = true };
        return game with
        {
            Tableau = Replace(game.Tableau, column, nextPile),
            Score = checked(game.Score + 5),
            Moves = checked(game.Moves + 1),
            Message = "+5 · Card revealed"
        };
    }

    private static SolitaireGameState Move(
        SolitaireGameState game,
        SolitairePileReference? from,
        int? requestedStartIndex,
        SolitairePileReference? to)
    {
        if (from is null || to is null || requestedStartIndex is null)
        {
            throw new SolitaireIllegalMoveException("A move requires from, startIndex, and to.");
        }
        ValidatePile(from, allowWaste: true);
        ValidatePile(to, allowWaste: false);
        if (from.Zone == to.Zone && from.Index == to.Index)
        {
            throw new SolitaireIllegalMoveException("A card cannot move onto its current pile.");
        }

        var source = PileAt(game, from);
        var startIndex = requestedStartIndex.Value;
        if (startIndex < 0 || startIndex >= source.Count)
        {
            throw new SolitaireIllegalMoveException("The selected source card does not exist.");
        }
        var moving = source.Skip(startIndex).ToArray();
        if (!CanLift(from, source, startIndex, moving))
        {
            throw new SolitaireIllegalMoveException("The selected cards are not a movable Klondike run.");
        }

        var destination = PileAt(game, to);
        if (!CanPlace(to, moving, destination))
        {
            throw new SolitaireIllegalMoveException("Those cards cannot be placed on that pile.");
        }

        var remaining = source.Take(startIndex).ToArray();
        var revealed = false;
        if (from.Zone == "tableau" && remaining.Length > 0 && !remaining[^1].FaceUp)
        {
            remaining[^1] = remaining[^1] with { FaceUp = true };
            revealed = true;
        }
        var withoutSource = ReplacePile(game, from, remaining);
        var updated = ReplacePile(withoutSource, to, destination.Concat(moving).ToArray());

        var scoreDelta = 0;
        var reasons = new List<string>();
        if (to.Zone == "foundation")
        {
            scoreDelta += 10;
            reasons.Add("Card home");
        }
        if (from.Zone == "waste" && to.Zone == "tableau")
        {
            scoreDelta += 5;
            reasons.Add("Waste to tableau");
        }
        if (from.Zone == "foundation" && to.Zone == "tableau")
        {
            scoreDelta -= 15;
            reasons.Add("Foundation card returned");
        }
        if (revealed)
        {
            scoreDelta += 5;
            reasons.Add("Card revealed");
        }
        var prefix = scoreDelta > 0 ? $"+{scoreDelta}" : scoreDelta < 0 ? $"−{Math.Abs(scoreDelta)}" : string.Empty;
        return updated with
        {
            Score = Math.Max(0, game.Score + scoreDelta),
            Moves = checked(game.Moves + 1),
            Message = scoreDelta == 0 ? "Nice move" : $"{prefix} · {string.Join(" & ", reasons)}"
        };
    }

    private static bool CanLift(
        SolitairePileReference from,
        IReadOnlyList<SolitaireCard> source,
        int startIndex,
        IReadOnlyList<SolitaireCard> moving)
    {
        if (from.Zone != "tableau")
        {
            return startIndex == source.Count - 1 && moving.Count == 1 && moving[0].FaceUp;
        }
        if (moving.Count == 0 || moving.Any(card => !card.FaceUp))
        {
            return false;
        }
        for (var index = 0; index < moving.Count - 1; index++)
        {
            if (moving[index].Rank != moving[index + 1].Rank + 1 ||
                IsRed(moving[index].Suit) == IsRed(moving[index + 1].Suit))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanPlace(
        SolitairePileReference to,
        IReadOnlyList<SolitaireCard> moving,
        IReadOnlyList<SolitaireCard> destination)
    {
        var lead = moving[0];
        if (to.Zone == "foundation")
        {
            if (moving.Count != 1)
            {
                return false;
            }
            return destination.Count == 0
                ? lead.Rank == 1
                : destination[^1].Suit == lead.Suit && lead.Rank == destination[^1].Rank + 1;
        }
        return destination.Count == 0
            ? lead.Rank == 13
            : destination[^1].FaceUp &&
              destination[^1].Rank == lead.Rank + 1 &&
              IsRed(destination[^1].Suit) != IsRed(lead.Suit);
    }

    private static IReadOnlyList<SolitaireCard> PileAt(
        SolitaireGameState game,
        SolitairePileReference pile) => pile.Zone switch
        {
            "waste" => game.Waste,
            "foundation" => game.Foundations[pile.Index],
            "tableau" => game.Tableau[pile.Index],
            _ => throw new SolitaireIllegalMoveException("The requested pile zone is invalid.")
        };

    private static SolitaireGameState ReplacePile(
        SolitaireGameState game,
        SolitairePileReference pile,
        IReadOnlyList<SolitaireCard> cards) => pile.Zone switch
        {
            "waste" => game with { Waste = cards },
            "foundation" => game with { Foundations = Replace(game.Foundations, pile.Index, cards) },
            "tableau" => game with { Tableau = Replace(game.Tableau, pile.Index, cards) },
            _ => throw new SolitaireIllegalMoveException("The requested pile zone is invalid.")
        };

    private static IReadOnlyList<IReadOnlyList<SolitaireCard>> Replace(
        IReadOnlyList<IReadOnlyList<SolitaireCard>> piles,
        int index,
        IReadOnlyList<SolitaireCard> cards) =>
        piles.Select((pile, pileIndex) => pileIndex == index ? cards : pile).ToArray();

    private static void ValidatePile(SolitairePileReference pile, bool allowWaste)
    {
        if (pile.Zone == "waste")
        {
            if (!allowWaste || pile.Index != 0)
            {
                throw new SolitaireIllegalMoveException("The waste pile reference is invalid.");
            }
            return;
        }
        if (pile.Zone == "foundation" && pile.Index is >= 0 and < 4)
        {
            return;
        }
        if (pile.Zone == "tableau" && pile.Index is >= 0 and < 7)
        {
            return;
        }
        throw new SolitaireIllegalMoveException("The requested pile reference is invalid.");
    }

    private static bool IsRed(string suit) => suit is "diamonds" or "hearts";

    private static SolitaireCardResponse ToResponse(SolitaireCard card) => card.FaceUp
        ? new SolitaireCardResponse(card.Id, card.Suit, card.Rank, true)
        : new SolitaireCardResponse(null, null, null, false);

    private sealed class Mulberry32(uint seed)
    {
        private uint value = seed;

        public int NextIndex(int exclusiveMaximum)
        {
            var random = NextUInt();
            return (int)(((ulong)random * (uint)exclusiveMaximum) >> 32);
        }

        private uint NextUInt()
        {
            unchecked
            {
                value += 0x6d2b79f5u;
                var result = value;
                result = (result ^ (result >> 15)) * (result | 1u);
                result ^= result + (result ^ (result >> 7)) * (result | 61u);
                return result ^ (result >> 14);
            }
        }
    }
}
