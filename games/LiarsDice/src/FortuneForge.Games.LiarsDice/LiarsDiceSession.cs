using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Dice;
using System.Security.Cryptography;

namespace FortuneForge.Games.LiarsDice;

public record LiarsDiceManagedPlayer(string UserId, string PlayerName, int SkillLevel);

public interface ILiarsDiceManagedPlayerRoster
{
    Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token);
    Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token);
    Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token);
}

public sealed record LiarsDiceManagedActor(LiarsDiceManagedPlayer Profile, ulong DecisionSeed, ulong TimingSeed,
    int DecisionVersion = 0, int TimingVersion = 0)
{
    public static LiarsDiceManagedActor Create(LiarsDiceManagedPlayer profile) => new(profile,
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)), BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
    public TimeSpan ThinkDelay() => TimeSpan.FromMilliseconds(350 +
        new DeterministicManagedPlayerRandom(TimingSeed, $"liars-dice-think:{Profile.UserId}:{TimingVersion}").Next(851));
}

public sealed record LiarsDiceResolvedRound(int RoundNumber, LiarsDiceChallengeOutcome Outcome);

public sealed record LiarsDiceAcceptedAction(string Kind, string PlayerId, int Quantity = 0, int Face = 0, uint Seed = 0,
    DateTime? AcceptedAtUtc = null);

/// <summary>Private, durable match including accepted inputs and managed-player decision cursors.</summary>
public sealed record LiarsDiceSession(Guid Id, string UserId, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    uint InitialSeed, uint RoundSeed, LiarsDiceMatchState State,
    IReadOnlyDictionary<string, LiarsDiceManagedActor> ManagedActors,
    IReadOnlyList<LiarsDiceAcceptedAction> Actions, long EventVersion, string EventKind, string Message,
    string? ScheduledManagedPlayerId, DateTime? NextManagedActionAtUtc, IReadOnlyList<LiarsDiceResolvedRound>? ResolvedRounds = null)
{
    public const string GameId = "liars-dice";
    public const string HumanId = "you";
    public static string AssignmentId(Guid id) => $"liars-dice:{id:N}";
    public bool OpponentsThinking => ScheduledManagedPlayerId is not null;

    public static LiarsDiceSession Start(Guid id, string userId, uint seed, int dicePerPlayer,
        IReadOnlyList<LiarsDiceManagedPlayer> players, DateTime nowUtc)
    {
        if (players.Count != 3 || players.Select(player => player.UserId).Distinct(StringComparer.Ordinal).Count() != 3 ||
            players.Any(player => player.UserId is HumanId || player.UserId == userId))
            throw new LiarsDiceRuleException("Three distinct managed opponents are required.");
        var actors = players.ToDictionary(player => player.UserId, LiarsDiceManagedActor.Create, StringComparer.Ordinal);
        var state = LiarsDiceMatchEngine.Start(seed, new[] { HumanId }.Concat(actors.Keys).ToArray(), dicePerPlayer);
        return new(id, userId, nowUtc, null, seed, seed, state, actors, [], 1, "started", "Place a bid.", null, null, ResolvedRounds: []);
    }

    public LiarsDiceSession HumanAction(LiarsDiceCommand command, DateTime nowUtc)
    {
        if (State.CurrentPlayerId != HumanId || command.PlayerId != HumanId)
            throw new LiarsDiceRuleException($"It is {DisplayName(State.CurrentPlayerId)}'s turn.");
        return Apply(command, nowUtc, command switch
        {
            PlaceLiarsDiceBid => "human-bid", ChallengeLiarsDiceBid => "human-challenge", SpotOnLiarsDiceBid => "human-spot-on",
            _ => throw new LiarsDiceRuleException("Unknown Liar's Dice command."),
        });
    }

    public LiarsDiceSession Advance(DateTime nowUtc)
    {
        if (!OpponentsThinking || NextManagedActionAtUtc is not { } due || nowUtc < due ||
            State.Winner is not null || State.Round.Phase == LiarsDiceRoundPhase.Resolved)
            return this;
        var actor = ManagedActors[State.CurrentPlayerId];
        var command = LiarsDiceManagedActionPolicy.Choose(State, actor.Profile.SkillLevel, actor.DecisionSeed, actor.DecisionVersion);
        var actors = new Dictionary<string, LiarsDiceManagedActor>(ManagedActors, StringComparer.Ordinal)
        { [actor.Profile.UserId] = actor with { DecisionVersion = checked(actor.DecisionVersion + 1) } };
        return (this with { ManagedActors = actors }).Apply(command, nowUtc, "managed-advance-request");
    }

    public LiarsDiceSession NextRound(uint seed, DateTime nowUtc)
    {
        RequireActionBudget();
        var next = this with
        {
            State = LiarsDiceMatchEngine.StartNextRound(State, seed), RoundSeed = seed,
            Actions = Actions.Append(new("next-round", "", Seed: seed, AcceptedAtUtc: nowUtc)).ToArray(),
            EventVersion = checked(EventVersion + 1), EventKind = "next-round",
            ScheduledManagedPlayerId = null, NextManagedActionAtUtc = null,
        };
        return (next with { Message = $"Round {next.State.RoundNumber} begins. {next.DisplayName(next.State.CurrentPlayerId)} acts first." }).Schedule(nowUtc);
    }

    private LiarsDiceSession Apply(LiarsDiceCommand command, DateTime nowUtc, string eventKind)
    {
        RequireActionBudget();
        var transition = LiarsDiceMatchEngine.Apply(State, command);
        var action = command switch
        {
            PlaceLiarsDiceBid bid => new LiarsDiceAcceptedAction("bid", bid.PlayerId, bid.Bid.Quantity, bid.Bid.Face.Value, AcceptedAtUtc: nowUtc),
            ChallengeLiarsDiceBid challenge => new("challenge", challenge.PlayerId, AcceptedAtUtc: nowUtc),
            SpotOnLiarsDiceBid spot => new("spot-on", spot.PlayerId, AcceptedAtUtc: nowUtc),
            _ => throw new LiarsDiceRuleException("Unknown Liar's Dice command."),
        };
        return (this with
        {
            State = transition.State, Actions = Actions.Append(action).ToArray(), EventVersion = checked(EventVersion + 1),
            ResolvedRounds = transition.Outcome is { } resolved
                ? (ResolvedRounds ?? []).Append(new(State.RoundNumber, resolved)).ToArray() : ResolvedRounds,
            EventKind = eventKind, CompletedAtUtc = transition.State.Winner is not null ? nowUtc : null,
            Message = transition.Outcome is null ? "Bid placed." : transition.State.Winner is { } winner
                ? $"{DisplayName(winner)} wins the match!" : $"{DisplayName(transition.Outcome.LoserId)} loses a die.",
            ScheduledManagedPlayerId = null, NextManagedActionAtUtc = null,
        }).Schedule(nowUtc);
    }

    private LiarsDiceSession Schedule(DateTime nowUtc)
    {
        if (State.Winner is not null || State.Round.Phase == LiarsDiceRoundPhase.Resolved ||
            !ManagedActors.TryGetValue(State.CurrentPlayerId, out var actor)) return this;
        var actors = new Dictionary<string, LiarsDiceManagedActor>(ManagedActors, StringComparer.Ordinal)
        { [actor.Profile.UserId] = actor with { TimingVersion = checked(actor.TimingVersion + 1) } };
        return this with { ManagedActors = actors, ScheduledManagedPlayerId = actor.Profile.UserId,
            NextManagedActionAtUtc = nowUtc + actor.ThinkDelay() };
    }

    private void RequireActionBudget()
    {
        if (Actions.Count >= 10_000) throw new LiarsDiceRuleException("This match reached its action limit. Start a new match.");
    }

    public string DisplayName(string playerId) => playerId == HumanId ? "You" :
        ManagedActors.TryGetValue(playerId, out var actor) ? actor.Profile.PlayerName : playerId;

    public LiarsDiceMatchResponse ToResponse()
    {
        var round = State.Round;
        var hand = round.Hands.TryGetValue(HumanId, out var humanHand) ? humanHand.Select(die => die.Value).ToArray() : [];
        return new(Id, round.Phase == LiarsDiceRoundPhase.Bidding ? "bidding" : "resolved", State.RoundNumber,
            round.CurrentPlayerId, round.CurrentBid is { } bid ? new(bid.Quantity, bid.Face.Value) : null,
            round.CurrentBidderId, round.Hands.Values.Sum(values => values.Length), hand,
            State.AllPlayerIds.Select(player => new LiarsDicePlayerResponse(player, DisplayName(player), State.DiceCounts[player],
                State.DiceCounts[player] > 0, player == HumanId)).ToArray(),
            State.LastOutcome is { } outcome ? new(outcome.ChallengerId, outcome.BidderId, outcome.LoserId,
                outcome.Bid.Quantity, outcome.Bid.Face.Value, outcome.MatchingDice, outcome.IsSpotOn ? "spot-on" : "liar") : null,
            State.Winner, OpponentsThinking, Message)
        {
            ResolvedRounds = (ResolvedRounds ?? []).Select(round => new LiarsDiceRoundHistoryResponse(round.RoundNumber,
                new(round.Outcome.ChallengerId, round.Outcome.BidderId, round.Outcome.LoserId, round.Outcome.Bid.Quantity,
                    round.Outcome.Bid.Face.Value, round.Outcome.MatchingDice, round.Outcome.IsSpotOn ? "spot-on" : "liar"))).ToArray(),
        };
    }
}

public sealed class LiarsDiceAccessException() : InvalidOperationException("That Liar's Dice table is not available.");
