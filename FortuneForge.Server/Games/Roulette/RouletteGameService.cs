using System.Collections.Immutable;
using System.Security.Cryptography;
using FortuneForge.Games.Roulette;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Roulette;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Activity;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Games.Abstractions.Auditing;
using System.Text.Json;

namespace FortuneForge.Server.Games.Roulette;

/// <summary>
/// Owns shared, no-credit Roulette tables. The Roulette package remains the
/// rules and settlement authority; table discovery and membership stay in the
/// server multiplayer layer.
/// </summary>
public sealed class RouletteGameService
{
    private const decimal StartingBalance = 1000m;
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private const int MaximumPlayers = 8;
    private static readonly TimeSpan HumanPresenceWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ManagedBettingDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ManagedSettlementDuration = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly SemaphoreSlim auditGate = new(1, 1);
    private readonly Dictionary<Guid, RouletteTable> tables = [];
    private readonly IMultiplayerMatchmaker matchmaking;
    private readonly IManagedTablePopulationDirector population;
    private readonly IGameAnalyticsSink? analytics;
    private readonly IGameSecurityEventSink? securityEvents;

    public RouletteGameService() : this(new MultiplayerMatchmaker(), new ManagedTablePopulationDirector()) { }

    internal RouletteGameService(
        IMultiplayerMatchmaker matchmaking,
        IManagedTablePopulationDirector? managedPopulationDirector = null,
        IGameAnalyticsSink? analytics = null,
        IGameSecurityEventSink? securityEvents = null)
    {
        this.matchmaking = matchmaking;
        population = managedPopulationDirector ?? new ManagedTablePopulationDirector();
        this.analytics = analytics;
        this.securityEvents = securityEvents;
    }

    public RouletteStatusResponse Status() => new(
        true,
        MinimumStake,
        MaximumStake,
        MinimumStake,
        StartingBalance,
        "free-play-shared-single-zero");

    public RouletteRoundResponse Start(string userId, string? displayName = null)
    {
        lock (gate)
        {
            var nowUtc = DateTime.UtcNow;
            var existing = tables.Values.FirstOrDefault(table => table.HasPlayer(userId));
            if (existing is not null)
            {
                if (existing.State.Phase == RouletteRoundPhase.Settled)
                {
                    existing.BeginNextRound();
                    RetireManagedPlayers(existing, nowUtc);
                }
                existing.TouchHuman(userId, nowUtc);
                MaintainPopulation(existing, nowUtc);
                return ToResponse(existing, userId);
            }

            var table = FindOpenTable() ?? CreateTable();
            if (table.State.Phase == RouletteRoundPhase.Settled)
            {
                table.BeginNextRound();
                RetireManagedPlayers(table, nowUtc);
            }
            if (table.Players.Count >= MaximumPlayers)
            {
                var leaving = table.Players.Where(player => player.IsManaged)
                    .OrderBy(player => player.JoinedRound).First();
                table.Players.Remove(leaving);
                ReleaseManagedPlayers(table, [leaving.UserId], nowUtc);
            }
            table.JoinHuman(userId, CleanDisplayName(displayName, table.Players.Count + 1), nowUtc);
            MaintainPopulation(table, nowUtc);
            return ToResponse(table, userId);
        }
    }

    public Task<RouletteRoundResponse> StartAuditedAsync(string userId, string? displayName = null, CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => Start(userId, displayName), "joined", cancellationToken);

    public Task<RouletteRoundResponse> GetAuditedAsync(string userId, Guid roundId, CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => Get(userId, roundId), "observed", cancellationToken);

    public Task<RouletteRoundResponse> PlaceBetAuditedAsync(string userId, Guid roundId, PlaceRouletteBetRequest request,
        CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => PlaceBet(userId, roundId, request), "bet-placed", cancellationToken);

    public Task<RouletteRoundResponse> RemoveBetAuditedAsync(string userId, Guid roundId, int betIndex,
        CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => RemoveBet(userId, roundId, betIndex), "bet-removed", cancellationToken);

    public Task<RouletteRoundResponse> ClearBetsAuditedAsync(string userId, Guid roundId, CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => ClearBets(userId, roundId), "bets-cleared", cancellationToken);

    public Task<RouletteRoundResponse> SpinAuditedAsync(string userId, Guid roundId, CancellationToken cancellationToken = default) =>
        AuditResponseAsync(() => Spin(userId, roundId), "spun", cancellationToken);

    private async Task<RouletteRoundResponse> AuditResponseAsync(Func<RouletteRoundResponse> action,
        string eventKind, CancellationToken cancellationToken)
    {
        await auditGate.WaitAsync(cancellationToken);
        try
        {
            var response = action();
            RouletteAuditSnapshot snapshot;
            lock (gate)
            {
                if (!tables.TryGetValue(response.RoundId, out var table))
                    throw new GameSecurityRecordingException(new InvalidOperationException("The Roulette table was removed before audit capture."));
                snapshot = Capture(table, eventKind);
            }
            await WriteAsync(snapshot, cancellationToken);
            return response;
        }
        finally { auditGate.Release(); }
    }

    private RouletteAuditSnapshot Capture(RouletteTable table, string eventKind)
    {
        var version = ++table.EventVersion;
        var instanceId = $"{table.Id:N}:{table.RoundNumber}";
        var players = table.Players.Select(player => new { player.UserId, player.SeatId, player.DisplayName,
            player.IsManaged, player.JoinedRound, player.DepartureRound, player.LastSeenAtUtc }).ToArray();
        var payload = new { table.Id, table.RoundNumber, table.RoundStartedAtUtc, table.RoundCompletedAtUtc,
            state = NormalizeState(table.State), players, balances = new Dictionary<string, decimal>(table.Balances, StringComparer.Ordinal),
            humanParticipants = table.HumanParticipants.Order(StringComparer.Ordinal).ToArray() };
        RoundAuditCapture? completion = null;
        if (table.State.Phase == RouletteRoundPhase.Settled && table.HumanParticipants.Count > 0)
            completion = RoundAuditCapture.Create("roulette", instanceId, table.Id.ToString("N"), table.RoundNumber,
                table.RoundStartedAtUtc, table.RoundCompletedAtUtc ?? DateTime.UtcNow, payload,
                "roulette-private-table-v1", PracticeOutcomeAudit.RouletteVersion,
                table.HumanParticipants.Order(StringComparer.Ordinal).Select(id => new RoundAuditParticipant(id, id, true, 0, 0, 0)).ToArray(),
                PracticeOutcomeAudit.Check(table.State));
        return new(instanceId, table.Id.ToString("N"), version, DateTime.UtcNow, payload,
            table.HumanParticipants.Concat(players.Where(player => !player.IsManaged).Select(player => player.UserId))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), eventKind, completion);
    }

    private Task WriteAsync(RouletteAuditSnapshot snapshot, CancellationToken cancellationToken) =>
        GameSecurityEventSink.WriteAsync(securityEvents, "roulette", snapshot.InstanceId, snapshot.Version,
            snapshot.OccurredAtUtc, snapshot.State, snapshot.ParticipantIds, snapshot.EventKind,
            snapshot.TableId, snapshot.Completion, cancellationToken);

    private sealed record RouletteAuditSnapshot(string InstanceId, string TableId, long Version,
        DateTime OccurredAtUtc, object State, string[] ParticipantIds, string EventKind,
        RoundAuditCapture? Completion);

    public RouletteRoundResponse Get(string userId, Guid roundId) =>
        Read(userId, roundId, ToResponse);

    public RouletteRoundResponse PlaceBet(
        string userId,
        Guid roundId,
        PlaceRouletteBetRequest request) =>
        Change(userId, roundId, table =>
        {
            if (request.Stake is < MinimumStake or > MaximumStake || request.Stake % MinimumStake != 0m)
                throw new ArgumentOutOfRangeException(nameof(request), "Choose a whole-number wager from R1 through R100.");
            if (request.Stake > table.Balances[userId])
                throw new RouletteRuleException("The stake exceeds your available table balance.");

            var bet = new RouletteBet(
                userId,
                ParseKind(request.Kind),
                request.Stake,
                request.Number,
                request.Numbers?.ToImmutableArray() ?? []);
            table.State = RouletteEngine.PlaceBet(table.State, bet);
            table.HumanParticipants.Add(userId);
            table.Balances[userId] -= bet.Stake;
        });

    public RouletteRoundResponse RemoveBet(string userId, Guid roundId, int betIndex) =>
        Change(userId, roundId, table =>
        {
            var owned = table.State.Bets
                .Select((bet, index) => (Bet: bet, Index: index))
                .Where(item => item.Bet.PlayerId == userId)
                .ToArray();
            if (betIndex < 0 || betIndex >= owned.Length)
                throw new ArgumentOutOfRangeException(nameof(betIndex));
            table.State = RouletteEngine.RemoveBet(table.State, owned[betIndex].Index);
            table.Balances[userId] += owned[betIndex].Bet.Stake;
        });

    public RouletteRoundResponse ClearBets(string userId, Guid roundId) =>
        Change(userId, roundId, table =>
        {
            var owned = table.State.Bets
                .Select((bet, index) => (Bet: bet, Index: index))
                .Where(item => item.Bet.PlayerId == userId)
                .OrderByDescending(item => item.Index)
                .ToArray();
            foreach (var item in owned)
            {
                table.State = RouletteEngine.RemoveBet(table.State, item.Index);
                table.Balances[userId] += item.Bet.Stake;
            }
        });

    public RouletteRoundResponse Spin(string userId, Guid roundId) =>
        Change(userId, roundId, table =>
        {
            if (!table.State.Bets.Any(bet => bet.PlayerId == userId))
                throw new RouletteRuleException("Place a bet before spinning.");
            var result = RouletteEngine.Spin(
                table.State,
                new RoulettePocket(RandomNumberGenerator.GetInt32(0, 37)));
            table.State = result.State;
            foreach (var returns in result.Settlements.GroupBy(settlement => settlement.PlayerId))
                if (table.Balances.ContainsKey(returns.Key))
                    table.Balances[returns.Key] += returns.Sum(settlement => settlement.TotalReturn);
            RecordCompletion(table, DateTime.UtcNow);
        });

    private RouletteRoundResponse Change(
        string userId,
        Guid roundId,
        Action<RouletteTable> action) =>
        Read(userId, roundId, (table, _) =>
        {
            action(table);
            table.Touch();
            return ToResponse(table, userId);
        });

    private T Read<T>(
        string userId,
        Guid roundId,
        Func<RouletteTable, string, T> action)
    {
        lock (gate)
        {
            if (!tables.TryGetValue(roundId, out var table) || !table.HasPlayer(userId))
                throw new RouletteAccessException();
            table.TouchHuman(userId, DateTime.UtcNow);
            MaintainPopulation(table, DateTime.UtcNow);
            return action(table, userId);
        }
    }

    private RouletteTable? FindOpenTable()
    {
        var candidates = tables.Values.Where(CanAcceptHuman);
        return matchmaking.FindLiveTable(
                   candidates,
                   CanAcceptHuman,
                   table => table.CreatedAtUtc,
                   table => table.Id.ToString("N"))
               ?? candidates.OrderBy(table => table.CreatedAtUtc).ThenBy(table => table.Id).FirstOrDefault();
    }

    private static bool CanAcceptHuman(RouletteTable table) =>
        table.Players.Count < MaximumPlayers || table.Players.Any(player => player.IsManaged);

    private RouletteTable CreateTable()
    {
        var table = new RouletteTable();
        tables.Add(table.Id, table);
        return table;
    }

    public void Sweep(DateTime nowUtc)
    {
        lock (gate)
        {
            var closing = new List<RouletteTable>();
            foreach (var table in tables.Values)
            {
                table.RemoveInactiveHumans(nowUtc - HumanPresenceWindow);
                var decision = Observe(table);
                if (!decision.KeepTableOpen)
                {
                    closing.Add(table);
                    continue;
                }
                if (table.HumanCount > 0)
                {
                    AddManagedPlayers(table, decision.ManagedArrivals, nowUtc);
                    PrepareManagedBets(table);
                    continue;
                }
                if (table.State.Phase == RouletteRoundPhase.Open &&
                    nowUtc - table.UpdatedAtUtc >= ManagedBettingDuration)
                {
                    PrepareManagedBets(table);
                    var result = RouletteEngine.Spin(
                        table.State,
                        new RoulettePocket(RandomNumberGenerator.GetInt32(0, 37)));
                    table.State = result.State;
                    foreach (var returns in result.Settlements.GroupBy(item => item.PlayerId))
                        table.Balances[returns.Key] += returns.Sum(item => item.TotalReturn);
                    RecordCompletion(table, nowUtc);
                    table.Touch(nowUtc);
                }
                else if (table.State.Phase == RouletteRoundPhase.Settled &&
                         nowUtc - table.UpdatedAtUtc >= ManagedSettlementDuration)
                {
                    table.BeginNextRound(nowUtc);
                    RetireManagedPlayers(table, nowUtc);
                    if (table.Players.Count == 0) closing.Add(table);
                    else PrepareManagedBets(table);
                }
            }
            foreach (var table in closing.Distinct()) Close(table, nowUtc);
        }
    }

    public async Task SweepAuditedAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await auditGate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<Guid, (RouletteTable Table, string Fingerprint)> before;
            lock (gate) before = tables.ToDictionary(pair => pair.Key,
                pair => (pair.Value, Fingerprint(pair.Value)));
            Exception? failure = null;
            try { Sweep(nowUtc); }
            catch (Exception exception) { failure = exception; }
            List<RouletteAuditSnapshot> changed = [];
            lock (gate)
            {
                foreach (var pair in tables)
                    if (!before.TryGetValue(pair.Key, out var previous) || previous.Fingerprint != Fingerprint(pair.Value))
                        changed.Add(Capture(pair.Value, "background-transition"));
                foreach (var pair in before.Where(pair => !tables.ContainsKey(pair.Key)))
                    changed.Add(Capture(pair.Value.Table, "table-closed"));
            }
            foreach (var snapshot in changed) await WriteAsync(snapshot, cancellationToken);
            if (failure is not null) throw failure;
        }
        finally { auditGate.Release(); }
    }

    private static string Fingerprint(RouletteTable table) => GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
    {
        table.RoundNumber, state = NormalizeState(table.State),
        balances = table.Balances.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        players = table.Players.Select(player => new { player.UserId, player.IsManaged, player.DepartureRound }).ToArray()
    }));

    private static object NormalizeState(RouletteRoundState state) => new
    {
        state.RoundId, state.Phase,
        bets = state.Bets.Select(bet => new { bet.PlayerId, bet.Kind, bet.Stake, bet.Number,
            numbers = bet.Numbers.IsDefault ? Array.Empty<int>() : bet.Numbers.ToArray() }).ToArray(),
        state.WinningPocket, state.Settlements
    };

    private void RecordCompletion(RouletteTable table, DateTime completedAtUtc)
    {
        table.RoundCompletedAtUtc ??= completedAtUtc;
        var roundId = $"{table.Id:N}:{table.RoundNumber}";
        foreach (var userId in table.State.Bets.Select(bet => bet.PlayerId).Distinct(StringComparer.Ordinal))
        {
            if (!table.HumanParticipants.Contains(userId) || table.RecordedParticipants.Contains(userId)) continue;
            if (analytics?.TryEnqueuePractice(new GameAnalyticsSettlement(
                "roulette", roundId, userId, "practice", true, table.RoundCompletedAtUtc.Value, table.RoundStartedAtUtc,
                0, 0, "in-memory-roulette-rounds", roundId)) == true)
                table.RecordedParticipants.Add(userId);
        }
    }

    private void MaintainPopulation(RouletteTable table, DateTime nowUtc)
    {
        var decision = Observe(table);
        if (!decision.KeepTableOpen)
        {
            Close(table, nowUtc);
            return;
        }
        AddManagedPlayers(table, decision.ManagedArrivals, nowUtc);
        PrepareManagedBets(table);
    }

    private ManagedTablePopulationDecision Observe(RouletteTable table) => population.Observe(new(
        ManagedPlayerGames.Roulette,
        table.Id.ToString("N"),
        table.RoundNumber,
        table.HumanCount,
        table.ManagedCount,
        1,
        Math.Min(MaximumPlayers, table.HumanCount + 2),
        tables.ContainsKey(table.Id)));

    private void AddManagedPlayers(RouletteTable table, int count, DateTime nowUtc)
    {
        if (count <= 0 || table.HumanCount == 0) return;
        var limited = Math.Min(count, 2 - table.ManagedCount);
        if (limited <= 0) return;
        var profiles = matchmaking.ReserveManagedPlayersAsync(
                ManagedPlayerGames.Roulette,
                AssignmentId(table),
                limited,
                table.Players.Select(player => player.UserId).ToArray(),
                nowUtc,
                CancellationToken.None)
            .GetAwaiter().GetResult();
        foreach (var profile in profiles)
            table.JoinManaged(
                profile,
                ManagedPlayerTableStayPolicy.DepartureRound(
                    ManagedPlayerGames.Roulette,
                    table.Id.ToString("N"),
                    profile.UserId,
                    table.RoundNumber),
                nowUtc);
    }

    private void RetireManagedPlayers(RouletteTable table, DateTime nowUtc)
    {
        var departing = table.Players.Where(player => player.IsManaged &&
            player.DepartureRound is { } departure &&
            ManagedPlayerTableStayPolicy.ShouldLeave(table.RoundNumber, departure)).ToArray();
        if (departing.Length == 0) return;
        foreach (var player in departing) table.Players.Remove(player);
        ReleaseManagedPlayers(table, departing.Select(player => player.UserId).ToArray(), nowUtc);
    }

    private static void PrepareManagedBets(RouletteTable table)
    {
        if (table.State.Phase != RouletteRoundPhase.Open) return;
        foreach (var player in table.Players.Where(player => player.IsManaged &&
                     table.State.Bets.All(bet => bet.PlayerId != player.UserId)))
        {
            var bet = RouletteManagedPlayers.Bet(
                player.UserId,
                table.RoundNumber,
                table.Balances[player.UserId]);
            if (bet.Stake <= 0) continue;
            table.State = RouletteEngine.PlaceBet(table.State, bet);
            table.Balances[player.UserId] -= bet.Stake;
        }
    }

    private void Close(RouletteTable table, DateTime nowUtc)
    {
        if (!tables.Remove(table.Id)) return;
        population.Close(ManagedPlayerGames.Roulette, table.Id.ToString("N"));
        ReleaseManagedPlayers(
            table,
            table.Players.Where(player => player.IsManaged).Select(player => player.UserId).ToArray(),
            nowUtc);
    }

    private void ReleaseManagedPlayers(
        RouletteTable table,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc)
    {
        if (profileIds.Count == 0) return;
        matchmaking.ReleaseManagedPlayersAsync(
                AssignmentId(table), profileIds, nowUtc, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private static string AssignmentId(RouletteTable table) =>
        $"roulette:{table.Id:N}";

    private static RouletteRoundResponse ToResponse(RouletteTable table, string userId)
    {
        var bets = table.State.Bets.Where(bet => bet.PlayerId == userId).ToArray();
        var settlements = table.State.Settlements.Where(settlement => settlement.PlayerId == userId).ToArray();
        return new(
            table.Id,
            table.Balances[userId],
            table.State.Phase == RouletteRoundPhase.Open ? "open" : "settled",
            bets.Select((bet, index) => new RouletteBetResponse(
                index,
                bet.PlayerId,
                KindName(bet.Kind),
                bet.Stake,
                bet.Number,
                bet.Numbers.IsDefault ? [] : bet.Numbers.ToArray())).ToArray(),
            table.State.WinningPocket?.Number,
            settlements.Select(settlement => new RouletteSettlementResponse(
                settlement.PlayerId,
                KindName(settlement.Kind),
                settlement.Stake,
                settlement.Won,
                settlement.TotalReturn)).ToArray(),
            table.Id.ToString("N"),
            table.RoundNumber,
            table.Players.Select(player => new RouletteTablePlayerResponse(
                player.SeatId,
                player.DisplayName,
                player.UserId == userId)).ToArray());
    }

    private static string CleanDisplayName(string? value, int fallbackNumber)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? $"Player {fallbackNumber}"
            : trimmed.Length <= 24 ? trimmed : trimmed[..24];
    }

    private static RouletteBetKind ParseKind(string? kind) => kind switch
    {
        "straight" => RouletteBetKind.Straight,
        "split" => RouletteBetKind.Split,
        "street" => RouletteBetKind.Street,
        "corner" => RouletteBetKind.Corner,
        "six-line" => RouletteBetKind.SixLine,
        "column" => RouletteBetKind.Column,
        "dozen" => RouletteBetKind.Dozen,
        "red" => RouletteBetKind.Red,
        "black" => RouletteBetKind.Black,
        "even" => RouletteBetKind.Even,
        "odd" => RouletteBetKind.Odd,
        "low" => RouletteBetKind.Low,
        "high" => RouletteBetKind.High,
        _ => throw new ArgumentException("Choose a valid Roulette bet.", nameof(kind)),
    };

    private static string KindName(RouletteBetKind kind) => kind switch
    {
        RouletteBetKind.Straight => "straight",
        RouletteBetKind.Split => "split",
        RouletteBetKind.Street => "street",
        RouletteBetKind.Corner => "corner",
        RouletteBetKind.SixLine => "six-line",
        RouletteBetKind.Column => "column",
        RouletteBetKind.Dozen => "dozen",
        RouletteBetKind.Red => "red",
        RouletteBetKind.Black => "black",
        RouletteBetKind.Even => "even",
        RouletteBetKind.Odd => "odd",
        RouletteBetKind.Low => "low",
        RouletteBetKind.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class RouletteTable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
        public int RoundNumber { get; private set; } = 1;
        public long EventVersion { get; set; }
        public DateTime RoundStartedAtUtc { get; private set; } = DateTime.UtcNow;
        public DateTime? RoundCompletedAtUtc { get; set; }
        public HashSet<string> HumanParticipants { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RecordedParticipants { get; } = new(StringComparer.Ordinal);
        public RouletteRoundState State { get; set; } =
            RouletteEngine.OpenRound(Guid.NewGuid().ToString("N"));
        public List<RoulettePlayer> Players { get; } = [];
        public Dictionary<string, decimal> Balances { get; } = new(StringComparer.Ordinal);

        public bool HasPlayer(string userId) =>
            Players.Any(player => player.UserId == userId);

        public int HumanCount => Players.Count(player => !player.IsManaged);
        public int ManagedCount => Players.Count(player => player.IsManaged);

        public void JoinHuman(string userId, string displayName, DateTime nowUtc)
        {
            if (HasPlayer(userId)) return;
            Players.Add(new RoulettePlayer(
                userId, $"seat_{Guid.NewGuid():N}", displayName, false, RoundNumber, null, nowUtc));
            Balances[userId] = StartingBalance;
            Touch(nowUtc);
        }

        public void JoinManaged(ManagedPlayerProfile profile, int departureRound, DateTime nowUtc)
        {
            if (HasPlayer(profile.UserId)) return;
            Players.Add(new RoulettePlayer(
                profile.UserId,
                $"seat_{Guid.NewGuid():N}",
                profile.PlayerName,
                true,
                RoundNumber,
                departureRound,
                nowUtc));
            Balances[profile.UserId] = StartingBalance;
            Touch(nowUtc);
        }

        public void TouchHuman(string userId, DateTime nowUtc)
        {
            var player = Players.Single(player => player.UserId == userId);
            if (!player.IsManaged) player.LastSeenAtUtc = nowUtc;
            Touch(nowUtc);
        }

        public void RemoveInactiveHumans(DateTime cutoffUtc) =>
            Players.RemoveAll(player => !player.IsManaged && player.LastSeenAtUtc < cutoffUtc);

        public void BeginNextRound(DateTime? nowUtc = null)
        {
            RoundNumber = checked(RoundNumber + 1);
            RoundStartedAtUtc = nowUtc ?? DateTime.UtcNow;
            RoundCompletedAtUtc = null;
            HumanParticipants.Clear();
            RecordedParticipants.Clear();
            State = RouletteEngine.OpenRound(Guid.NewGuid().ToString("N"));
            Touch(nowUtc ?? DateTime.UtcNow);
        }

        public void Touch(DateTime? nowUtc = null) => UpdatedAtUtc = nowUtc ?? DateTime.UtcNow;
    }

    private sealed class RoulettePlayer(
        string userId,
        string seatId,
        string displayName,
        bool isManaged,
        int joinedRound,
        int? departureRound,
        DateTime lastSeenAtUtc)
    {
        public string UserId { get; } = userId;
        public string SeatId { get; } = seatId;
        public string DisplayName { get; } = displayName;
        public bool IsManaged { get; } = isManaged;
        public int JoinedRound { get; } = joinedRound;
        public int? DepartureRound { get; } = departureRound;
        public DateTime LastSeenAtUtc { get; set; } = lastSeenAtUtc;
    }
}

public sealed class RouletteAccessException()
    : InvalidOperationException("That Roulette table is not available.");
