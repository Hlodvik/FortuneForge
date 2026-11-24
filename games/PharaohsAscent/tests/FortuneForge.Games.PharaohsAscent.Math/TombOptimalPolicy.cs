using System.Collections.Frozen;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.PharaohsAscent.RtpAnalysis;

/// <summary>
/// Test-only finite-horizon decision analysis. It selects a publicly offered room;
/// every simulated outcome and settlement must still execute SealedTombRules.
/// </summary>
public static class TombOptimalPolicy
{
    // D(r)=5*60^r is a common denominator for every value with r rooms left:
    // coin probabilities have denominator 20, the next observed relic has 3
    // equally likely values, and the sarcophagus mean is 52/5 stake units.
    // Integer comparisons therefore select exact optima, including exact ties.
    private static readonly long[] Denominators = CreateDenominators();
    private static readonly PolicyAnalysis Analysis = Build();

    public static long ExactNumerator => Analysis.Numerator;
    public static long ExactDenominator => Analysis.Denominator;
    public static decimal ExpectedPayoutMultiplier => ExactNumerator / (decimal)ExactDenominator;
    public static int ObservedStateCount => Analysis.Decisions.Count;
    public static int CollectOptimalStateCount => Analysis.Decisions.Values.Count(decision => decision.Kind == "collect");

    /// <summary>Uses only the current funded path and the relic visibly offered on its door.</summary>
    public static string ChooseRoomKind(SealedTombState state)
    {
        var key = Observe(state);
        return Analysis.Decisions.TryGetValue(key, out var decision) ? decision.Kind :
            throw new InvalidOperationException("The observed production state is outside the verified six-room policy.");
    }

    public static SealedTombAction ChooseAction(SealedTombState state)
    {
        SealedTombState.Validate(state);
        var run = state.ActiveRun ?? throw new InvalidOperationException("An active expedition is required.");
        if (run.Phase == "finale") return new("open-sarcophagus", state.Revision);
        var kind = ChooseRoomKind(state);
        return kind == "collect" ? new("collect", state.Revision) :
            new("open-room", state.Revision, run.Choices.Single(choice => choice.Kind == kind).Id);
    }

    public static IReadOnlyDictionary<string, decimal> EvaluateChoices(SealedTombState state)
    {
        var key = Observe(state);
        if (!Analysis.Decisions.TryGetValue(key, out var decision))
            throw new InvalidOperationException("The observed production state is outside the verified six-room policy.");
        var denominator = (decimal)Denominators[key.Remaining];
        return new Dictionary<string, decimal>
        {
            ["collect"] = decision.Collect / denominator,
            ["coin"] = decision.Coin / denominator,
            ["relic"] = decision.Relic / denominator,
            ["key"] = decision.Key / denominator,
        };
    }

    private static Observation Observe(SealedTombState state)
    {
        SealedTombState.Validate(state);
        var run = state.ActiveRun ?? throw new InvalidOperationException("An active expedition is required.");
        if (run.Phase != "explore" || run.TorchesRemaining <= 0)
            throw new InvalidOperationException("Choose a room only while exploring.");
        if (run.RoomPoints % run.FundedWagerPoints != 0)
            throw new InvalidOperationException("The production room prizes are not integral funded-stake units.");
        var offered = run.Choices.Single(choice => choice.Kind == "relic").Relic switch
        {
            "sun" => 0, "moon" => 1, "serpent" => 2,
            _ => throw new InvalidOperationException("The offered relic is unknown."),
        };
        return new(run.TorchesRemaining, checked((int)(run.RoomPoints / run.FundedWagerPoints)),
            run.Relics.Sun, run.Relics.Moon, run.Relics.Serpent, run.Keys, offered);
    }

    private static PolicyAnalysis Build()
    {
        VerifyProductionKernel();
        var decisions = new Dictionary<Observation, Decision>();
        long numerator = 0;
        for (var offered = 0; offered < 3; offered++)
            numerator = checked(numerator + Value(new(SealedTombRules.TorchCount, 0, 0, 0, 0, 0, offered), decisions));
        var denominator = checked(3 * Denominators[SealedTombRules.TorchCount]);
        var divisor = Gcd(numerator, denominator);
        return new(decisions.ToFrozenDictionary(), numerator / divisor, denominator / divisor);
    }

    private static long Value(Observation state, Dictionary<Observation, Decision> cache)
    {
        var multiplier = 1 + System.Math.Min(state.Sun, System.Math.Min(state.Moon, state.Serpent));
        if (state.Remaining == 0)
            return checked((SealedTombRules.BaseMultiplier + state.RawPrizes * multiplier) * Denominators[0]);
        if (cache.TryGetValue(state, out var saved)) return saved.Value;
        var scale = Denominators[state.Remaining];
        var next = state with { Remaining = state.Remaining - 1 };

        long Future(Observation successor) => checked(
            Value(successor with { OfferedRelic = 0 }, cache) +
            Value(successor with { OfferedRelic = 1 }, cache) +
            Value(successor with { OfferedRelic = 2 }, cache));

        var collect = checked((SealedTombRules.BaseMultiplier + state.RawPrizes * multiplier) * scale);
        var coin = checked(10 * Future(next with { RawPrizes = state.RawPrizes + 2 }) +
            7 * Future(next with { RawPrizes = state.RawPrizes + 3 }) +
            3 * Future(next with { RawPrizes = state.RawPrizes + 4 }));
        var relicNext = next with { RawPrizes = state.RawPrizes + 1 };
        relicNext = state.OfferedRelic switch
        {
            0 => relicNext with { Sun = state.Sun + 1 },
            1 => relicNext with { Moon = state.Moon + 1 },
            2 => relicNext with { Serpent = state.Serpent + 1 },
            _ => throw new InvalidOperationException("The decision-analysis relic is unknown."),
        };
        var relic = checked(20 * Future(relicNext));
        // Production tests the third key before the sixth-room completion.
        var key = state.Keys + 1 == SealedTombRules.KeyRequirement
            ? checked(((SealedTombRules.BaseMultiplier + (state.RawPrizes + 1) * multiplier) * 5 + 52) * (scale / 5))
            : checked(20 * Future(next with { RawPrizes = state.RawPrizes + 1, Keys = state.Keys + 1 }));
        var kind = "collect";
        var best = collect;
        foreach (var candidate in new[] { (Kind: "coin", Value: coin), (Kind: "relic", Value: relic), (Kind: "key", Value: key) })
            if (candidate.Value > best) { kind = candidate.Kind; best = candidate.Value; }
        cache.Add(state, new(kind, best, collect, coin, relic, key));
        return best;
    }

    /// <summary>
    /// Check analysis probabilities and boundary order against the real rules.
    /// Fixed draws are confined to this isolated math verification, never play.
    /// </summary>
    public static void VerifyProductionKernel()
    {
        Ensure(SealedTombRules.TorchCount == 6 && SealedTombRules.KeyRequirement == 3 && SealedTombRules.BaseMultiplier == 5,
            "The expedition structure changed; revise the decision analysis.");
        const long stake = 2;
        var ticket = SealedTombState.Empty with { Tickets = [new("tomb-policy-proof-0001", stake)] };
        var entered = SealedTombRules.Act(ticket, new("enter", ticket.Revision), new FixedDraws(0, 0));
        Ensure(entered.PayoutPoints == 0 && entered.After.ActiveRun is { TorchesRemaining: 6, BankedPoints: 10 },
            "The secured base, torch count, or entry settlement changed.");
        var names = new[] { "sun", "moon", "serpent" };
        for (var index = 0; index < names.Length; index++)
        {
            var offered = SealedTombRules.Act(ticket, new("enter", ticket.Revision), new FixedDraws(0, index));
            Ensure(offered.After.ActiveRun!.Choices.Single(choice => choice.Kind == "relic").Relic == names[index],
                "The public relic-offer distribution changed.");
        }
        long coinTotal = 0;
        for (var roll = 0; roll < 100; roll++)
        {
            var opened = Open(entered.After, "coin", new FixedDraws(roll, 0));
            var units = opened.After.ActiveRun!.Path[^1].AwardPoints / stake;
            Ensure(units == (roll < 50 ? 2 : roll < 85 ? 3 : 4), "The coin reward distribution changed.");
            Ensure(opened.PayoutPoints == 0, "Opening an unfinished room unexpectedly settles a reward.");
            coinTotal += units;
        }
        Ensure(coinTotal == 265, "The coin expectation is no longer 53/20.");

        var finaleState = entered.After;
        foreach (var kind in new[] { "coin", "coin", "coin", "key", "key", "key" })
        {
            var step = Open(finaleState, kind, new FixedDraws(0, 0));
            Ensure(step.PayoutPoints == 0, "The third-key finale was settled as a sixth-room completion.");
            finaleState = step.After;
        }
        Ensure(finaleState.ActiveRun is { Phase: "finale", TorchesRemaining: 0, Keys: 3 },
            "A third key in room six must still reach the finale.");
        long finaleTotal = 0;
        for (var roll = 0; roll < 100; roll++)
        {
            var finished = SealedTombRules.Act(finaleState, new("open-sarcophagus", finaleState.Revision), new FixedDraws(roll, 0));
            var units = finished.After.LastRun!.FinalePoints / stake;
            Ensure(units == (roll < 60 ? 8 : roll < 90 ? 12 : 20), "The sarcophagus reward distribution changed.");
            Ensure(finished.PayoutPoints == finished.After.LastRun.PayoutPoints, "The finale settlement disagrees with its receipt.");
            finaleTotal += units;
        }
        Ensure(finaleTotal == 1_040, "The sarcophagus expectation is no longer 52/5.");

        var relicState = Open(entered.After, "relic", new FixedDraws(0, 1)).After;
        relicState = Open(relicState, "relic", new FixedDraws(0, 2)).After;
        relicState = Open(relicState, "relic", new FixedDraws(0, 0)).After;
        Ensure(relicState.ActiveRun is { RelicMultiplier: 2, RoomPoints: 6, BankedPoints: 22 },
            "A complete relic set must multiply all accumulated room prizes, leaving the base unchanged.");
        relicState = Open(relicState, "relic", new FixedDraws(0, 1)).After;
        relicState = Open(relicState, "relic", new FixedDraws(0, 2)).After;
        var secondSet = Open(relicState, "relic", new FixedDraws(0, 0));
        Ensure(secondSet.After.ActiveRun is null && secondSet.After.LastRun is
            { Reason: "torches", RelicMultiplier: 3, RoomPoints: 12, BasePoints: 10, PayoutPoints: 46 } && secondSet.PayoutPoints == 46,
            "A second complete relic set must triple all six room prizes and settle when the sixth torch is used.");
    }

    private static SealedTombTransition Open(SealedTombState state, string kind, IGameRandomSource random) =>
        SealedTombRules.Act(state, new("open-room", state.Revision,
            state.ActiveRun!.Choices.Single(choice => choice.Kind == kind).Id), random);

    private static long[] CreateDenominators()
    {
        var result = new long[SealedTombRules.TorchCount + 1];
        result[0] = 5;
        for (var remaining = 1; remaining < result.Length; remaining++) result[remaining] = checked(result[remaining - 1] * 60);
        return result;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct Observation(int Remaining, int RawPrizes, int Sun, int Moon, int Serpent, int Keys, int OfferedRelic);
    private sealed record Decision(string Kind, long Value, long Collect, long Coin, long Relic, long Key);
    private sealed record PolicyAnalysis(FrozenDictionary<Observation, Decision> Decisions, long Numerator, long Denominator);
    private sealed class FixedDraws(int roll, int relic) : IGameRandomSource
    {
        public int Next(int bound) => bound switch
        {
            100 => roll,
            3 => relic,
            _ => throw new InvalidOperationException("The production random kernel changed its draw bounds."),
        };
    }
}
