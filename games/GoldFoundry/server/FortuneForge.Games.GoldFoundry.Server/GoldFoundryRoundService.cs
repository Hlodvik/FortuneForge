using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.GoldFoundry.Server;

public sealed class GoldFoundryRoundService(GoldFoundryRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<GoldFoundryRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance.SlotsCreditsBalanceCents, cancellationToken);
    }

    public async Task<GoldFoundryRoundResult> SpinAsync(string accountId, GoldFoundryRoundRequest request, bool allowStart,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(request);
        if (!GoldFoundryRoundRequest.IsValidOperationId(request.OperationId))
            throw new ArgumentException("Include a valid Gold Foundry spin operation ID.", nameof(request));
        var saved = await rounds.PrepareOrReadAsync(accountId, request, allowStart, cancellationToken);
        if (saved.Final is { } final) return final;

        var round = saved.Round;
        GameWalletSettlementReceipt receipt;
        try
        {
            receipt = await wallet.SettleAsync(accountId, new(round.Outcome.SpinId.ToString("N"), round.OutcomeSha256,
                round.WagerCents, round.PayoutCents), cancellationToken);
        }
        catch (GameWalletConflictException exception)
        {
            throw new GameWalletUnavailableException(exception);
        }
        return await rounds.FinalizeAsync(round, receipt, cancellationToken);
    }
}
