using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.ArcaneArchives.Server;

/// <summary>Resumes the same game preparation and wallet operation across failed deliveries.</summary>
public sealed class ArcaneRoundService(ArcaneRoundStore rounds, IGameWalletClient wallet)
{
    public Task<ArcaneRoundState> ReadStateAsync(string accountId, CancellationToken cancellationToken) =>
        rounds.ReadAsync(accountId, cancellationToken);

    public async Task<ArcaneRoundResult> SpinAsync(string accountId, ArcaneRoundRequest request, bool allowStart,
        CancellationToken cancellationToken)
    {
        var status = await rounds.PrepareAsync(accountId, request, allowStart, cancellationToken);
        if (status.Final is { } finalized) return finalized;

        var round = status.Prepared.Round;
        var receipt = await wallet.SettleAsync(accountId, new GameWalletSettlementRequest(round.Outcome.SpinId.ToString("N"),
            round.OutcomeSha256, round.WagerCents, round.PayoutCents, round.Request.UseFreeSpin), cancellationToken);
        return await rounds.FinalizeAsync(round, receipt, cancellationToken);
    }
}
