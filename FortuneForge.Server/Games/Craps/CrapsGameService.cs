using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.SecurityAudit;

namespace FortuneForge.Server.Games.Craps;

/// <summary>Owns account-isolated, no-credit Craps Pass Line rounds.</summary>
public sealed class CrapsGameService(IGameAnalyticsSink? analytics = null, IGameSecurityEventSink? securityEvents = null)
{
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private readonly ConcurrentDictionary<Guid, CrapsSession> rounds = new();

    public CrapsStatusResponse Status() => new(true, MinimumStake, MaximumStake, MinimumStake, "free-play-pass-line");

    public CrapsRoundResponse Start(string userId, StartCrapsRoundRequest request)
    {
        if (request.Stake is < MinimumStake or > MaximumStake || request.Stake % MinimumStake != 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose a Pass Line wager from R1 through R100.");
        var extras = (request.ExtraBets ?? []).Select(CreateExtra).ToList();
        var session = new CrapsSession(userId, CrapsEngine.StartPassLine(new CrapsPassLineBet(userId, request.Stake)), extras);
        if (!rounds.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not open a Craps table.");
        return ToResponse(session);
    }

    public async Task<CrapsRoundResponse> StartAuditedAsync(string userId, StartCrapsRoundRequest request, CancellationToken cancellationToken = default)
    {
        var response = Start(userId, request);
        try { await RecordAsync(rounds[response.RoundId], "started", cancellationToken); }
        catch { rounds.TryRemove(response.RoundId, out _); throw; }
        return response;
    }

    public Task<CrapsRoundResponse> RollAuditedAsync(string userId, Guid roundId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, roundId, () => Roll(userId, roundId), "rolled", cancellationToken);

    public Task<CrapsRoundResponse> PlaceOddsAuditedAsync(string userId, Guid roundId, decimal stake, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, roundId, () => PlaceOdds(userId, roundId, stake), "odds-placed", cancellationToken);

    public CrapsRoundResponse Get(string userId, Guid roundId) => Read(userId, roundId, ToResponse);

    public CrapsRoundResponse Roll(string userId, Guid roundId) => Change(userId, roundId, session =>
    {
        var dice = new DicePair(new DieValue(RandomNumberGenerator.GetInt32(1, 7)), new DieValue(RandomNumberGenerator.GetInt32(1, 7)));
        session.State = CrapsEngine.Roll(session.State, dice).State;
        SettleExtras(session, dice.Total);
    });

    public CrapsRoundResponse PlaceOdds(string userId, Guid roundId, decimal stake) => Change(userId, roundId, session =>
    {
        if (session.State.Phase is not CrapsPassLinePhase.Point || session.State.Point is null)
            throw new CrapsRuleException("Pass Line odds are available only after a point is established.");
        if (stake is < MinimumStake or > MaximumStake || stake % MinimumStake != 0m)
            throw new ArgumentOutOfRangeException(nameof(stake), "Choose odds from R1 through R100.");
        if (session.ExtraBets.Any(bet => bet.Kind == "odds" && !bet.Resolved))
            throw new CrapsRuleException("Odds are already placed for this point.");
        session.ExtraBets.Add(new CrapsExtraBet("odds", stake));
    });

    private CrapsRoundResponse Change(string userId, Guid roundId, Action<CrapsSession> action) =>
        Read(userId, roundId, session => { action(session); session.EventVersion++; return ToResponse(session); });

    private async Task<CrapsRoundResponse> ChangeAuditedAsync(string userId, Guid roundId,
        Func<CrapsRoundResponse> change, string eventKind, CancellationToken cancellationToken)
    {
        if (!rounds.TryGetValue(roundId, out var session) || session.UserId != userId)
            throw new CrapsAccessException();
        await session.AuditGate.WaitAsync(cancellationToken);
        try
        {
            var response = change();
            await RecordAsync(session, eventKind, cancellationToken);
            return response;
        }
        finally { session.AuditGate.Release(); }
    }

    private Task RecordAsync(CrapsSession session, string eventKind, CancellationToken cancellationToken)
    {
        object payload;
        long version;
        RoundAuditCapture? completion;
        lock (session.SyncRoot)
        {
            version = session.EventVersion;
            payload = new { session.Id, session.StartedAtUtc, session.CompletedAtUtc, session.State,
                extraBets = session.ExtraBets.Select(bet => new { bet.Kind, bet.Stake, bet.Resolved,
                    bet.Won, bet.TotalReturn }).ToArray() };
            completion = session.State.Phase == CrapsPassLinePhase.Resolved
                ? GameSecurityEventSink.FreeCompletion("craps", session.Id.ToString("N"), session.Id.ToString("N"),
                    1, session.StartedAtUtc, session.CompletedAtUtc ?? DateTime.UtcNow, payload,
                    session.UserId, "craps-private-dice-and-bets-v1",
                    PracticeOutcomeAudit.Check(session.State, session.ExtraBets.Select(bet => new PracticeCrapsExtraBet(
                        bet.Kind, bet.Stake, bet.Resolved, bet.Won, bet.TotalReturn)).ToArray()),
                    PracticeOutcomeAudit.CrapsVersion)
                : null;
        }
        return GameSecurityEventSink.WriteAsync(securityEvents, "craps", session.Id.ToString("N"),
            version, DateTime.UtcNow, payload, [session.UserId], eventKind,
            completion: completion, cancellationToken: cancellationToken);
    }

    private T Read<T>(string userId, Guid roundId, Func<CrapsSession, T> action)
    {
        if (!rounds.TryGetValue(roundId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new CrapsAccessException();
        lock (session.SyncRoot)
        {
            var result = action(session);
            if (!session.AnalyticsRecorded && session.State.Phase == CrapsPassLinePhase.Resolved)
            {
                session.CompletedAtUtc ??= DateTime.UtcNow;
                session.AnalyticsRecorded = analytics?.TryEnqueuePractice(new GameAnalyticsSettlement(
                    "craps", session.Id.ToString("N"), session.UserId, "practice", true,
                    session.CompletedAtUtc.Value, session.StartedAtUtc, 0, 0, "in-memory-craps-rounds", session.Id.ToString("N"))) ?? false;
            }
            return result;
        }
    }

    private static CrapsRoundResponse ToResponse(CrapsSession session) => new(
        session.Id, session.State.Bet.Stake, PhaseName(session.State.Phase), session.State.Point,
        session.State.Rolls.Select((dice, index) => new CrapsRollResponse(index + 1, dice.First.Value, dice.Second.Value, dice.Total,
            index == session.State.Rolls.Length - 1 && session.State.LastOutcome is { } outcome ? ResultName(outcome.Result) : null)).ToArray(),
        session.State.LastOutcome is { } last ? new CrapsOutcomeResponse(last.Dice.First.Value, last.Dice.Second.Value, last.Dice.Total,
            ResultName(last.Result), last.IsTerminal, last.TotalReturn) : null,
        session.ExtraBets.Select(bet => new CrapsExtraBetResponse(bet.Kind, bet.Stake, bet.Resolved, bet.Won, bet.TotalReturn)).ToArray());

    private static CrapsExtraBet CreateExtra(CrapsExtraBetRequest request)
    {
        var kind = request.Kind.Trim().ToLowerInvariant();
        if (kind is not ("field" or "any-seven" or "any-craps"))
            throw new CrapsRuleException("The supported one-roll bets are Field, Any Seven, and Any Craps.");
        if (request.Stake is < MinimumStake or > MaximumStake || request.Stake % MinimumStake != 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose each extra bet from R1 through R100.");
        return new CrapsExtraBet(kind, request.Stake);
    }

    private static void SettleExtras(CrapsSession session, int total)
    {
        foreach (var bet in session.ExtraBets.Where(bet => !bet.Resolved))
        {
            if (bet.Kind == "odds" && session.State.Phase is not CrapsPassLinePhase.Resolved) continue;
            bet.Resolved = true;
            bet.TotalReturn = bet.Kind switch
            {
                "field" when total is 2 or 12 => bet.Stake * 3m,
                "field" when total is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2m,
                "any-seven" when total == 7 => bet.Stake * 5m,
                "any-craps" when total is 2 or 3 or 12 => bet.Stake * 8m,
                "odds" when session.State.LastOutcome?.Result is CrapsRollResult.PointHit => OddsReturn(bet.Stake, session.State.Point!.Value),
                _ => 0m,
            };
            bet.Won = bet.TotalReturn > 0m;
        }
    }

    private static decimal OddsReturn(decimal stake, int point) => point switch
    {
        4 or 10 => stake * 3m,
        5 or 9 => stake * 2.5m,
        6 or 8 => stake * 2.2m,
        _ => throw new CrapsRuleException("The point does not support odds."),
    };

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

    private sealed class CrapsSession(string userId, CrapsPassLineState state, List<CrapsExtraBet> extraBets)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public bool AnalyticsRecorded { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim AuditGate { get; } = new(1, 1);
        public long EventVersion { get; set; }
        public string UserId { get; } = userId;
        public CrapsPassLineState State { get; set; } = state;
        public List<CrapsExtraBet> ExtraBets { get; } = extraBets;
    }

    private sealed class CrapsExtraBet(string kind, decimal stake)
    {
        public string Kind { get; } = kind;
        public decimal Stake { get; } = stake;
        public bool Resolved { get; set; }
        public bool Won { get; set; }
        public decimal? TotalReturn { get; set; }
    }
}

public sealed class CrapsAccessException() : InvalidOperationException("That Craps table is not available.");
