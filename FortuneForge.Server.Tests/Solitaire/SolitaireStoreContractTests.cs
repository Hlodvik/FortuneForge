using FortuneForge.Server.Cards.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireStoreContractTests
{
    private static readonly DateTime Start = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Queue_IsFifoAndIsolatedByPlayerCountAndBuyIn()
    {
        var store = new InMemoryStore();
        store.Fund("p1", "p2", "p3", "p4", "p5", "p6");

        _ = await Join(store, "p1", 4, 500, "join_p1_00000001", Start);
        _ = await Join(store, "p2", 6, 500, "join_p2_00000001", Start);
        _ = await Join(store, "p3", 4, 1000, "join_p3_00000001", Start);
        _ = await Join(store, "p4", 4, 500, "join_p4_00000001", Start.AddMilliseconds(1));
        _ = await Join(store, "p5", 4, 500, "join_p5_00000001", Start.AddMilliseconds(2));
        var matched = await Join(store, "p6", 4, 500, "join_p6_00000001", Start.AddMilliseconds(3));

        var match = Assert.IsType<SolitaireMatchSessionResponse>(matched.Session);
        Assert.Equal(["p1", "p4", "p5", "p6"], match.Players.Select(player => player.PlayerId));
        Assert.Equal(4, match.PlayerCount);
        Assert.Equal(5m, match.BuyInCredits);
        Assert.IsType<SolitaireQueueSessionResponse>(
            (await store.GetSessionAsync("p2", Start, default)).Session);
        Assert.IsType<SolitaireQueueSessionResponse>(
            (await store.GetSessionAsync("p3", Start, default)).Session);
        Assert.Equal(6, ((SolitaireQueueSessionResponse)(await store.GetSessionAsync("p2", Start, default)).Session).PlayerCount);
        Assert.Equal(10m, ((SolitaireQueueSessionResponse)(await store.GetSessionAsync("p3", Start, default)).Session).BuyInCredits);
        Assert.Equal(6, store.LedgerCount("buyin"));
    }

    [Fact]
    public async Task JoinAndCancel_ReplayDebitAndRefundExactlyOnce()
    {
        var store = new InMemoryStore();
        store.Fund("p1");

        var joined = await Join(store, "p1", 4, 500, "join_replay_00001", Start);
        var replayedJoin = await Join(store, "p1", 4, 500, "join_replay_00001", Start.AddSeconds(1));
        var ticket = Assert.IsType<SolitaireQueueSessionResponse>(joined.Session).TicketId;

        Assert.Equal(((SolitaireQueueSessionResponse)joined.Session).TicketId,
            ((SolitaireQueueSessionResponse)replayedJoin.Session).TicketId);
        Assert.Equal(9_500, store.Balance("p1"));
        Assert.Equal(1, store.LedgerCount("buyin"));

        _ = await store.CancelAsync("p1", ticket, "cancel_replay_001", Start.AddSeconds(2), default);
        _ = await store.CancelAsync("p1", ticket, "cancel_replay_001", Start.AddSeconds(3), default);

        Assert.Equal(10_000, store.Balance("p1"));
        Assert.Equal(1, store.LedgerCount("refund"));
        Assert.IsType<SolitaireIdleSessionResponse>(
            (await store.GetSessionAsync("p1", Start.AddSeconds(3), default)).Session);
    }

    [Fact]
    public async Task ConcurrentJoinReplayAndCancelMatchRace_HaveSingleAtomicWinner()
    {
        var replayStore = new InMemoryStore();
        replayStore.Fund("p1");
        var duplicateJoins = await Task.WhenAll(
            Task.Run(() => Join(replayStore, "p1", 4, 500, "concurrent_join_01", Start)),
            Task.Run(() => Join(replayStore, "p1", 4, 500, "concurrent_join_01", Start)));

        Assert.All(duplicateJoins, response => Assert.IsType<SolitaireQueueSessionResponse>(response.Session));
        Assert.Equal(9_500, replayStore.Balance("p1"));
        Assert.Equal(1, replayStore.LedgerCount("buyin"));

        var raceStore = new InMemoryStore();
        raceStore.Fund("p1", "p2", "p3", "p4");
        var p1 = await Join(raceStore, "p1", 4, 500, "race_join_p1_001", Start);
        _ = await Join(raceStore, "p2", 4, 500, "race_join_p2_001", Start);
        _ = await Join(raceStore, "p3", 4, 500, "race_join_p3_001", Start);
        var ticket = Assert.IsType<SolitaireQueueSessionResponse>(p1.Session).TicketId;

        var cancelTask = Task.Run(async () =>
        {
            try
            {
                await raceStore.CancelAsync(
                    "p1", ticket, "race_cancel_p1_01", Start.AddSeconds(1), default);
                return true;
            }
            catch (SolitaireConflictException)
            {
                return false;
            }
        });
        var matchTask = Task.Run(() => Join(
            raceStore, "p4", 4, 500, "race_join_p4_001", Start.AddSeconds(1)));
        var cancelled = await cancelTask;
        _ = await matchTask;

        Assert.Equal(4, raceStore.LedgerCount("buyin"));
        Assert.Equal(cancelled ? 1 : 0, raceStore.LedgerCount("refund"));
        Assert.Equal(cancelled ? 10_000 : 9_500, raceStore.Balance("p1"));
        var p4Session = (await raceStore.GetSessionAsync("p4", Start.AddSeconds(2), default)).Session;
        Assert.True(cancelled
            ? p4Session is SolitaireQueueSessionResponse
            : p4Session is SolitaireMatchSessionResponse);
    }

    [Fact]
    public async Task Command_IsIdempotentRejectsStaleVersionAndReconnectsExactState()
    {
        var store = await MatchedStore();
        var initial = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("p1", Start, default)).Session);
        var command = new SolitaireCommandRequest(
            SolitaireCommandTypes.Draw,
            initial.Version,
            null,
            null,
            null,
            null);

        var first = await store.CommandAsync(
            "p1",
            initial.MatchId,
            command,
            "draw_command_0001",
            Start.AddSeconds(1),
            default);
        var replay = await store.CommandAsync(
            "p1",
            initial.MatchId,
            command,
            "draw_command_0001",
            Start.AddSeconds(2),
            default);
        var firstMatch = Assert.IsType<SolitaireMatchSessionResponse>(first.Session);
        var replayMatch = Assert.IsType<SolitaireMatchSessionResponse>(replay.Session);

        Assert.Equal(2, firstMatch.Version);
        Assert.Equal(1, firstMatch.Moves);
        Assert.Equal(firstMatch.Version, replayMatch.Version);
        Assert.Equal(firstMatch.Game.Moves, replayMatch.Game.Moves);
        Assert.Equal(firstMatch.Game.Stock.Select(card => card.Id), replayMatch.Game.Stock.Select(card => card.Id));
        Assert.Equal(firstMatch.Game.Waste.Select(card => card.Id), replayMatch.Game.Waste.Select(card => card.Id));
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.CommandAsync(
            "p1",
            initial.MatchId,
            command,
            "stale_command_0001",
            Start.AddSeconds(3),
            default));

        var reconnect = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("p1", Start.AddSeconds(4), default)).Session);
        Assert.Equal(firstMatch.Version, reconnect.Version);
        Assert.Equal(firstMatch.Game.Stock.Select(card => card.Id), reconnect.Game.Stock.Select(card => card.Id));
        Assert.Equal(firstMatch.Game.Waste.Select(card => card.Id), reconnect.Game.Waste.Select(card => card.Id));
    }

    [Fact]
    public async Task Deadline_FinalizesScoresAndClaimPaysNinetyPercentOnce()
    {
        var store = await MatchedStore();
        store.SetScore("p1", 120, moves: 30);
        store.SetScore("p2", 120, moves: 24);
        store.SetScore("p3", 90, moves: 5);
        store.SetScore("p4", 50, moves: 2);
        var deadlineRead = Start.AddMinutes(10).AddMilliseconds(1);

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("p1", deadlineRead, default)).Session);
        _ = await store.GetSessionAsync("p2", deadlineRead.AddSeconds(1), default);

        Assert.Equal("p2", result.Standings[0].PlayerId);
        Assert.Equal(18m, result.WinnerPayoutCredits);
        Assert.Equal(2m, result.PlatformFeeCredits);
        Assert.Equal(9_500, store.Balance("p2"));
        Assert.Equal(9_500, store.Balance("p1"));
        Assert.Equal(0, store.LedgerCount("payout"));
        Assert.Equal(1, store.RevenueRecords);

        _ = await store.ClaimAsync(
            "p2", result.MatchId, "claim_deadline_0001", deadlineRead.AddSeconds(2), default);
        _ = await store.ClaimAsync(
            "p2", result.MatchId, "claim_deadline_0001", deadlineRead.AddSeconds(3), default);
        Assert.Equal(11_300, store.Balance("p2"));
        Assert.Equal(1, store.LedgerCount("payout"));

        var current = store.Player("p1");
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.CommandAsync(
            "p1",
            result.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Draw, current.Version, null, null, null, null),
            "late_command_00001",
            deadlineRead,
            default));
    }

    [Fact]
    public async Task DuplicateCompletionAndClaim_DoNotRepeatSettlement()
    {
        var store = await MatchedStore();
        store.Complete("p1", 100, 20_000, 20);
        store.Complete("p2", 90, 19_000, 19);
        store.Complete("p3", 80, 18_000, 18);
        store.Complete("p4", 70, 17_000, 17);
        store.Complete("p4", 70, 17_000, 17);

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("p1", Start.AddMinutes(1), default)).Session);
        Assert.Equal(0, store.LedgerCount("payout"));
        Assert.Equal(1, store.RevenueRecords);
        Assert.Equal(9_500, store.Balance("p1"));

        _ = await store.ClaimAsync(
            "p1", result.MatchId, "claim_result_00001", Start.AddMinutes(2), default);
        _ = await store.ClaimAsync(
            "p1", result.MatchId, "claim_result_00001", Start.AddMinutes(3), default);
        Assert.IsType<SolitaireIdleSessionResponse>(
            (await store.GetSessionAsync("p1", Start.AddMinutes(3), default)).Session);
        Assert.Single(await store.GetHistoryAsync("p1", 30, Start.AddMinutes(3), default));
        Assert.Equal(1, store.LedgerCount("payout"));
        Assert.Equal(11_300, store.Balance("p1"));
    }

    [Fact]
    public async Task Forfeit_IsVersionedIdempotentAndCannotTrustClientTotals()
    {
        var store = await MatchedStore();
        var match = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("p1", Start, default)).Session);

        var forfeited = await store.ForfeitAsync(
            "p1",
            match.MatchId,
            match.Version,
            "forfeit_command_01",
            Start.AddSeconds(5),
            default);
        var replayed = await store.ForfeitAsync(
            "p1",
            match.MatchId,
            match.Version,
            "forfeit_command_01",
            Start.AddSeconds(6),
            default);

        Assert.Equal(SolitairePlayerStatuses.Forfeited, store.Player("p1").Status);
        Assert.Equal(0, store.Player("p1").Game.Score);
        var firstForfeit = Assert.IsType<SolitaireMatchSessionResponse>(forfeited.Session);
        var replayedForfeit = Assert.IsType<SolitaireMatchSessionResponse>(replayed.Session);
        Assert.Equal(firstForfeit.Version, replayedForfeit.Version);
        Assert.Equal(firstForfeit.Score, replayedForfeit.Score);
        Assert.Equal(firstForfeit.Moves, replayedForfeit.Moves);
        Assert.DoesNotContain(
            typeof(SolitaireCommandRequest).GetProperties(),
            property => property.Name is "Score" or "Moves" or "ElapsedSeconds" or "Seed" or "CompletedAtUtc");
    }

    private static Task<SolitaireStoreSession> Join(
        InMemoryStore store,
        string player,
        int count,
        long buyInCents,
        string key,
        DateTime at) => store.JoinAsync(
        player,
        player,
        count,
        buyInCents,
        3,
        key,
        (uint)player.GetHashCode(),
        at,
        default);

    private static async Task<InMemoryStore> MatchedStore()
    {
        var store = new InMemoryStore();
        store.Fund("p1", "p2", "p3", "p4");
        _ = await Join(store, "p1", 4, 500, "match_join_p1_01", Start);
        _ = await Join(store, "p2", 4, 500, "match_join_p2_01", Start);
        _ = await Join(store, "p3", 4, 500, "match_join_p3_01", Start);
        _ = await Join(store, "p4", 4, 500, "match_join_p4_01", Start);
        return store;
    }

    private sealed class InMemoryStore : ICompetitiveSolitaireStore
    {
        private readonly Lock sync = new();
        private readonly Dictionary<string, long> balances = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Link> sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SolitaireTicket>> partitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SolitaireTicket> tickets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryMatch> matches = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Operation, string Target, string Detail)> actions = new(StringComparer.Ordinal);
        private readonly HashSet<string> ledgers = new(StringComparer.Ordinal);
        private readonly HashSet<string> claimed = new(StringComparer.Ordinal);

        public int RevenueRecords { get; private set; }

        public void Fund(params string[] users)
        {
            foreach (var user in users) balances[user] = 10_000;
        }

        public long Balance(string user) => balances[user];
        public int LedgerCount(string type) => ledgers.Count(value => value.EndsWith($"-{type}", StringComparison.Ordinal));
        public SolitairePlayerState Player(string user) => MatchFor(user).Players[user];

        public void SetScore(string user, int score, int moves)
        {
            lock (sync)
            {
                var match = MatchFor(user);
                var player = match.Players[user];
                match.Players[user] = player with { Game = player.Game with { Score = score, Moves = moves } };
            }
        }

        public void Complete(string user, int score, long elapsed, int moves)
        {
            lock (sync)
            {
                var match = MatchFor(user);
                if (match.Match.Status == "settled") return;
                var player = match.Players[user];
                match.Players[user] = player with
                {
                    Status = SolitairePlayerStatuses.Finished,
                    Game = player.Game with { Score = score, Moves = moves },
                    Version = player.Version + 1,
                    ElapsedMilliseconds = elapsed,
                    CompletedAtUtc = match.Match.StartedAtUtc.AddMilliseconds(elapsed)
                };
                SettleIfTerminal(match, match.Match.StartedAtUtc.AddMilliseconds(elapsed));
            }
        }

        public Task<SolitaireStoreSession> GetSessionAsync(
            string userId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                Expire(MatchForOrNull(userId), nowUtc);
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> JoinAsync(
            string userId,
            string displayName,
            int playerCount,
            long buyInCents,
            int drawCount,
            string idempotencyKey,
            uint dealSeed,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var actionId = $"{userId}:{idempotencyKey}";
                var partition = $"{playerCount}:{buyInCents}:{drawCount}";
                var ticketId = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{userId}\n{idempotencyKey}");
                if (Replay(actionId, "join", ticketId, partition)) return Task.FromResult(Session(userId, nowUtc));
                if (sessions.TryGetValue(userId, out var current) && current.Kind != SolitaireSessionKinds.Idle)
                    throw new SolitaireConflictException("active session");
                if (balances[userId] < buyInCents)
                    throw new SolitaireInsufficientCreditsException(balances[userId], buyInCents);
                balances[userId] -= buyInCents;
                ledgers.Add($"{ticketId}-buyin");
                var ticket = new SolitaireTicket(ticketId, userId, displayName, playerCount, buyInCents, partition, "queued", nowUtc, null)
                {
                    DrawCount = drawCount
                };
                tickets[ticketId] = ticket;
                if (!partitions.TryGetValue(partition, out var queue))
                {
                    queue = [];
                    partitions.Add(partition, queue);
                }
                queue.Add(ticket);
                sessions[userId] = new Link(SolitaireSessionKinds.Queued, ticketId, null);
                actions.Add(actionId, ("join", ticketId, partition));
                if (queue.Count >= playerCount)
                {
                    var selected = queue.Take(playerCount).ToArray();
                    queue.RemoveRange(0, playerCount);
                    var matchId = FirestoreCompetitiveSolitaireStore.CreateLookupKey(
                        $"{partition}\n{string.Join("\n", selected.Select(value => value.TicketId))}");
                    var pool = playerCount * buyInCents;
                    var match = new SolitaireMatch(
                        matchId, playerCount, buyInCents, pool,
                        SolitaireMoney.WinnerPayout(playerCount, buyInCents),
                        pool - SolitaireMoney.WinnerPayout(playerCount, buyInCents),
                        dealSeed, nowUtc, nowUtc.AddMinutes(10), "playing",
                        selected.Select(value => value.UserId).ToArray(),
                        selected.Select(value => value.DisplayName).ToArray(),
                        selected.Select(value => value.TicketId).ToArray(),
                        selected.Select(value => value.JoinedAtUtc).ToArray(), null, null)
                    {
                        DrawCount = drawCount
                    };
                    var memory = new MemoryMatch(match);
                    for (var index = 0; index < selected.Length; index++)
                    {
                        var selectedTicket = selected[index];
                        tickets[selectedTicket.TicketId] = selectedTicket with { Status = "matched", MatchId = matchId };
                        memory.Players.Add(selectedTicket.UserId, new SolitairePlayerState(
                            matchId, selectedTicket.UserId, selectedTicket.DisplayName, index + 1,
                            SolitairePlayerStatuses.Playing, SolitaireEngine.CreateGame(dealSeed, drawCount), 1,
                            null, null, 0, false)
                        {
                            StartedAtUtc = nowUtc,
                            DeadlineAtUtc = nowUtc.AddMinutes(10)
                        });
                        sessions[selectedTicket.UserId] = new Link(SolitaireSessionKinds.Match, null, matchId);
                    }
                    matches.Add(matchId, memory);
                }
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> CancelAsync(
            string userId,
            string ticketId,
            string idempotencyKey,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var actionId = $"{userId}:{idempotencyKey}";
                if (Replay(actionId, "cancel", ticketId, string.Empty)) return Task.FromResult(Session(userId, nowUtc));
                if (!tickets.TryGetValue(ticketId, out var ticket) || ticket.UserId != userId || ticket.Status != "queued")
                    throw new SolitaireConflictException("not cancellable");
                partitions[ticket.PartitionKey].RemoveAll(value => value.TicketId == ticketId);
                tickets[ticketId] = ticket with { Status = "cancelled" };
                balances[userId] += ticket.BuyInCents;
                ledgers.Add($"{ticketId}-refund");
                sessions[userId] = new Link(SolitaireSessionKinds.Idle, null, null);
                actions.Add(actionId, ("cancel", ticketId, string.Empty));
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> CommandAsync(
            string userId,
            string matchId,
            SolitaireCommandRequest command,
            string idempotencyKey,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var memory = matches[matchId];
                Expire(memory, nowUtc);
                var detail = $"{command.Type}:{command.ExpectedVersion}:{command.From}:{command.StartIndex}:{command.To}:{command.Column}";
                var actionId = $"{userId}:{idempotencyKey}";
                if (Replay(actionId, "command", matchId, detail)) return Task.FromResult(Session(userId, nowUtc));
                if (memory.Match.Status != "playing") throw new SolitaireConflictException("finished");
                var player = memory.Players[userId];
                if (player.Version != command.ExpectedVersion) throw new SolitaireConflictException("stale");
                var game = SolitaireEngine.Apply(player.Game, command);
                var won = SolitaireEngine.IsWon(game);
                memory.Players[userId] = player with
                {
                    Game = game,
                    Version = player.Version + 1,
                    Status = won ? SolitairePlayerStatuses.Finished : player.Status,
                    ElapsedMilliseconds = won ? (long)(nowUtc - memory.Match.StartedAtUtc).TotalMilliseconds : null,
                    CompletedAtUtc = won ? nowUtc : null
                };
                actions.Add(actionId, ("command", matchId, detail));
                SettleIfTerminal(memory, nowUtc);
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> ForfeitAsync(
            string userId,
            string matchId,
            int expectedVersion,
            string idempotencyKey,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var memory = matches[matchId];
                var actionId = $"{userId}:{idempotencyKey}";
                if (Replay(actionId, "forfeit", matchId, expectedVersion.ToString())) return Task.FromResult(Session(userId, nowUtc));
                var player = memory.Players[userId];
                if (player.Version != expectedVersion) throw new SolitaireConflictException("stale");
                memory.Players[userId] = player with
                {
                    Status = SolitairePlayerStatuses.Forfeited,
                    Game = player.Game with { Score = 0 },
                    Version = player.Version + 1,
                    ElapsedMilliseconds = 600_000,
                    CompletedAtUtc = nowUtc
                };
                actions.Add(actionId, ("forfeit", matchId, expectedVersion.ToString()));
                SettleIfTerminal(memory, nowUtc);
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> DismissAsync(
            string userId,
            string matchId,
            string idempotencyKey,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var actionId = $"{userId}:{idempotencyKey}";
                if (Replay(actionId, "dismiss", matchId, string.Empty)) return Task.FromResult(Session(userId, nowUtc));
                var memory = matches[matchId];
                if (memory.Match.Status != "settled") throw new SolitaireConflictException("not settled");
                memory.Players[userId] = memory.Players[userId] with { Acknowledged = true };
                sessions[userId] = new Link(SolitaireSessionKinds.Idle, null, null);
                actions.Add(actionId, ("dismiss", matchId, string.Empty));
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<SolitaireStoreSession> ClaimAsync(
            string userId,
            string matchId,
            string idempotencyKey,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var actionId = $"{userId}:{idempotencyKey}";
                if (Replay(actionId, "claim", matchId, string.Empty))
                    return Task.FromResult(Session(userId, nowUtc));
                var memory = matches[matchId];
                if (memory.Match.Status != "settled" || !claimed.Add($"{matchId}:{userId}"))
                    throw new SolitaireConflictException("not claimable");
                var player = memory.Players[userId];
                if (player.PayoutCents > 0)
                {
                    balances[userId] += player.PayoutCents;
                    ledgers.Add($"{memory.Match.MatchId}-{userId}-payout");
                }
                memory.Players[userId] = player with { Acknowledged = true };
                sessions[userId] = new Link(SolitaireSessionKinds.Idle, null, null);
                actions.Add(actionId, ("claim", matchId, string.Empty));
                return Task.FromResult(Session(userId, nowUtc));
            }
        }

        public Task<IReadOnlyList<SolitaireHistoryItemResponse>> GetHistoryAsync(
            string userId,
            int limit,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var history = matches.Values
                    .Where(value => value.Match.Status == "settled" && value.Players.ContainsKey(userId)
                        && claimed.Contains($"{value.Match.MatchId}:{userId}"))
                    .Select(value =>
                    {
                        var ranked = SolitaireCompetitionRules.Rank(value.Players.Values.ToArray());
                        var player = value.Players[userId];
                        return new SolitaireHistoryItemResponse(
                            value.Match.MatchId, value.Match.PlayerCount,
                            SolitaireMoney.ToCredits(value.Match.BuyInCents),
                            SolitaireMoney.ToCredits(value.Match.PrizePoolCents),
                            ranked.ToList().FindIndex(item => item.UserId == userId) + 1,
                            player.Game.Score,
                            (int)Math.Ceiling((player.ElapsedMilliseconds ?? 0) / 1000d),
                            SolitaireMoney.ToCredits(player.PayoutCents),
                            SolitaireMoney.ToCredits(player.PayoutCents - value.Match.BuyInCents),
                            value.Match.CompletedAtUtc!.Value,
                            value.Match.DisplayNames.Where((_, index) => value.Match.PlayerIds[index] != userId).ToArray());
                    })
                    .OrderByDescending(value => value.CompletedAtUtc)
                    .Take(limit)
                    .ToArray();
                return Task.FromResult<IReadOnlyList<SolitaireHistoryItemResponse>>(history);
            }
        }

        private bool Replay(string actionId, string operation, string target, string detail)
        {
            if (!actions.TryGetValue(actionId, out var existing)) return false;
            if (existing != (operation, target, detail)) throw new SolitaireConflictException("reused key");
            return true;
        }

        private void Expire(MemoryMatch? memory, DateTime nowUtc)
        {
            if (memory is null || memory.Match.Status != "playing" || nowUtc < memory.Match.DeadlineAtUtc) return;
            foreach (var user in memory.Match.PlayerIds)
            {
                var player = memory.Players[user];
                if (player.Status != SolitairePlayerStatuses.Playing) continue;
                memory.Players[user] = player with
                {
                    Status = SolitairePlayerStatuses.Finished,
                    Version = player.Version + 1,
                    ElapsedMilliseconds = 600_000,
                    CompletedAtUtc = memory.Match.DeadlineAtUtc
                };
            }
            Settle(memory, memory.Match.DeadlineAtUtc);
        }

        private void SettleIfTerminal(MemoryMatch memory, DateTime at)
        {
            if (memory.Players.Values.All(value => value.Status != SolitairePlayerStatuses.Playing)) Settle(memory, at);
        }

        private void Settle(MemoryMatch memory, DateTime at)
        {
            if (memory.Match.Status == "settled") return;
            var winner = SolitaireCompetitionRules.Rank(memory.Players.Values.ToArray())[0];
            memory.Players[winner.UserId] = winner with { PayoutCents = memory.Match.WinnerPayoutCents };
            RevenueRecords++;
            memory.Match = memory.Match with { Status = "settled", CompletedAtUtc = at, WinnerUserId = winner.UserId };
            foreach (var user in memory.Match.PlayerIds) sessions[user] = new Link(SolitaireSessionKinds.Result, null, memory.Match.MatchId);
        }

        private SolitaireStoreSession Session(string userId, DateTime nowUtc)
        {
            if (!sessions.TryGetValue(userId, out var link) || link.Kind == SolitaireSessionKinds.Idle)
                return new(new SolitaireIdleSessionResponse(), balances[userId]);
            if (link.Kind == SolitaireSessionKinds.Queued)
            {
                var ticket = tickets[link.TicketId!];
                var queue = partitions[ticket.PartitionKey];
                return new(new SolitaireQueueSessionResponse(
                    ticket.TicketId, ticket.PlayerCount, SolitaireMoney.ToCredits(ticket.BuyInCents),
                    SolitaireMoney.ToCredits(ticket.PlayerCount * ticket.BuyInCents),
                    SolitaireMoney.ToCredits(SolitaireMoney.WinnerPayout(ticket.PlayerCount, ticket.BuyInCents)),
                    queue.FindIndex(value => value.TicketId == ticket.TicketId) + 1,
                    ticket.JoinedAtUtc,
                    queue.Select((value, index) => new SolitairePlayerResponse(
                        value.UserId, value.DisplayName, index + 1, value.JoinedAtUtc,
                        SolitairePlayerStatuses.Queued, value.UserId == userId)).ToArray()), balances[userId]);
            }
            var memory = matches[link.MatchId!];
            if (memory.Match.Status == "settled")
            {
                var ranked = SolitaireCompetitionRules.Rank(memory.Players.Values.ToArray());
                return new(new SolitaireResultSessionResponse(
                    memory.Match.MatchId, memory.Match.PlayerCount,
                    SolitaireMoney.ToCredits(memory.Match.BuyInCents),
                    SolitaireMoney.ToCredits(memory.Match.PrizePoolCents),
                    SolitaireMoney.ToCredits(memory.Match.WinnerPayoutCents),
                    SolitaireMoney.ToCredits(memory.Match.PlatformFeeCents),
                    memory.Match.StartedAtUtc, memory.Match.CompletedAtUtc!.Value,
                    ranked.Select((player, index) => new SolitaireStandingResponse(
                        index + 1, player.UserId, player.DisplayName, player.Game.Score,
                        player.Game.Moves, (int)Math.Ceiling((player.ElapsedMilliseconds ?? 0) / 1000d),
                        player.Status, SolitaireMoney.ToCredits(player.PayoutCents), player.UserId == userId)).ToArray())
                {
                    ClaimStatus = claimed.Contains($"{memory.Match.MatchId}:{userId}")
                        ? SolitaireClaimStatuses.Completed
                        : SolitaireClaimStatuses.Unclaimed,
                    CanClaim = !claimed.Contains($"{memory.Match.MatchId}:{userId}")
                }, balances[userId]);
            }
            var current = memory.Players[userId];
            return new(new SolitaireMatchSessionResponse(
                memory.Match.MatchId, memory.Match.PlayerCount,
                SolitaireMoney.ToCredits(memory.Match.BuyInCents),
                SolitaireMoney.ToCredits(memory.Match.PrizePoolCents),
                SolitaireMoney.ToCredits(memory.Match.WinnerPayoutCents),
                memory.Match.StartedAtUtc, memory.Match.DeadlineAtUtc,
                current.Version, current.Game.Score, current.Game.Moves,
                Math.Max(0, (long)(memory.Match.DeadlineAtUtc - nowUtc).TotalMilliseconds),
                SolitaireEngine.ToResponse(current.Game),
                memory.Players.Values.OrderBy(value => value.Seat).Select(value => new SolitairePlayerResponse(
                    value.UserId, value.DisplayName, value.Seat, memory.Match.JoinedAtUtc[value.Seat - 1],
                    value.Status, value.UserId == userId)).ToArray()), balances[userId]);
        }

        private MemoryMatch MatchFor(string user) => matches[sessions[user].MatchId!];
        private MemoryMatch? MatchForOrNull(string user) => sessions.TryGetValue(user, out var link) && link.MatchId is not null ? matches[link.MatchId] : null;

        private sealed record Link(string Kind, string? TicketId, string? MatchId);
        private sealed class MemoryMatch(SolitaireMatch match)
        {
            public SolitaireMatch Match { get; set; } = match;
            public Dictionary<string, SolitairePlayerState> Players { get; } = new(StringComparer.Ordinal);
        }
    }
}
