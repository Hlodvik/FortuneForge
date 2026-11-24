using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.PharaohsAscent.Server;

public sealed class PharaohsAscentRoundService(PharaohsAscentRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<PharaohsAscentRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<PharaohsAscentRoundResult> SpinAsync(string accountId, PharaohsAscentRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);

    public Task<PharaohsAscentRoundStateReply> ActAsync(string accountId, string operationId, SealedTombAction action,
        CancellationToken cancellationToken) =>
        rounds.ActAsync(accountId, operationId, action, wallet, accounts, cancellationToken);
}
