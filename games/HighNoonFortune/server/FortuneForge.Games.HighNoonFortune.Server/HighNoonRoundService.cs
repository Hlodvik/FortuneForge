using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.HighNoonFortune.Server;

public sealed class HighNoonRoundService(HighNoonRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<HighNoonRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<HighNoonRoundResult> SpinAsync(string accountId, HighNoonRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
