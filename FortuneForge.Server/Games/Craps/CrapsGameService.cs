using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;

namespace FortuneForge.Server.Games.Craps;

/// <summary>Owns account-isolated, no-credit Craps Pass Line rounds.</summary>
public sealed class CrapsGameService
{
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private readonly ConcurrentDictionary<Guid, CrapsSession> rounds = new();

    public CrapsStatusResponse Status() => new(true, MinimumStake, MaximumStake, MinimumStake, "free-play-pass-line");

    public CrapsRoundResponse Start(string userId, StartCrapsRoundRequest request)
    {
        if (request.Stake is < MinimumStake or > MaximumStake || request.Stake % MinimumStake != 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose a Pass Line wager from R1 through R100.");
        var session = new CrapsSession(userId, CrapsEngine.StartPassLine(new CrapsPassLineBet(userId, request.Stake)));
        if (!rounds.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not open a Craps table.");
        return ToResponse(session);
    }

    public CrapsRoundResponse Get(string userId, Guid roundId) => Read(userId, roundId, ToResponse);

    public CrapsRoundResponse Roll(string userId, Guid roundId) => Change(userId, roundId, session =>
    {
        var dice = new DicePair(new DieValue(RandomNumberGenerator.GetInt32(1, 7)), new DieValue(RandomNumberGenerator.GetInt32(1, 7)));
        session.State = CrapsEngine.Roll(session.State, dice).State;
    });

    private CrapsRoundResponse Change(string userId, Guid roundId, Action<CrapsSession> action) =>
        Read(userId, roundId, session => { action(session); return ToResponse(session); });

    private T Read<T>(string userId, Guid roundId, Func<CrapsSession, T> action)
    {
        if (!rounds.TryGetValue(roundId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new CrapsAccessException();
        lock (session.SyncRoot) return action(session);
    }

    private static CrapsRoundResponse ToResponse(CrapsSession session) => new(
        session.Id, session.State.Bet.Stake, PhaseName(session.State.Phase), session.State.Point,
        session.State.Rolls.Select((dice, index) => new CrapsRollResponse(index + 1, dice.First.Value, dice.Second.Value, dice.Total,
            index == session.State.Rolls.Length - 1 && session.State.LastOutcome is { } outcome ? ResultName(outcome.Result) : null)).ToArray(),
        session.State.LastOutcome is { } last ? new CrapsOutcomeResponse(last.Dice.First.Value, last.Dice.Second.Value, last.Dice.Total,
            ResultName(last.Result), last.IsTerminal, last.TotalReturn) : null);

    private static string PhaseName(CrapsPassLinePhase phase) => phase switch
    {
        CrapsPassLinePhase.ComeOut => "come-out", CrapsPassLinePhase.Point => "point", CrapsPassLinePhase.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static string ResultName(CrapsRollResult result) => result switch
    {
        CrapsRollResult.NaturalWin => "natural-win", CrapsRollResult.CrapsLoss => "craps-loss",
        CrapsRollResult.PointEstablished => "point-established", CrapsRollResult.PointHit => "point-hit",
        CrapsRollResult.SevenOut => "seven-out", CrapsRollResult.NoDecision => "no-decision",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    private sealed class CrapsSession(string userId, CrapsPassLineState state)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public CrapsPassLineState State { get; set; } = state;
    }
}

public sealed class CrapsAccessException() : InvalidOperationException("That Craps table is not available.");
