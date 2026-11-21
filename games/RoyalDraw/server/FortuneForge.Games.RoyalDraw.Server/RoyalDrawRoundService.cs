using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.RoyalDraw.Server;

public sealed class RoyalDrawRoundService(RoyalDrawRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<RoyalDrawRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<RoyalDrawRoundResult> SpinAsync(string accountId, RoyalDrawRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
