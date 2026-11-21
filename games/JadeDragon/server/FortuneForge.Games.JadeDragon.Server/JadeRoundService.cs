using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.JadeDragon.Server;

public sealed class JadeRoundService(JadeRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<JadeRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public async Task<JadeRoundResult> SpinAsync(string accountId, JadeRoundRequest request, bool allowStart,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(request);
        if (!JadeRoundRequest.IsValidOperationId(request.OperationId))
            throw new ArgumentException("Include a valid Jade Dragon spin operation ID.", nameof(request));
        var saved = await rounds.FindAsync(accountId, request, cancellationToken);
        if (saved is null)
        {
            if (!allowStart) throw new JadeRoundPausedException();
            var balance = await accounts.GetStateAsync(accountId, cancellationToken);
            saved = await rounds.PrepareAsync(accountId, request, balance.LegacyFreeGamesRemaining, cancellationToken);
        }
        if (saved.Final is { } final) return final;
        var round = saved.Round;
        GameWalletSettlementReceipt receipt;
        try
        {
            receipt = await wallet.SettleAsync(accountId, new(round.SpinId.ToString("N"), round.OutcomeSha256,
                round.WagerCents, round.PayoutCents, round.SpendsLegacy, round.Before.LegacyRemaining), cancellationToken);
        }
        catch (GameWalletConflictException exception)
        {
            throw new GameWalletUnavailableException(exception);
        }
        return await rounds.FinalizeAsync(round, receipt, cancellationToken);
    }
}
