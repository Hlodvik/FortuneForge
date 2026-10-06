using System.Collections.Immutable;
using System.Security.Cryptography;
using FortuneForge.Games.Roulette;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Roulette;
using FortuneForge.Server.Matchmaking;

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
    private readonly Dictionary<Guid, RouletteTable> tables = [];
    private readonly IMultiplayerMatchmaker matchmaking;
    private readonly IManagedTablePopulationDirector population;

    public RouletteGameService() : this(new MultiplayerMatchmaker(), new ManagedTablePopulationDirector()) { }

    internal RouletteGameService(
        IMultiplayerMatchmaker matchmaking,
        IManagedTablePopulationDirector? managedPopulationDirector = null)
    {
        this.matchmaking = matchmaking;
        population = managedPopulationDirector ?? new ManagedTablePopulationDirector();
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
