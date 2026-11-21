using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.CandyCarnival.Server;

public sealed class CandyCarnivalRoundService(CandyCarnivalRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<CandyCarnivalRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<CandyCarnivalRoundResult> SpinAsync(string accountId, CandyCarnivalRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
