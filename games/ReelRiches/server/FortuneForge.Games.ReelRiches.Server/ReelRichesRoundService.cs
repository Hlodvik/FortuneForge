using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.ReelRiches.Server;

public sealed class ReelRichesRoundService(ReelRichesRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<ReelRichesRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<ReelRichesRoundResult> SpinAsync(string accountId, ReelRichesRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
