using FortuneForge.Games.Slots;

namespace FortuneForge.Games.CrookedRoad;

public sealed record CrookedRoadFeatureEvent(string Kind, bool Completed, CrookedRoadBonusView Bonus);
public sealed record CrookedRoadOutcome(Guid SpinId, DateTime GeneratedAtUtc, string Kind, long WagerPoints,
    string[][] Reels, SpinPayout Payout, CrookedRoadSavedState After, CrookedRoadFeatureEvent? SpinFeature)
{
    public string GameId { get; init; } = CrookedRoadRules.GameId;
    public string EngineVersion { get; init; } = CrookedRoadRules.EngineVersion;
    public string MathModelId { get; init; } = CrookedRoadRules.Data.ModelId;
}

/// <summary>Fresh game-owned reel and destination rules. Financial admission lives in the server package.</summary>
public static class CrookedRoadGame
{
    public static void Admit(CrookedRoadSavedState state, long wager, bool free)
    {
        CrookedRoadState.Validate(state);
        CrookedRoadRules.ValidateWager(wager, free);
        if (free)
        {
            if (state.ActiveBonus is not { } bonus) throw new CrookedRoadConflictException("free-spins-unavailable", "No destination play is active.");
            if (wager != bonus.FundedWagerPoints) throw new CrookedRoadConflictException("free-spin-wager-locked", "Use the destination's funded wager.");
            if (CrookedRoadViews.ActionRequired(state)) throw new CrookedRoadConflictException("feature-action-required", "Make your destination choice before playing.");
        }
        else
        {
            if (state.ActiveBonus is not null) throw new CrookedRoadConflictException("free-spins-required", "Finish the destination before placing another wager.");
            var possibleTickets = 1 + (state.Funding.Spins == CrookedRoadRules.Data.FundingSpins - 1 ? 1 : 0);
            if (state.Tickets.Length + possibleTickets > CrookedRoadRules.MaximumTickets)
                throw new CrookedRoadConflictException("road-ticket-bank-full", "Use a funded destination ticket before another wager.");
        }
    }

    public static CrookedRoadOutcome Spin(CrookedRoadSavedState before, long wager, bool free,
        Guid spinId, DateTime generatedAtUtc, IGameRandomSource random)
    {
        Admit(before, wager, free);
        if (free) return PlayBonus(before, spinId, generatedAtUtc, random);
        var reels = DrawBoard(random);
        var payout = CrookedRoadRules.Evaluate(reels, wager);
        var funding = before.Funding;
        var total = checked(funding.WagerPointsTotal + wager);
        var count = funding.Spins + 1;
        var tickets = before.Tickets.ToList();
        if (count == CrookedRoadRules.Data.FundingSpins)
        {
            tickets.Add(new($"{spinId:N}:road", total / count));
            count = 0;
            total = 0;
        }
        if (reels.Sum(reel => reel.Count(symbol => symbol == "FREE")) >= CrookedRoadRules.Data.ScatterRequired)
            tickets.Add(new($"{spinId:N}:scatter", wager));
        var after = before with { Funding = new(count, CrookedRoadRules.Data.FundingSpins, total), Tickets = tickets.ToArray(),
            Revision = checked(before.Revision + 1) };
        after = CrookedRoadMap.EarnTile(after, CrookedRoadMap.TileKinds[random.Next(CrookedRoadMap.TileKinds.Count)]);
        CrookedRoadState.Validate(after);
        return new(spinId, generatedAtUtc, "spin", wager, reels, payout, after, null);
    }

    public static CrookedRoadBonus Begin(CrookedRoadTicket ticket, string destination, IGameRandomSource random)
    {
        int[][]? doors = null;
        if (destination == "hill")
        {
            doors = CrookedRoadRules.Data.HillPrizes.Select(stage =>
            {
                var deck = stage.ToArray();
                for (var index = deck.Length - 1; index > 0; index--)
                {
                    var selected = random.Next(index + 1);
                    (deck[index], deck[selected]) = (deck[selected], deck[index]);
                }
                return deck;
            }).ToArray();
        }
        return new(ticket.Id, destination, ticket.FundedWagerPoints, 0, 0, null, null,
            destination == "fort" ? new long[9] : null, destination == "fort" ? 3 : 0, doors, []);
    }

    private static CrookedRoadOutcome PlayBonus(CrookedRoadSavedState before, Guid spinId, DateTime generated,
        IGameRandomSource random)
    {
        var bonus = before.ActiveBonus!;
        var wager = bonus.FundedWagerPoints;
        var reels = Enumerable.Range(0, 5).Select(_ => new[] { "POWER", "POWER", "POWER" }).ToArray();
        var award = 0L;
        var completed = false;
        switch (bonus.Kind)
        {
            case "well":
                reels = DrawBoard(random);
                var origin = bonus.Choice!.Value;
                for (var reel = 0; reel < 5; reel++)
                    for (var row = 0; row < 3; row++)
                        if (Math.Abs(reel - 2) + Math.Abs(row - origin) <= bonus.Step) reels[reel][row] = "ACE";
                var score = CrookedRoadRules.Data.Paylines.Sum(route => CrookedRoadRules.LineScore(
                    route.Select((row, reel) => reels[reel][row]).ToArray()).Units);
                award = CrookedRoadMath.WellAward(wager, score, origin);
                bonus = bonus with { Step = bonus.Step + 1, Board = reels, AwardPoints = checked(bonus.AwardPoints + award) };
                completed = bonus.Step == 3;
                break;
            case "fort":
                var held = bonus.Held!.ToArray();
                var landed = false;
                for (var cell = 0; cell < 9; cell++)
                {
                    if (held[cell] != 0 || random.Next(CrookedRoadRules.Data.FortLandingDenominator) != 0) continue;
                    held[cell] = DrawCoin(random);
                    landed = true;
                }
                var remaining = landed ? 3 : bonus.RespinsRemaining - 1;
                completed = remaining == 0 || held.All(value => value != 0);
                if (completed) award = CrookedRoadMath.FortAward(wager, held.Sum());
                bonus = bonus with { Held = held, Step = bonus.Step + 1, RespinsRemaining = completed ? 0 : remaining,
                    AwardPoints = award };
                break;
            case "hill":
                var choice = bonus.Choice!.Value;
                var prize = bonus.HiddenDoors![bonus.Step][choice];
                award = checked(wager * prize);
                var revealed = Enumerable.Range(0, 3).Select(index => new CrookedRoadDoor(index, index == choice,
                    index == choice, index == choice ? award : null)).ToArray();
                bonus = bonus with { Step = bonus.Step + 1, Choice = null, AwardPoints = checked(bonus.AwardPoints + award), LastDoors = revealed };
                completed = bonus.Step == 3;
                break;
            default: throw new InvalidOperationException("Unknown destination.");
        }
        var view = CrookedRoadViews.Public(bonus);
        if (bonus.Kind == "hill") view = view with { Doors = bonus.LastDoors };
        var after = before with { Revision = checked(before.Revision + 1), ActiveBonus = completed ? null : bonus,
            CompletedJourneys = checked(before.CompletedJourneys + (completed ? 1 : 0)) };
        CrookedRoadState.Validate(after);
        // Bonus awards are feature awards, never fabricated payline wins.
        return new(spinId, generated, "bonus", wager, reels, new(award, []), after, new(bonus.Kind, completed, view));
    }

    private static string[][] DrawBoard(IGameRandomSource random) => Enumerable.Range(0, 5)
        .Select(_ => Enumerable.Range(0, 3).Select(_ => CrookedRoadRules.DrawSymbol(random)).ToArray()).ToArray();
    private static int DrawCoin(IGameRandomSource random)
    {
        var ticket = random.Next(CrookedRoadRules.Data.FortCoinWeights.Sum());
        for (var index = 0; index < CrookedRoadRules.Data.FortCoinWeights.Length; index++)
        {
            ticket -= CrookedRoadRules.Data.FortCoinWeights[index];
            if (ticket < 0) return CrookedRoadRules.Data.FortCoinValues[index];
        }
        throw new InvalidOperationException("Invalid Fort coin ticket.");
    }
}
