using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Mines;

namespace FortuneForge.Games.Mines.Server;

public sealed record StartMinesRequest(string RequestId, long WagerCents, int MineCount);
public sealed record RevealMinesRequest(string RequestId, int Tile);
public sealed record CashOutMinesRequest(string RequestId);
public sealed record MinesTileResponse(int Index, bool IsRevealed, string Kind);
public sealed record MinesRoundResponse(string RoundId, long WagerCents, int MineCount, string Status,
    int SafeReveals, int GemsRemaining, decimal CurrentMultiplier, decimal? NextMultiplier,
    long CashOutCents, bool CanCashOut, long PayoutCents, IReadOnlyList<MinesTileResponse> Tiles);
public sealed record MinesSessionResponse(long BalanceCents, bool Practice, MinesRoundResponse? Round,
    long MinimumWagerCents = 10, long MaximumWagerCents = 10_000, int HouseEdgeBasisPoints = 100);
public sealed record MinesErrorResponse(string Code, string Message);
public sealed record MinesStoredRound(string RoundId, string UserId, MinesRoundState State, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public void Validate(string expectedUser)
    {
        if (UserId != expectedUser || RoundId.Length != 64 || Version < 1 || UpdatedAt < CreatedAt)
            throw new InvalidOperationException("The persisted Mines round identity is corrupt.");
        try { State.Validate(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidOperationException("The persisted Mines round is corrupt.", exception); }
    }
}
public sealed record MinesCommandReceipt(string UserId, string Fingerprint, MinesSessionResponse Response);
public sealed record MinesTransactionSnapshot(long BalanceCents, MinesStoredRound? LatestRound,
    MinesStoredRound? TargetRound, MinesCommandReceipt? ExistingCommand);
public sealed record MinesMutation(MinesSessionResponse Response, MinesStoredRound? Round = null,
    long BalanceDeltaCents = 0, string? LedgerType = null, bool Replay = false);

public interface IMinesStore
{
    bool Practice { get; }
    Task<MinesTransactionSnapshot> ReadAsync(string userId, CancellationToken cancellationToken = default);
    Task<MinesSessionResponse> ExecuteAsync(string userId, string commandId, string fingerprint, string? roundId,
        Func<MinesTransactionSnapshot, MinesMutation> action, CancellationToken cancellationToken = default);
}

public sealed class MinesConflictException(string message) : Exception(message);
public sealed class MinesNotFoundException() : Exception("Mines round not found.");
public sealed class MinesInsufficientFundsException() : Exception("There are not enough credits for this wager.");

/// <summary>All state transitions run inside the store's wallet transaction. Only Project is public.</summary>
public sealed class MinesService(IMinesStore store, MinesEngine? engine = null, TimeProvider? clock = null)
{
    private readonly MinesEngine engine = engine ?? new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<MinesSessionResponse> SessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        ValidatePlayer(userId);
        var snapshot = await store.ReadAsync(userId, cancellationToken);
        return Session(snapshot.BalanceCents, snapshot.LatestRound);
    }

    public Task<MinesSessionResponse> StartAsync(string userId, StartMinesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WagerCents is < 10 or > 10_000) throw new ArgumentOutOfRangeException(nameof(request), "Wager must be 10 through 10000 cents.");
        if (request.MineCount is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(request), "Choose 1 through 24 mines.");
        return Command(userId, request.RequestId, $"start:{request.WagerCents}:{request.MineCount}", null, snapshot =>
        {
            if (snapshot.LatestRound?.State.Status == MinesRoundStatus.Active)
                throw new MinesConflictException("Finish the active round before placing another wager.");
            if (snapshot.BalanceCents < request.WagerCents) throw new MinesInsufficientFundsException();
            // Board generation occurs only after the wager and funds are validated inside the transaction.
            var state = engine.Start(request.WagerCents, request.MineCount);
            var now = clock.GetUtcNow();
            var round = new MinesStoredRound(Hash($"{userId}\n{request.RequestId}"), userId, state, 1, now, now);
            var balance = checked(snapshot.BalanceCents - request.WagerCents);
            return new(Session(balance, round), round, -request.WagerCents, "mines-wager");
        }, cancellationToken);
    }

    public Task<MinesSessionResponse> RevealAsync(string userId, string roundId, RevealMinesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRoundId(roundId);
        if (request.Tile is < 0 or > 24) throw new ArgumentOutOfRangeException(nameof(request), "Tile must be 0 through 24.");
        return Command(userId, request.RequestId, $"reveal:{roundId}:{request.Tile}", roundId,
            snapshot => Advance(snapshot, userId, roundId, state => engine.Reveal(state, request.Tile)), cancellationToken);
    }

    public Task<MinesSessionResponse> CashOutAsync(string userId, string roundId, CashOutMinesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRoundId(roundId);
        return Command(userId, request.RequestId, $"cashout:{roundId}", roundId,
            snapshot => Advance(snapshot, userId, roundId, engine.CashOut), cancellationToken);
    }

    private MinesMutation Advance(MinesTransactionSnapshot snapshot, string userId, string roundId, Func<MinesRoundState, MinesRoundState> advance)
    {
        var existing = snapshot.TargetRound;
        if (existing is null || existing.UserId != userId || existing.RoundId != roundId) throw new MinesNotFoundException();
        existing.Validate(userId);
        if (existing.State.Status != MinesRoundStatus.Active) throw new MinesConflictException("This round has already ended. Start a new round.");
        MinesRoundState state;
        try { state = advance(existing.State); }
        catch (InvalidOperationException exception) { throw new MinesConflictException(exception.Message); }
        var round = existing with { State = state, Version = checked(existing.Version + 1), UpdatedAt = clock.GetUtcNow() };
        var payout = state.Status == MinesRoundStatus.Active ? 0 : state.PayoutCents;
        var balance = checked(snapshot.BalanceCents + payout);
        return new(Session(balance, round), round, payout, payout > 0 ? "mines-payout" : null);
    }

    private Task<MinesSessionResponse> Command(string userId, string requestId, string fingerprint, string? roundId,
        Func<MinesTransactionSnapshot, MinesMutation> action, CancellationToken cancellationToken)
    {
        ValidatePlayer(userId);
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length is < 16 or > 128 ||
            requestId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("requestId must contain 16 through 128 letters, digits, hyphens, or underscores.");
        return store.ExecuteAsync(userId, Hash($"{userId}\n{requestId}"), fingerprint, roundId, snapshot =>
        {
            if (snapshot.ExistingCommand is { } receipt)
            {
                if (receipt.UserId != userId || receipt.Fingerprint != fingerprint)
                    throw new MinesConflictException("This requestId was already used for a different action or payload.");
                return new(receipt.Response, Replay: true);
            }
            return action(snapshot);
        }, cancellationToken);
    }

    private MinesSessionResponse Session(long balance, MinesStoredRound? round) => new(balance, store.Practice, round is null ? null : Project(round));
    private MinesRoundResponse Project(MinesStoredRound round)
    {
        round.Validate(round.UserId);
        var view = engine.Project(round.State);
        return new(round.RoundId, view.WagerCents, view.MineCount, view.Status switch
        {
            MinesRoundStatus.Active => "active", MinesRoundStatus.Lost => "lost", MinesRoundStatus.CashedOut => "cashed-out", MinesRoundStatus.Cleared => "cleared",
            _ => throw new InvalidOperationException("Unknown Mines status."),
        }, view.SafeReveals, view.GemsRemaining, view.CurrentMultiplier, view.NextMultiplier, view.CashOutCents,
            view.CanCashOut, view.PayoutCents, view.Tiles.Select(tile => new MinesTileResponse(tile.Index, tile.IsRevealed,
                tile.Kind.ToString().ToLowerInvariant())).ToArray());
    }

    private static void ValidatePlayer(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 256 || userId.Contains('/')) throw new ArgumentException("A valid account is required.");
    }
    private static void ValidateRoundId(string roundId)
    {
        if (roundId is null || roundId.Length != 64 || roundId.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("The Mines round identifier is invalid.");
    }
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static readonly JsonSerializerOptions StorageJson = new() { PropertyNameCaseInsensitive = true };
}
