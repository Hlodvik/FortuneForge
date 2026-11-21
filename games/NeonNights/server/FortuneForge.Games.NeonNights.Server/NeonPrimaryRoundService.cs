using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.NeonNights.Server;

public sealed class NeonPrimaryRoundService(NeonPrimaryRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<NeonRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var context = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, context, cancellationToken);
    }

    public async Task<NeonRoundResult> SpinAsync(string accountId, NeonRoundRequest request, bool allowStart,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(request);
        if (!NeonRoundRequest.IsValidOperationId(request.OperationId) || request.UseSpecialBoost)
            throw new ArgumentException("The Neon spin options are invalid.", nameof(request));

        var stored = await rounds.FindAsync(accountId, request, cancellationToken);
        if (stored is null)
        {
            if (!allowStart) throw new NeonPrimaryRoundPausedException();
            var context = await accounts.GetStateAsync(accountId, cancellationToken);
            stored = await rounds.PrepareAsync(accountId, request, context.LegacyFreeGamesRemaining, cancellationToken);
        }
        return await SettleAsync(stored, cancellationToken);
    }

    public async Task<NeonRoundResult> SelectNitroAsync(string accountId, Guid spinId, int reelIndex,
        CancellationToken cancellationToken) =>
        await SettleAsync(await rounds.SelectNitroAsync(accountId, spinId, reelIndex, cancellationToken), cancellationToken);

    public async Task<bool> GrantAsync(string accountId, string operationId, long wagerPoints, bool car, bool disco,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (!NeonRoundRequest.IsValidOperationId(operationId) || car == disco)
            throw new ArgumentException("The Neon preview options are invalid.");
        var saved = await rounds.FindGrantAsync(accountId, operationId, wagerPoints, car, cancellationToken);
        if (saved is { } already) return already;
        var context = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.GrantAsync(accountId, operationId, wagerPoints, car, context.LegacyFreeGamesRemaining, cancellationToken);
    }

    private async Task<NeonRoundResult> SettleAsync(NeonPrimaryStoredRound saved, CancellationToken cancellationToken)
    {
        if (saved.Final is { } complete) return complete;
        var round = saved.Round;
        if (round.Outcome.NeonNitroWild?.RequiresChoice == true)
        {
            if (round.Reservation is null)
            {
                var hold = await SendWalletAsync(round, new(round.Outcome.SpinId.ToString("N"), round.OutcomeSha256,
                    round.WagerCents, 0, round.SpendsLegacy, round.ExpectedLegacy, Phase: GameWalletSettlementPhase.Reserve), cancellationToken);
                if (hold.InsufficientFunds || hold.InsufficientLegacyFreeGames || hold.LegacyFreeGamesCountMismatch)
                    return await rounds.FinalizeAsync(round, hold, cancellationToken);
                var held = await rounds.PublishReservedNitroAsync(round, hold, cancellationToken);
                if (held.Final is { } finalized) return finalized;
                round = held.Round;
            }
            if (round.Outcome.NeonNitroWild?.RequiresChoice == true)
                return new(null, NeonPrimaryRoundStore.CreateHeldOffer(round), null);
        }

        var operation = round.Outcome.SpinId.ToString("N");
        var request = round.ReservationSha256 is { } reservation
            ? new GameWalletSettlementRequest(operation, round.OutcomeSha256, 0, round.PayoutCents,
                Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reservation)
            : new GameWalletSettlementRequest(operation, round.OutcomeSha256, round.WagerCents,
                round.PayoutCents, round.SpendsLegacy, round.ExpectedLegacy);
        var walletReceipt = await SendWalletAsync(round, request, cancellationToken);
        return await rounds.FinalizeAsync(round, walletReceipt, cancellationToken);
    }

    private async Task<GameWalletSettlementReceipt> SendWalletAsync(NeonPrimaryPreparedRound round,
        GameWalletSettlementRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await wallet.SettleAsync(round.AccountId, request, cancellationToken);
        }
        catch (GameWalletConflictException exception)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }
}
