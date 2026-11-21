using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HouseHoldem;
using Google.Cloud.Firestore;
using Grpc.Core;
using static FortuneForge.Server.Cards.HouseHoldem.HouseHoldemRoundCodec;

namespace FortuneForge.Server.Cards.HouseHoldem;

/// <summary>Game lifecycle coordinator. All money effects use the signed generic wallet client.</summary>
internal sealed class HouseHoldemFirestoreStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts, bool allowNewRounds = true) : IHouseHoldemStore
{
    private readonly HouseHoldemPreparedStore commands = new(database);

    public async Task<HouseHoldemStoreResult?> TryReplayStartAsync(string userId, string idempotencyKey,
        long anteCents, long tripsCents, CancellationToken cancellationToken)
    {
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var saved = await commands.ExistingAsync(userId, idempotencyKey, roundId, "start", anteCents, tripsCents, cancellationToken);
        if (saved is not null) return RequireResult(await CompleteAsync(saved, cancellationToken));
        if (await commands.ReadRoundAsync(userId, roundId, cancellationToken) is null) return null;
        return await StartAsync(userId, idempotencyKey, anteCents, tripsCents, [], DateTimeOffset.UtcNow, cancellationToken);
    }

    public Task<HouseHoldemStoreResult> StartAsync(string userId, string idempotencyKey, long anteCents, long tripsCents,
        IReadOnlyList<string> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(userId, idempotencyKey, Hash($"{userId}\n{idempotencyKey}"), "start", anteCents, tripsCents,
            shuffledDeck, nowUtc, cancellationToken);

    public Task<HouseHoldemStoreResult> TransitionAsync(string userId, string roundId, string idempotencyKey,
        string command, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(userId, idempotencyKey, roundId, command, 0, 0, null, nowUtc, cancellationToken);

    private async Task<HouseHoldemStoreResult> ExecuteAsync(string userId, string key, string roundId, string command,
        long ante, long trips, IReadOnlyList<string>? deck, DateTimeOffset now, CancellationToken token)
    {
        var saved = await commands.ExistingAsync(userId, key, roundId, command, ante, trips, token);
        if (saved is null)
        {
            var state = await accounts.GetStateAsync(userId, token);
            saved = await commands.PrepareAsync(userId, key, roundId, command, ante, trips, deck, now,
                state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException(), allowNewRounds, token);
        }
        return RequireResult(await CompleteAsync(saved, token));
    }

    private async Task<HouseHoldemCommandCompletion> CompleteAsync(HouseHoldemCommandRecord saved, CancellationToken token)
    {
        if (saved.Completion is { } final) return final;
        var prepared = saved.Prepared;
        var receipt = await wallet.SettleAsync(prepared.AccountId, prepared.WalletRequest(), token);
        return await commands.FinalizeAsync(prepared, receipt, token);
    }

    private static HouseHoldemStoreResult RequireResult(HouseHoldemCommandCompletion completion) => completion.Result ??
        throw new HouseHoldemInsufficientCreditsException(completion.Wallet!.BalanceBeforeCents,
            completion.RequiredCents);

    public async Task RecoverPendingAsync(string account, CancellationToken token)
    {
        if (await commands.PendingAsync(account, token) is { } pending)
            await CompleteAsync(pending, token);
    }

    public async Task<HouseHoldemStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        await RecoverPendingAsync(userId, cancellationToken);
        var stored = await commands.ReadRoundAsync(userId, roundId, cancellationToken);
        if (stored is null) return null;
        var account = await accounts.GetStateAsync(userId, cancellationToken);
        return new(roundId, stored.Round, account.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException());
    }

    public async Task<string?> GetActiveRoundIdAsync(string userId, CancellationToken cancellationToken)
    {
        await RecoverPendingAsync(userId, cancellationToken);
        return await commands.ActiveRoundAsync(userId, cancellationToken);
    }

    internal static async Task<T> RunWithContentionRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await operation(); }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted && attempt < 4 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }
}

internal sealed class HouseHoldemRoundPausedException() : Exception("This game is not accepting new wagers yet.");
