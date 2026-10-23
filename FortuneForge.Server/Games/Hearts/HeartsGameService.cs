using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;

namespace FortuneForge.Server.Games.Hearts;

/// <summary>Owns short-lived, no-credit Hearts tables.</summary>
public sealed class HeartsGameService(
    IHeartsManagedPlayerRoster managedPlayers,
    TimeProvider timeProvider,
    IHeartsOnboardingStore? onboarding = null,
    ILogger<HeartsGameService>? logger = null)
{
    private const PlayerSeat HumanSeat = PlayerSeat.North;
    private static readonly TimeSpan LeaseHeartbeatInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan AbandonedSessionTimeout = TimeSpan.FromMinutes(20);
    // The shared assignment store leases profiles for thirty minutes. Stop using
    // a local cohort before that deadline, even if a worker/heartbeat was delayed.
    private static readonly TimeSpan LeaseSafetyWindow = TimeSpan.FromMinutes(25);
    private static readonly IReadOnlySet<PlayerSeat> ManagedSeats = Enum.GetValues<PlayerSeat>()
        .Where(seat => seat != HumanSeat)
        .ToHashSet();
    private readonly ConcurrentDictionary<Guid, HeartsSession> sessions = new();
    // A fixed stripe pool keeps repeated account visits from growing a permanent gate map.
    private readonly SemaphoreSlim[] trialStarts = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    internal int RetainedSessionCount => sessions.Count;

    public Task<HeartsMatchResponse> StartAsync(
        string userId,
        StartHeartsMatchRequest request,
        CancellationToken cancellationToken = default) => StartAsync(userId, request, false, cancellationToken);

    public async Task<HeartsMatchResponse> StartTrialAsync(string userId, CancellationToken cancellationToken = default)
    {
        var gate = trialStarts[(StringComparer.Ordinal.GetHashCode(userId) & int.MaxValue) % trialStarts.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var previous in sessions.Values.Where(session => session.IsTrial && session.UserId == userId))
            {
                lock (previous.SyncRoot) ExpireIfDue(previous, UtcNow());
                await PersistTrialCompletionAsync(previous, cancellationToken);
                await ReleaseProfilesAsync(previous, cancellationToken, throwOnFailure: true);
                lock (previous.SyncRoot)
                    if (!previous.IsCancelled && !IsFinished(previous)) { previous.LastActivityAtUtc = UtcNow(); return ToResponse(previous); }
            }
            return await StartAsync(userId, new StartHeartsMatchRequest(null, HeartsMatchEngine.DefaultTargetScore, "standard"), true, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task CancelTrialAsync(string userId, Guid matchId, CancellationToken cancellationToken = default)
    {
        var session = Session(userId, matchId);
        lock (session.SyncRoot)
        {
            if (!session.IsTrial) throw new HeartsRuleException("This table is not a learning trial.");
            if (!IsFinished(session))
            {
                session.IsCancelled = true;
                session.ClearScheduledTurn();
                session.Message = "Trial ended.";
            }
            session.ManagedReleasePending = !session.ManagedPlayersReleased;
        }
        await PersistTrialCompletionAsync(session, cancellationToken);
        await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: true);
    }

    public async Task LeaveAsync(string userId, Guid matchId, CancellationToken cancellationToken = default)
    {
        // Removing an already-absent free table is harmless; an existing private
        // session still requires its owner and must not bypass trial onboarding.
        if (!sessions.TryGetValue(matchId, out var session)) return;
        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal)) throw new HeartsAccessException();
        lock (session.SyncRoot)
        {
            if (session.IsTrial) throw new HeartsRuleException("Use the learning trial exit to close this table.");
            session.IsCancelled = true;
            session.ClearScheduledTurn();
            session.ManagedReleasePending = !session.ManagedPlayersReleased;
        }
        await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: true);
        sessions.TryRemove(session.Id, out _);
    }

    public async Task RetryTrialCompletionAsync(string userId, CancellationToken cancellationToken = default)
    {
        foreach (var session in sessions.Values.Where(session => session.IsTrial && session.UserId == userId))
            await PersistTrialCompletionAsync(session, cancellationToken);
    }

    private async Task<HeartsMatchResponse> StartAsync(
        string userId, StartHeartsMatchRequest request, bool isTrial, CancellationToken cancellationToken)
    {
        var targetScore = request.TargetScore ?? HeartsMatchEngine.DefaultTargetScore;
        var seed = request.Seed ?? NextSeed();
        var requestedSkillLevel = ParseDifficulty(request.Difficulty);
        var initialState = HeartsMatchEngine.Start(seed, targetScore);
        var matchId = Guid.NewGuid();
        var assignmentId = AssignmentId(matchId);
        var reservedAtUtc = UtcNow();
        var reserved = await managedPlayers.ReserveAsync(
            assignmentId, ManagedSeats.Count, [userId], reservedAtUtc, cancellationToken);
        if (reserved.Count != ManagedSeats.Count)
        {
            await managedPlayers.ReleaseAsync(
                assignmentId,
                reserved.Select(profile => profile.UserId).ToArray(),
                UtcNow(),
                cancellationToken);
            throw new ManagedPlayerSupplyUnavailableException();
        }

        var roster = ManagedSeats.OrderBy(seat => seat)
            .Zip(reserved, (seat, profile) => new KeyValuePair<PlayerSeat, ManagedActor>(
                seat, new ManagedActor(profile)))
            .ToDictionary();
        var session = new HeartsSession(
            matchId, userId, initialState, requestedSkillLevel, roster,
            reservedAtUtc, isTrial);
        if (!sessions.TryAdd(session.Id, session))
        {
            await managedPlayers.ReleaseAsync(
                assignmentId,
                reserved.Select(profile => profile.UserId).ToArray(),
                UtcNow(),
                cancellationToken);
            throw new InvalidOperationException("Could not open a Hearts table.");
        }
        return ToResponse(session);
    }

    public async Task<HeartsMatchResponse> GetAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var session = Session(userId, matchId);
        HeartsMatchResponse response;
        bool heartbeatDue;
        lock (session.SyncRoot)
        {
            if (session.IsExpired) throw new HeartsAccessException();
            response = ToResponse(session);
            heartbeatDue = session.TryClaimLeaseHeartbeat(
                UtcNow(), LeaseHeartbeatInterval);
        }
        await PersistTrialCompletionAsync(session, cancellationToken);
        await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: false);
        if (heartbeatDue)
            await HeartbeatAsync(session, matchId, cancellationToken);
        return response;
    }

    public Task<HeartsMatchResponse> PassAsync(
        string userId,
        Guid matchId,
        IReadOnlyList<string> cards,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
        {
            var parsed = cards.Select(CardCode.Parse).ToArray();
            ApplyHumanAction(session, new PassHeartsCards(HumanSeat, parsed));
        }, cancellationToken);

    public Task<HeartsMatchResponse> PlayCardAsync(
        string userId,
        Guid matchId,
        string card,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
            ApplyHumanAction(session, new PlayHeartsCard(HumanSeat, CardCode.Parse(card))), cancellationToken);

    public Task<HeartsMatchResponse> AdvanceAsync(
        string userId,
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, AdvanceManagedTurn, cancellationToken);

    internal async Task AdvanceDueManagedTurnsAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var session in sessions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.SyncRoot) ExpireIfDue(session, UtcNow());
            await PersistTrialCompletionAsync(session, cancellationToken);
            await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: false);
            lock (session.SyncRoot)
            {
                if (session.IsExpired && session.ManagedPlayersReleased && !session.TrialCompletionPending)
                {
                    sessions.TryRemove(session.Id, out _);
                    continue;
                }
            }
            bool heartbeatDue;
            lock (session.SyncRoot)
            {
                if (session.IsCancelled || !session.OpponentsThinking ||
                    session.NextManagedActionAtUtc is not { } actionAt ||
                    UtcNow() < actionAt)
                    continue;

                AdvanceManagedTurn(session);
                if (IsFinished(session) && !session.ManagedPlayersReleased)
                    session.ManagedReleasePending = true;
                heartbeatDue = session.TryClaimLeaseHeartbeat(
                    UtcNow(), LeaseHeartbeatInterval);
            }

            await PersistTrialCompletionAsync(session, cancellationToken);
            await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: false);
            if (heartbeatDue)
            {
                await HeartbeatAsync(session, session.Id, cancellationToken);
            }
        }
    }

    public Task<HeartsMatchResponse> NextRoundAsync(
        string userId,
        Guid matchId,
        uint? seed,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, matchId, session =>
        {
            if (session.IsTrial) throw new HeartsRuleException("The trial has one hand. Start a free game or join a competitive table to keep playing.");
            session.State = HeartsMatchEngine.StartNextRound(session.State, seed ?? NextSeed());
            session.LastCompletedTrick = null;
            session.Message = $"Round {session.State.RoundNumber} is ready. Pass three cards {DirectionName(session.State.PassDirection)}.";
            session.ClearScheduledTurn();
            PrepareManagedTurn(session);
        }, cancellationToken);

    private async Task<HeartsMatchResponse> ChangeAsync(
        string userId,
        Guid matchId,
        Action<HeartsSession> action,
        CancellationToken cancellationToken)
    {
        var session = Session(userId, matchId);
        HeartsMatchResponse response;
        bool heartbeatDue;
        lock (session.SyncRoot)
        {
            if (session.IsExpired) throw new HeartsAccessException();
            if (session.IsCancelled) throw new HeartsRuleException(session.IsTrial ? "This trial has ended. Choose Learn to play to start another." : "This free table has ended. Start another free game to play.");
            action(session);
            if (IsFinished(session) && !session.ManagedPlayersReleased)
                session.ManagedReleasePending = true;
            heartbeatDue = session.TryClaimLeaseHeartbeat(
                UtcNow(), LeaseHeartbeatInterval);
            response = ToResponse(session);
        }
        await PersistTrialCompletionAsync(session, cancellationToken);
        await ReleaseProfilesAsync(session, cancellationToken, throwOnFailure: false);
        if (heartbeatDue)
        {
            await HeartbeatAsync(session, matchId, cancellationToken);
        }
        return response;
    }

    private async Task PersistTrialCompletionAsync(HeartsSession session, CancellationToken cancellationToken)
    {
        if (onboarding is null) return;
        lock (session.SyncRoot)
            if (!session.TrialCompletionPending) return;
        try
        {
            await onboarding.CompleteTrialAsync(session.UserId, session.Id, UtcNow(), cancellationToken);
            lock (session.SyncRoot) session.TrialCompletionPending = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Finishing the cards remains successful. A later read or worker
            // sweep retries the monotonic account update without replaying cards.
            logger?.LogWarning(exception, "Hearts trial {MatchId} will retry recording account onboarding.", session.Id);
        }
    }

    private async Task ReleaseProfilesAsync(HeartsSession session, CancellationToken cancellationToken, bool throwOnFailure)
    {
        lock (session.SyncRoot)
            if (!session.ManagedReleasePending || session.ManagedPlayersReleased) return;
        await session.LeaseGate.WaitAsync(cancellationToken);
        try
        {
            lock (session.SyncRoot)
                if (!session.ManagedReleasePending || session.ManagedPlayersReleased) return;
            await managedPlayers.ReleaseAsync(AssignmentId(session.Id),
                session.ManagedActors.Values.Select(actor => actor.Profile.UserId).ToArray(), UtcNow(), cancellationToken);
            lock (session.SyncRoot)
            {
                session.ManagedPlayersReleased = true;
                session.ManagedReleasePending = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Hearts table {MatchId} will retry releasing opponent profiles.", session.Id);
            if (throwOnFailure) throw;
        }
        finally { session.LeaseGate.Release(); }
    }

    private static bool IsFinished(HeartsSession session) => session.State.Winner is not null ||
        (session.IsTrial && session.State.Round.Phase == HeartsPhase.Complete);

    private async Task HeartbeatAsync(
        HeartsSession session,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        await session.LeaseGate.WaitAsync(cancellationToken);
        try
        {
            var now = UtcNow();
            lock (session.SyncRoot)
            {
                ExpireIfDue(session, now);
                if (session.ManagedPlayersReleased || session.ManagedReleasePending || session.IsCancelled) return;
            }
            await managedPlayers.HeartbeatAsync(
                AssignmentId(matchId),
                session.ManagedActors.Values.Select(actor => actor.Profile.UserId).ToArray(),
                now,
                cancellationToken);
            lock (session.SyncRoot) session.LeaseSafeUntilUtc = now + LeaseSafetyWindow;
        }
        catch
        {
            lock (session.SyncRoot)
                session.RetryLeaseHeartbeat(UtcNow());
            throw;
        }
        finally { session.LeaseGate.Release(); }
    }

    private HeartsSession Session(string userId, Guid matchId)
    {
        if (!sessions.TryGetValue(matchId, out var session) ||
            !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new HeartsAccessException();
        lock (session.SyncRoot)
        {
            ExpireIfDue(session, UtcNow());
            if (session.IsExpired) throw new HeartsAccessException();
            session.LastActivityAtUtc = UtcNow();
        }
        return session;
    }

    private static void ExpireIfDue(HeartsSession session, DateTime nowUtc)
    {
        if (session.IsExpired || nowUtc < session.LastActivityAtUtc + AbandonedSessionTimeout &&
            (session.ManagedPlayersReleased || nowUtc < session.LeaseSafeUntilUtc)) return;
        session.IsExpired = true;
        session.IsCancelled = true;
        session.ClearScheduledTurn();
        session.ManagedReleasePending = !session.ManagedPlayersReleased;
        session.Message = "This Hearts table expired after inactivity. Start another game to play.";
    }

    private void ApplyHumanAction(HeartsSession session, HeartsCommand command)
    {
        if (session.State.Round.Phase == HeartsPhase.Playing && session.State.Round.Turn != HumanSeat)
            throw new HeartsRuleException($"It is {DisplayName(session, session.State.Round.Turn)}'s turn.");
        var completedBefore = session.State.Round.CompletedTricks.Count;
        var transition = HeartsMatchEngine.Apply(session.State, command);
        session.State = transition.State;
        RememberCompletedTrick(session, completedBefore);
        session.Message = transition.Message;
        session.ClearScheduledTurn();
        PrepareManagedTurn(session);
    }

    private void AdvanceManagedTurn(HeartsSession session)
    {
        if (!session.OpponentsThinking ||
            session.NextManagedActionAtUtc is not { } actionAt ||
            UtcNow() < actionAt)
            return;

        var round = session.State.Round;
        HeartsCommand command;
        if (round.Phase == HeartsPhase.Passing)
        {
            if (!round.SubmittedPasses.Any(pass => pass.Seat == HumanSeat))
            {
                session.ClearScheduledTurn();
                return;
            }
            var seat = NextPassingManagedSeat(round);
            var actor = session.ManagedActors[seat];
            command = new PassHeartsCards(
                seat,
                HeartsManagedActionPolicy.ChoosePass(
                    round, seat, actor.Profile.SkillLevel,
                    actor.DecisionSeed, actor.NextDecisionVersion()));
        }
        else if (round.Phase == HeartsPhase.Playing && ManagedSeats.Contains(round.Turn))
        {
            var seat = round.Turn;
            var actor = session.ManagedActors[seat];
            command = new PlayHeartsCard(
                seat,
                HeartsManagedActionPolicy.ChooseCard(
                    round, seat, actor.Profile.SkillLevel,
                    actor.DecisionSeed, actor.NextDecisionVersion()));
        }
        else
        {
            session.ClearScheduledTurn();
            return;
        }

        var completedBefore = round.CompletedTricks.Count;
        var transition = HeartsMatchEngine.Apply(session.State, command);
        session.State = transition.State;
        RememberCompletedTrick(session, completedBefore);
        session.Message = transition.Message;
        session.ClearScheduledTurn();
        PrepareManagedTurn(session);
    }

    private void PrepareManagedTurn(HeartsSession session)
    {
        var round = session.State.Round;
        if (session.State.Winner is not null || round.Phase == HeartsPhase.Complete)
        {
            session.ClearScheduledTurn();
            return;
        }

        PlayerSeat? seat = round.Phase switch
        {
            HeartsPhase.Passing when round.SubmittedPasses.Any(pass => pass.Seat == HumanSeat) &&
                                     round.SubmittedPasses.Count < TrickTakingRules.PlayerCount =>
                NextPassingManagedSeat(round),
            HeartsPhase.Playing when ManagedSeats.Contains(round.Turn) => round.Turn,
            _ => null,
        };
        if (seat is null)
        {
            session.ClearScheduledTurn();
            return;
        }

        if (session.ScheduledManagedSeat != seat)
        {
            var actor = session.ManagedActors[seat.Value];
            session.ScheduleTurn(seat.Value, UtcNow() + actor.NextThinkDelay());
        }
    }

    private static PlayerSeat NextPassingManagedSeat(HeartsState round) =>
        Enum.GetValues<PlayerSeat>()
            .First(seat => ManagedSeats.Contains(seat) &&
                           !round.SubmittedPasses.Any(pass => pass.Seat == seat));

    private static void RememberCompletedTrick(HeartsSession session, int completedBefore)
    {
        if (session.State.Round.CompletedTricks.Count > completedBefore)
            session.LastCompletedTrick = session.State.Round.CompletedTricks[^1];
        if (session.IsTrial && session.State.Round.Phase == HeartsPhase.Complete &&
            session.State.Round.CompletedTricks.Count == 13)
            session.TrialCompletionPending = true;
    }

    private static HeartsMatchResponse ToResponse(HeartsSession session)
    {
        var state = session.State;
        var round = state.Round;
        var scores = round.Scores.ToDictionary(score => score.Seat, score => score.Points);
        var legal = round.Phase == HeartsPhase.Playing && round.Turn == HumanSeat
            ? HeartsEngine.LegalCards(round, HumanSeat)
            : [];
        return new HeartsMatchResponse(
            session.Id, state.TargetScore, state.RoundNumber, PhaseName(round.Phase),
            DirectionName(state.PassDirection), SeatName(round.Turn), SeatName(HumanSeat),
            !session.IsCancelled && round.Turn == HumanSeat && state.Winner is null, session.OpponentsThinking, round.HeartsBroken,
            round.SubmittedPasses.Select(pass => SeatName(pass.Seat)).ToArray(),
            Enum.GetValues<PlayerSeat>().Select(seat => new HeartsPlayerResponse(
                SeatName(seat), DisplayName(session, seat), round.HandFor(seat).Count,
                scores[seat], state.Score.For(seat),
                round.SubmittedPasses.Any(pass => pass.Seat == seat))).ToArray(),
            round.HandFor(HumanSeat).Select(ToCard).ToArray(), legal.Select(ToCard).ToArray(),
            new HeartsTrickResponse(SeatName(round.CurrentTrick.Leader), round.CurrentTrick.Plays.Select(play =>
                new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray()),
            session.LastCompletedTrick is { } recent ? new HeartsRecentTrickResponse(
                recent.Number, SeatName(recent.Leader), SeatName(recent.Winner),
                recent.Plays.Sum(play => HeartsRules.PointValue(play.Card)), recent.Plays.Select(play =>
                    new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray()) : null,
            round.CompletedTricks.Select(trick => new HeartsCompletedTrickResponse(
                trick.Number, SeatName(trick.Leader), SeatName(trick.Winner),
                trick.Plays.Sum(play => HeartsRules.PointValue(play.Card)))).ToArray(),
            new HeartsScoreResponse(state.Score.North, state.Score.East, state.Score.South, state.Score.West),
            new HeartsScoreResponse(scores[PlayerSeat.North], scores[PlayerSeat.East], scores[PlayerSeat.South], scores[PlayerSeat.West]),
            DifficultyName(session.RequestedSkillLevel), state.Winner is { } winner ? SeatName(winner) : null, session.Message, session.IsTrial);
    }

    private static string DisplayName(HeartsSession session, PlayerSeat seat) =>
        seat == HumanSeat ? "You" : session.IsTrial ? $"Trial bot {(int)seat}" : session.ManagedActors[seat].Profile.PlayerName;
    private static HeartsCardResponse ToCard(PlayingCard card) => new(card.Code, RankName(card.Rank), SuitName(card.Suit), CardLabel(card));
    private static uint NextSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private static int ParseDifficulty(string? difficulty) => difficulty?.Trim().ToLowerInvariant() switch
    {
        null or "" or "standard" => ManagedPlayerSkillLevels.Average,
        "relaxed" => ManagedPlayerSkillLevels.Poor,
        "sharp" => ManagedPlayerSkillLevels.Strong,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), "Hearts difficulty must be relaxed, standard, or sharp."),
    };
    private static string DifficultyName(int skillLevel) => skillLevel switch
    {
        ManagedPlayerSkillLevels.Poor => "relaxed",
        ManagedPlayerSkillLevels.Average => "standard",
        ManagedPlayerSkillLevels.Strong => "sharp",
        _ => throw new ArgumentOutOfRangeException(nameof(skillLevel)),
    };
    private static string PhaseName(HeartsPhase phase) => phase switch { HeartsPhase.Passing => "passing", HeartsPhase.Playing => "playing", HeartsPhase.Complete => "complete", _ => throw new ArgumentOutOfRangeException(nameof(phase)) };
    private static string DirectionName(HeartsPassDirection value) => value switch { HeartsPassDirection.Left => "left", HeartsPassDirection.Right => "right", HeartsPassDirection.Across => "across", HeartsPassDirection.Hold => "hold", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string SeatName(PlayerSeat seat) => seat.ToString().ToLowerInvariant();
    private static string RankName(CardRank rank) => rank switch { CardRank.Ace => "ace", CardRank.Jack => "jack", CardRank.Queen => "queen", CardRank.King => "king", _ => ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture) };
    private static string SuitName(CardSuit suit) => suit.ToString().ToLowerInvariant();
    private static string CardLabel(PlayingCard card) => $"{card.Code.Split('|')[0]}{card.Suit switch { CardSuit.Clubs => "♣", CardSuit.Diamonds => "♦", CardSuit.Hearts => "♥", CardSuit.Spades => "♠", _ => "?" }}";
    private static string AssignmentId(Guid matchId) => $"hearts:{matchId:N}";
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class ManagedActor(HeartsManagedPlayer profile)
    {
        private int decisionVersion;
        private int timingVersion;
        public HeartsManagedPlayer Profile { get; } = profile;
        public ulong DecisionSeed { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        private ulong TimingSeed { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        public int NextDecisionVersion() => decisionVersion++;
        public TimeSpan NextThinkDelay()
        {
            var random = new DeterministicManagedPlayerRandom(
                TimingSeed,
                $"hearts-think:{Profile.UserId}:{timingVersion++}");
            return TimeSpan.FromMilliseconds(300 + random.Next(651));
        }
    }

    private sealed class HeartsSession(
        Guid id,
        string userId,
        HeartsMatchState state,
        int requestedSkillLevel,
        IReadOnlyDictionary<PlayerSeat, ManagedActor> managedActors,
        DateTime createdAtUtc,
        bool isTrial)
    {
        private DateTime nextLeaseHeartbeatAtUtc = createdAtUtc + LeaseHeartbeatInterval;
        public Guid Id { get; } = id;
        public bool IsTrial { get; } = isTrial;
        public bool IsCancelled { get; set; }
        public bool IsExpired { get; set; }
        public bool TrialCompletionPending { get; set; }
        public bool ManagedReleasePending { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim LeaseGate { get; } = new(1, 1);
        public DateTime LastActivityAtUtc { get; set; } = createdAtUtc;
        public DateTime LeaseSafeUntilUtc { get; set; } = createdAtUtc + LeaseSafetyWindow;
        public string UserId { get; } = userId;
        public HeartsMatchState State { get; set; } = state;
        public int RequestedSkillLevel { get; } = requestedSkillLevel;
        public IReadOnlyDictionary<PlayerSeat, ManagedActor> ManagedActors { get; } = managedActors;
        public bool OpponentsThinking => ScheduledManagedSeat is not null;
        public PlayerSeat? ScheduledManagedSeat { get; private set; }
        public DateTime? NextManagedActionAtUtc { get; private set; }
        public bool ManagedPlayersReleased { get; set; }
        public CompletedTrick? LastCompletedTrick { get; set; }
        public string Message { get; set; } = "Pass three cards to the left.";

        public bool TryClaimLeaseHeartbeat(DateTime nowUtc, TimeSpan interval)
        {
            if (ManagedPlayersReleased || ManagedReleasePending || IsCancelled || nowUtc < nextLeaseHeartbeatAtUtc)
                return false;
            nextLeaseHeartbeatAtUtc = nowUtc + interval;
            return true;
        }

        public void RetryLeaseHeartbeat(DateTime nowUtc)
        {
            if (!ManagedPlayersReleased)
                nextLeaseHeartbeatAtUtc = nowUtc;
        }

        public void ScheduleTurn(PlayerSeat seat, DateTime atUtc)
        {
            ScheduledManagedSeat = seat;
            NextManagedActionAtUtc = atUtc;
        }

        public void ClearScheduledTurn()
        {
            ScheduledManagedSeat = null;
            NextManagedActionAtUtc = null;
        }
    }

}

public sealed class HeartsAccessException() : InvalidOperationException("That Hearts table is not available.");
