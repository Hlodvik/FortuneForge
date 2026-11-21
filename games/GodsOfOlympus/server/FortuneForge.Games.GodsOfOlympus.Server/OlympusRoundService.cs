using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public sealed class OlympusRoundService(OlympusRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<OlympusRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<OlympusRoundResult> SpinAsync(string accountId, OlympusRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
