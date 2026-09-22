using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FortuneForge.Games.Roulette;

namespace FortuneForge.Server.Games.Roulette;

/// <summary>Owns account-isolated, no-credit Roulette practice rounds.</summary>
public sealed class RouletteGameService
{
    private const decimal StartingBalance = 1000m;
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private readonly ConcurrentDictionary<Guid, RouletteSession> rounds = new();

    public RouletteStatusResponse Status() => new(true, MinimumStake, MaximumStake, MinimumStake, StartingBalance, "free-play-single-zero");

    public RouletteRoundResponse Start(string userId)
    {
        var session = new RouletteSession(userId, RouletteEngine.OpenRound(Guid.NewGuid().ToString("N")), StartingBalance);
        if (!rounds.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not open a Roulette table.");
        return ToResponse(session);
    }

    public RouletteRoundResponse Get(string userId, Guid roundId) => Read(userId, roundId, ToResponse);

    public RouletteRoundResponse PlaceBet(string userId, Guid roundId, PlaceRouletteBetRequest request) =>
        Change(userId, roundId, session =>
        {
            if (request.Stake is < MinimumStake or > MaximumStake || request.Stake % MinimumStake != 0m)
                throw new ArgumentOutOfRangeException(nameof(request), "Choose a whole-number wager from R1 through R100.");
            if (request.Stake > session.Balance)
                throw new RouletteRuleException("The stake exceeds your available practice balance.");

            var bet = new RouletteBet(userId, ParseKind(request.Kind), request.Stake, request.Number,
                request.Numbers?.ToImmutableArray() ?? []);
            session.State = RouletteEngine.PlaceBet(session.State, bet);
            session.Balance -= bet.Stake;
        });

    public RouletteRoundResponse RemoveBet(string userId, Guid roundId, int betIndex) =>
        Change(userId, roundId, session =>
        {
            var bet = session.State.Bets[betIndex];
            session.State = RouletteEngine.RemoveBet(session.State, betIndex);
            session.Balance += bet.Stake;
        });

    public RouletteRoundResponse ClearBets(string userId, Guid roundId) =>
        Change(userId, roundId, session =>
        {
            session.Balance += session.State.Bets.Sum(bet => bet.Stake);
            session.State = RouletteEngine.ClearBets(session.State);
        });

    public RouletteRoundResponse Spin(string userId, Guid roundId) =>
        Change(userId, roundId, session =>
        {
            var result = RouletteEngine.Spin(session.State, new RoulettePocket(RandomNumberGenerator.GetInt32(0, 37)));
            session.State = result.State;
            session.Balance += result.Settlements.Sum(settlement => settlement.TotalReturn);
        });

    private RouletteRoundResponse Change(string userId, Guid roundId, Action<RouletteSession> action) =>
        Read(userId, roundId, session =>
        {
            action(session);
            return ToResponse(session);
        });

    private T Read<T>(string userId, Guid roundId, Func<RouletteSession, T> action)
    {
        if (!rounds.TryGetValue(roundId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new RouletteAccessException();
        lock (session.SyncRoot) return action(session);
    }

    private static RouletteRoundResponse ToResponse(RouletteSession session) => new(
        session.Id, session.Balance, session.State.Phase == RouletteRoundPhase.Open ? "open" : "settled",
        session.State.Bets.Select((bet, index) => new RouletteBetResponse(index, bet.PlayerId, KindName(bet.Kind), bet.Stake, bet.Number,
            bet.Numbers.IsDefault ? [] : bet.Numbers.ToArray())).ToArray(),
        session.State.WinningPocket?.Number,
        session.State.Settlements.Select(settlement => new RouletteSettlementResponse(settlement.PlayerId, KindName(settlement.Kind),
            settlement.Stake, settlement.Won, settlement.TotalReturn)).ToArray());

    private static RouletteBetKind ParseKind(string? kind) => kind switch
    {
        "straight" => RouletteBetKind.Straight, "split" => RouletteBetKind.Split, "street" => RouletteBetKind.Street,
        "corner" => RouletteBetKind.Corner, "six-line" => RouletteBetKind.SixLine, "column" => RouletteBetKind.Column,
        "dozen" => RouletteBetKind.Dozen, "red" => RouletteBetKind.Red, "black" => RouletteBetKind.Black,
        "even" => RouletteBetKind.Even, "odd" => RouletteBetKind.Odd, "low" => RouletteBetKind.Low, "high" => RouletteBetKind.High,
        _ => throw new ArgumentException("Choose a valid Roulette bet.", nameof(kind)),
    };

    private static string KindName(RouletteBetKind kind) => kind switch
    {
        RouletteBetKind.Straight => "straight", RouletteBetKind.Split => "split", RouletteBetKind.Street => "street",
        RouletteBetKind.Corner => "corner", RouletteBetKind.SixLine => "six-line", RouletteBetKind.Column => "column",
        RouletteBetKind.Dozen => "dozen", RouletteBetKind.Red => "red", RouletteBetKind.Black => "black",
        RouletteBetKind.Even => "even", RouletteBetKind.Odd => "odd", RouletteBetKind.Low => "low", RouletteBetKind.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class RouletteSession(string userId, RouletteRoundState state, decimal balance)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public RouletteRoundState State { get; set; } = state;
        public decimal Balance { get; set; } = balance;
    }
}

public sealed class RouletteAccessException() : InvalidOperationException("That Roulette table is not available.");
