using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.PhantomManor.Server;

public sealed class PhantomManorRoundService(PhantomManorRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<PhantomManorRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<PhantomManorRoundResult> SpinAsync(string accountId, PhantomManorRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
