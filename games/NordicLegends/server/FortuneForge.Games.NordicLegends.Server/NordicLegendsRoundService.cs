using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.NordicLegends.Server;

public sealed class NordicLegendsRoundService(NordicLegendsRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<NordicLegendsRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<NordicLegendsRoundResult> SpinAsync(string accountId, NordicLegendsRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
