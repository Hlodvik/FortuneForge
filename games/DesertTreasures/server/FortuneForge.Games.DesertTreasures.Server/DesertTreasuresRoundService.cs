using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.DesertTreasures.Server;

public sealed class DesertTreasuresRoundService(DesertTreasuresRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<DesertTreasuresRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<DesertTreasuresRoundResult> SpinAsync(string accountId, DesertTreasuresRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
