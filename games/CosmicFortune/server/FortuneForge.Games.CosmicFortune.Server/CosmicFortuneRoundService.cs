using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.CosmicFortune.Server;

public sealed class CosmicFortuneRoundService(CosmicFortuneRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<CosmicFortuneRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<CosmicFortuneRoundResult> SpinAsync(string accountId, CosmicFortuneRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);

    public Task<CosmicFortuneRoundStateReply> ActAsync(string accountId, string operationId, SealedTombAction action,
        CancellationToken cancellationToken) =>
        rounds.ActAsync(accountId, operationId, action, wallet, accounts, cancellationToken);
}
