using FortuneForge.Games.Slots;

namespace FortuneForge.Games.CosmicFortune;

public sealed record SealedTombTransition(SealedTombState After, long PayoutPoints);

/// <summary>One production expedition policy, independent of its wallet and browser presentation.</summary>
public static class SealedTombRules
{
    public const int ContractVersion = 1;
    public const int SigilTarget = 6;
    public const int TorchCount = 6;
    public const int KeyRequirement = 3;
    public const long BaseMultiplier = 5;
    private static readonly string[] Relics = ["sun", "moon", "serpent"];

    public static SealedTombState Accrue(SealedTombState before, Guid spinId, long wagerPoints,
        bool isFreeSpin, int redScarabs, string? engineVersion = null)
    {
        SealedTombState.Validate(before);
        if (before.ActiveRun is not null || before.LastRun is not null)
            throw new SealedTombConflictException("tomb-expedition-active", "Finish The Sealed Tomb and review its treasure before spinning the reels.");
        if (spinId == Guid.Empty) throw new ArgumentException("A funded sigil requires its settled spin identity.");
        var current = (engineVersion ?? CosmicFortuneEngine.EngineVersion) == CosmicFortuneEngine.EngineVersion;
        var minimum = current ? before.FundingMinimumScarabs : 2;
        var after = before with { Revision = checked(before.Revision + 1),
            MinimumScarabs = current ? minimum : before.MinimumScarabs };
        if (isFreeSpin || redScarabs < minimum) return after;
        CosmicFortuneEngine.ValidateWager(wagerPoints, false);
        var count = before.Sigils + 1;
        var total = checked(before.SigilWagerPointTotal + wagerPoints);
        return count < SigilTarget ? after with { Sigils = count, SigilWagerPointTotal = total } :
            after with
            {
                Sigils = 0,
                SigilWagerPointTotal = 0,
                Tickets = [.. before.Tickets, new($"tomb-{spinId:N}", current && minimum >= 3 ?
                    total / count : SealedTombState.Average(total, count))],
                MinimumScarabs = current ? PharaohsAscentMathV3.Current.TombMinimumScarabs : before.MinimumScarabs,
            };
    }

    public static SealedTombTransition Act(SealedTombState before, SealedTombAction action, IGameRandomSource random)
    {
        SealedTombState.Validate(before);
        if (action.ExpectedRevision != before.Revision)
            throw new SealedTombConflictException("tomb-revision-changed", "The tomb changed. Refresh its state before choosing another room.");
        if (action.Action != "open-room" && action.ChoiceId is not null)
            throw new ArgumentException("Only opening a room accepts a room choice.");
        var after = before with { Revision = checked(before.Revision + 1) };
        long payout = 0;
        switch (action.Action)
        {
            case "enter":
                if (before.ActiveRun is not null || before.LastRun is not null || before.Tickets.Count == 0)
                    throw new SealedTombConflictException("tomb-ticket-unavailable", "Earn a Sealed Tomb ticket before entering.");
                var ticket = before.Tickets[0];
                after = after with { Tickets = before.Tickets.Skip(1).ToArray(), LastRun = null,
                    ActiveRun = Offer(new(ticket.Id, ticket.FundedWagerPoints, [], []), random) };
                break;
            case "open-room":
                var run = before.ActiveRun ?? throw MissingRun();
                if (run.Phase != "explore") throw new SealedTombConflictException("tomb-sarcophagus-ready", "Open the sarcophagus to finish this expedition.");
                var choice = run.Choices.SingleOrDefault(choice => choice.Id == action.ChoiceId) ??
                    throw new SealedTombConflictException("tomb-choice-unavailable", "Choose one of the currently offered rooms.");
                var multiplier = choice.Kind == "coin" ? Next(random, 100) switch { < 50 => 2, < 85 => 3, _ => 4 } : 1;
                var next = run with { Path = [.. run.Path, new(run.Path.Count + 1, choice.Kind, choice.Relic,
                    checked(run.FundedWagerPoints * multiplier))], Choices = [] };
                if (next.Keys >= KeyRequirement) after = after with { ActiveRun = next };
                else if (next.TorchesRemaining == 0)
                    (after, payout) = Complete(after, next, 0, "torches");
                else after = after with { ActiveRun = Offer(next, random) };
                break;
            case "open-sarcophagus":
                var finale = before.ActiveRun ?? throw MissingRun();
                if (finale.Phase != "finale") throw new SealedTombConflictException("tomb-keys-required", "Three keys unlock the sarcophagus.");
                var finaleMultiplier = Next(random, 100) switch { < 60 => 8, < 90 => 12, _ => 20 };
                (after, payout) = Complete(after, finale, checked(finale.FundedWagerPoints * finaleMultiplier), "sarcophagus");
                break;
            case "collect":
                var collect = before.ActiveRun ?? throw MissingRun();
                if (collect.Phase != "explore") throw new SealedTombConflictException("tomb-sarcophagus-ready", "Open the unlocked sarcophagus before collecting.");
                (after, payout) = Complete(after, collect, 0, "collect");
                break;
            case "dismiss":
                if (before.ActiveRun is not null || before.LastRun is null)
                    throw new SealedTombConflictException("tomb-result-unavailable", "There is no completed expedition to dismiss.");
                after = after with { LastRun = null };
                break;
            default:
                throw new ArgumentException("Choose a supported Sealed Tomb action.");
        }
        SealedTombState.Validate(after);
        return new(after, payout);
    }

    internal static string ChoiceId(string runId, int step, string kind) => $"{runId}-{step}-{kind}";
    private static SealedTombRun Offer(SealedTombRun run, IGameRandomSource random) => run with
    {
        Choices = [new(ChoiceId(run.Id, run.Path.Count, "coin"), "coin", null),
            new(ChoiceId(run.Id, run.Path.Count, "relic"), "relic", Relics[Next(random, Relics.Length)]),
            new(ChoiceId(run.Id, run.Path.Count, "key"), "key", null)],
    };
    private static (SealedTombState State, long Payout) Complete(SealedTombState state, SealedTombRun run,
        long finalePoints, string reason)
    {
        var total = checked(run.BankedPoints + finalePoints);
        return (state with { ActiveRun = null, LastRun = new(run.Id, run.FundedWagerPoints, run.Path.Count,
            run.Keys, run.Relics, run.RelicMultiplier, run.BasePoints, run.RoomPoints, finalePoints, total, reason) }, total);
    }
    private static int Next(IGameRandomSource random, int bound)
    {
        var value = random.Next(bound);
        return value >= 0 && value < bound ? value : throw new InvalidOperationException("The tomb random source returned an invalid value.");
    }
    private static SealedTombConflictException MissingRun() => new("tomb-expedition-unavailable", "Enter The Sealed Tomb before choosing a room.");
}

public sealed class SealedTombConflictException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
