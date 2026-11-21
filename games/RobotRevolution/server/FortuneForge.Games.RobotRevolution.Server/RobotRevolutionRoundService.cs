using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.RobotRevolution.Server;

public sealed class RobotRevolutionRoundService(RobotRevolutionRoundStore rounds, IGameWalletClient wallet, IGameWalletAccountReader accounts)
{
    public async Task<RobotRevolutionRoundStateReply> ReadStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await accounts.GetStateAsync(accountId, cancellationToken);
        return await rounds.ReadStateAsync(accountId, balance, cancellationToken);
    }

    public Task<RobotRevolutionRoundResult> SpinAsync(string accountId, RobotRevolutionRoundRequest request, bool allowStart,
        CancellationToken cancellationToken) =>
        rounds.SpinAsync(accountId, request, allowStart, wallet, accounts, cancellationToken);
}
