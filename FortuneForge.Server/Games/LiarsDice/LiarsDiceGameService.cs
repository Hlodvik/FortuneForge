using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.LiarsDice;

namespace FortuneForge.Server.Games.LiarsDice;

/// <summary>Owns account-isolated Liar's Dice matches.</summary>
public sealed class LiarsDiceGameService(
    ILiarsDiceManagedPlayerRoster managedPlayers,
    TimeProvider timeProvider)
{
    private const string HumanId = "you";
    private const int ManagedPlayerCount = 3;
    private static readonly TimeSpan LeaseHeartbeatInterval = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, LiarsDiceSession> matches = new();

    public LiarsDiceStatusResponse Status() =>
        new(true, LiarsDiceMatchEngine.DefaultDicePerPlayer, "free-play");

    public async Task<LiarsDiceMatchResponse> StartAsync(
        string userId,
        StartLiarsDiceMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var dicePerPlayer = request.DicePerPlayer ?? LiarsDiceMatchEngine.DefaultDicePerPlayer;
        if (dicePerPlayer <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(request), "Each Liar's Dice player needs at least one die.");
        var seed = request.Seed ?? NextSeed();
        var matchId = Guid.NewGuid();
        var assignmentId = AssignmentId(matchId);
        var reserved = await managedPlayers.ReserveAsync(
            assignmentId, ManagedPlayerCount, [userId], UtcNow(), cancellationToken);
        if (reserved.Count != ManagedPlayerCount)
        {
            await managedPlayers.ReleaseAsync(
                assignmentId,
                reserved.Select(profile => profile.UserId).ToArray(),
                UtcNow(),
                cancellationToken);
            throw new InvalidOperationException("A complete Liar's Dice table could not be reserved.");
        }

        var roster = reserved.ToDictionary(
            profile => profile.UserId,
            profile => new ManagedActor(profile),
            StringComparer.Ordinal);
        var playerIds = new[] { HumanId }.Concat(roster.Keys).ToArray();
        var session = new LiarsDiceSession(
            matchId,
            userId,
            LiarsDiceMatchEngine.Start(seed, playerIds, dicePerPlayer),
            roster,
            UtcNow() + LeaseHeartbeatInterval);
        PrepareManagedTurn(session);
        if (!matches.TryAdd(session.Id, session))
        {
            await managedPlayers.ReleaseAsync(
                assignmentId,
                reserved.Select(profile => profile.UserId).ToArray(),
                UtcNow(),
                cancellationToken);
            throw new InvalidOperationException("Could not open a Liar's Dice table.");
        }
        return ToResponse(session);
    }

    public async Task<LiarsDiceMatchResponse> GetAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var session = Session(userId, matchId);
        LiarsDiceMatchResponse response;
        bool heartbeatDue;
        lock (session.SyncRoot)
        {
            response = ToResponse(session);
            heartbeatDue = session.TryClaimLeaseHeartbeat(
                UtcNow(), LeaseHeartbeatInterval);
        }
        if (heartbeatDue)
            await HeartbeatAsync(session, matchId, cancellationToken);
        return response;
    }

    public Task<LiarsDiceMatchResponse> BidAsync(
        string userId,
        Guid matchId,
        PlaceLiarsDiceBidRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session => ApplyHumanAction(
            session,
            new PlaceLiarsDiceBid(HumanId, new LiarsDiceBid(request.Quantity, new DieValue(request.Face)))),
            cancellationToken);

    public Task<LiarsDiceMatchResponse> ChallengeAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
            ApplyHumanAction(session, new ChallengeLiarsDiceBid(HumanId)), cancellationToken);

    public Task<LiarsDiceMatchResponse> SpotOnAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
            ApplyHumanAction(session, new SpotOnLiarsDiceBid(HumanId)), cancellationToken);

    public Task<LiarsDiceMatchResponse> AdvanceAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, AdvanceManagedTurn, cancellationToken);

    internal async Task AdvanceDueManagedTurnsAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var session in matches.Values)
        {
            string[] releaseIds = [];
            bool heartbeatDue;
            lock (session.SyncRoot)
            {
                if (!session.OpponentsThinking ||
                    session.NextManagedActionAtUtc is not { } actionAt ||
                    UtcNow() < actionAt)
                    continue;

                AdvanceManagedTurn(session);
                if (session.State.Winner is not null && !session.ManagedPlayersReleased)
                {
                    session.ManagedPlayersReleased = true;
                    releaseIds = session.ManagedActors.Keys.ToArray();
                }
                heartbeatDue = releaseIds.Length == 0 && session.TryClaimLeaseHeartbeat(
                    UtcNow(), LeaseHeartbeatInterval);
            }

            if (releaseIds.Length > 0)
            {
                await managedPlayers.ReleaseAsync(
                    AssignmentId(session.Id), releaseIds, UtcNow(), cancellationToken);
            }
            else if (heartbeatDue)
            {
                await HeartbeatAsync(session, session.Id, cancellationToken);
            }
        }
    }

    public Task<LiarsDiceMatchResponse> NextRoundAsync(
        string userId,
        Guid matchId,
        uint? seed,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
        {
            session.State = LiarsDiceMatchEngine.StartNextRound(session.State, seed ?? NextSeed());
            session.Message = $"Round {session.State.RoundNumber} begins. {DisplayName(session, session.State.CurrentPlayerId)} acts first.";
            session.ClearScheduledTurn();
            PrepareManagedTurn(session);
        }, cancellationToken);

    private async Task<LiarsDiceMatchResponse> ChangeAsync(
        string userId,
        Guid matchId,
        Action<LiarsDiceSession> action,
        CancellationToken cancellationToken)
    {
        var session = Session(userId, matchId);
        LiarsDiceMatchResponse response;
        string[] releaseIds = [];
        bool heartbeatDue;
        lock (session.SyncRoot)
        {
            action(session);
            if (session.State.Winner is not null && !session.ManagedPlayersReleased)
            {
                session.ManagedPlayersReleased = true;
                releaseIds = session.ManagedActors.Keys.ToArray();
            }
            heartbeatDue = releaseIds.Length == 0 && session.TryClaimLeaseHeartbeat(
                UtcNow(), LeaseHeartbeatInterval);
            response = ToResponse(session);
        }
        if (releaseIds.Length > 0)
        {
            await managedPlayers.ReleaseAsync(
                AssignmentId(matchId), releaseIds, UtcNow(), cancellationToken);
        }
        else if (heartbeatDue)
        {
            await HeartbeatAsync(session, matchId, cancellationToken);
        }
        return response;
    }

    private async Task HeartbeatAsync(
        LiarsDiceSession session,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        try
        {
            await managedPlayers.HeartbeatAsync(
                AssignmentId(matchId),
                session.ManagedActors.Keys.ToArray(),
                UtcNow(),
                cancellationToken);
        }
        catch
        {
            lock (session.SyncRoot)
                session.RetryLeaseHeartbeat(UtcNow());
            throw;
        }
    }

    private LiarsDiceSession Session(string userId, Guid matchId)
    {
        if (!matches.TryGetValue(matchId, out var session) ||
            !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new LiarsDiceAccessException();
        return session;
    }

    private void ApplyHumanAction(LiarsDiceSession session, LiarsDiceCommand command)
    {
        if (!string.Equals(session.State.CurrentPlayerId, HumanId, StringComparison.Ordinal))
            throw new LiarsDiceRuleException($"It is {DisplayName(session, session.State.CurrentPlayerId)}'s turn.");
        var transition = LiarsDiceMatchEngine.Apply(session.State, command);
        session.State = transition.State;
        session.Message = PlayerMessage(session, transition);
        session.ClearScheduledTurn();
        PrepareManagedTurn(session);
    }

    private void AdvanceManagedTurn(LiarsDiceSession session)
    {
        if (!session.OpponentsThinking ||
            session.NextManagedActionAtUtc is not { } actionAt ||
            UtcNow() < actionAt)
            return;
        if (session.State.Winner is not null ||
            session.State.Round.Phase == LiarsDiceRoundPhase.Resolved ||
            !session.ManagedActors.TryGetValue(session.State.CurrentPlayerId, out var actor))
        {
            session.ClearScheduledTurn();
            return;
        }

        var transition = LiarsDiceMatchEngine.Apply(
            session.State,
            LiarsDiceManagedActionPolicy.Choose(
                session.State,
                actor.Profile.SkillLevel,
                actor.DecisionSeed,
                actor.NextDecisionVersion()));
        session.State = transition.State;
        session.Message = string.Empty;
        session.ClearScheduledTurn();
        PrepareManagedTurn(session);
    }

    private void PrepareManagedTurn(LiarsDiceSession session)
    {
        if (session.State.Winner is not null ||
            session.State.Round.Phase == LiarsDiceRoundPhase.Resolved ||
            !session.ManagedActors.TryGetValue(session.State.CurrentPlayerId, out var actor))
        {
            session.ClearScheduledTurn();
            return;
        }

        if (!string.Equals(session.ScheduledManagedPlayerId, actor.Profile.UserId, StringComparison.Ordinal))
            session.ScheduleTurn(actor.Profile.UserId, UtcNow() + actor.NextThinkDelay());
    }

    private static LiarsDiceMatchResponse ToResponse(LiarsDiceSession session)
    {
        var state = session.State;
        var round = state.Round;
        var hand = round.Hands.TryGetValue(HumanId, out var humanHand) ? humanHand : [];
        return new LiarsDiceMatchResponse(
            session.Id, round.Phase == LiarsDiceRoundPhase.Bidding ? "bidding" : "resolved", state.RoundNumber,
            round.CurrentPlayerId, round.CurrentBid is { } bid ? new LiarsDiceBidResponse(bid.Quantity, bid.Face.Value) : null,
            round.CurrentBidderId, round.Hands.Values.Sum(values => values.Length), hand.Select(die => die.Value).ToArray(),
            state.AllPlayerIds.Select(player => new LiarsDicePlayerResponse(
                player, DisplayName(session, player), state.DiceCounts[player],
                state.DiceCounts[player] > 0, player == HumanId)).ToArray(),
            state.LastOutcome is { } outcome ? new LiarsDiceOutcomeResponse(
                outcome.ChallengerId, outcome.BidderId, outcome.LoserId,
                outcome.Bid.Quantity, outcome.Bid.Face.Value, outcome.MatchingDice,
                outcome.IsSpotOn ? "spot-on" : "liar") : null,
            state.Winner, session.OpponentsThinking, session.Message);
    }

    private static uint NextSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private static string PlayerMessage(
        LiarsDiceSession session,
        LiarsDiceMatchTransition transition)
    {
        if (transition.Outcome is null)
            return "Bid placed.";
        if (transition.State.Winner is { } winner)
            return $"{DisplayName(session, winner)} wins the match!";
        return $"{DisplayName(session, transition.Outcome.LoserId)} loses a die.";
    }
    private static string DisplayName(LiarsDiceSession session, string id) =>
        id == HumanId ? "You" : session.ManagedActors.TryGetValue(id, out var actor) ? actor.Profile.PlayerName : id;
    private static string AssignmentId(Guid matchId) => $"liars-dice:{matchId:N}";
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class ManagedActor(LiarsDiceManagedPlayer profile)
    {
        private int decisionVersion;
        private int timingVersion;
        public LiarsDiceManagedPlayer Profile { get; } = profile;
        public ulong DecisionSeed { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        private ulong TimingSeed { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        public int NextDecisionVersion() => decisionVersion++;
        public TimeSpan NextThinkDelay()
        {
            var random = new DeterministicManagedPlayerRandom(
                TimingSeed,
                $"liars-dice-think:{Profile.UserId}:{timingVersion++}");
            return TimeSpan.FromMilliseconds(350 + random.Next(851));
        }
    }

    private sealed class LiarsDiceSession(
        Guid id,
        string userId,
        LiarsDiceMatchState state,
        IReadOnlyDictionary<string, ManagedActor> managedActors,
        DateTime nextLeaseHeartbeatAtUtc)
    {
        private DateTime nextLeaseHeartbeatAtUtc = nextLeaseHeartbeatAtUtc;
        public Guid Id { get; } = id;
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public LiarsDiceMatchState State { get; set; } = state;
        public IReadOnlyDictionary<string, ManagedActor> ManagedActors { get; } = managedActors;
        public bool OpponentsThinking => ScheduledManagedPlayerId is not null;
        public string? ScheduledManagedPlayerId { get; private set; }
        public DateTime? NextManagedActionAtUtc { get; private set; }
        public bool ManagedPlayersReleased { get; set; }
        public string Message { get; set; } = "Place a bid.";

        public bool TryClaimLeaseHeartbeat(DateTime nowUtc, TimeSpan interval)
        {
            if (ManagedPlayersReleased || nowUtc < nextLeaseHeartbeatAtUtc)
                return false;
            nextLeaseHeartbeatAtUtc = nowUtc + interval;
            return true;
        }

        public void RetryLeaseHeartbeat(DateTime nowUtc)
        {
            if (!ManagedPlayersReleased)
                nextLeaseHeartbeatAtUtc = nowUtc;
        }

        public void ScheduleTurn(string profileId, DateTime atUtc)
        {
            ScheduledManagedPlayerId = profileId;
            NextManagedActionAtUtc = atUtc;
        }

        public void ClearScheduledTurn()
        {
            ScheduledManagedPlayerId = null;
            NextManagedActionAtUtc = null;
        }
    }

}

public sealed class LiarsDiceAccessException() : InvalidOperationException("That Liar's Dice table is not available.");
