using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.SamuraiFortune.Server;

public sealed class SamuraiFortuneRoundService(SamuraiFortuneRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<SamuraiFortuneRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<SamuraiFortuneRoundResult> SpinAsync(string accountId, SamuraiFortuneRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
