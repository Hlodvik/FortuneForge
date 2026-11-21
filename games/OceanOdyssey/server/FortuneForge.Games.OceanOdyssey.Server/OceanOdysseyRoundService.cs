using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.OceanOdyssey.Server;

public sealed class OceanOdysseyRoundService(OceanOdysseyRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<OceanOdysseyRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<OceanOdysseyRoundResult> SpinAsync(string accountId, OceanOdysseyRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
