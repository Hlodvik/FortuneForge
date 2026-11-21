using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Craps;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Craps;

internal sealed class CrapsCompatibilityWallet(FirestoreDb database, IWalletRoundSettlementStore wallet, TimeProvider clock)
    : IGameWalletClient, IGameWalletAccountReader
{
    public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var terms = WalletRoundSettlement.FromDirectRequest(CrapsModule.Descriptor.Id, accountId, request);
        try
        {
            var settlement = await wallet.SettleAsync(terms, clock.GetUtcNow(), cancellationToken);
            return settlement.ToWireReceipt();
        }
        catch (WalletRoundSettlementConflictException)
        {
            throw new GameWalletConflictException();
        }
        catch (Exception exception) when ((exception is InvalidOperationException or Grpc.Core.RpcException) &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var balance = await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, accountId,
                useLegacyLedger: false, cancellationToken);
            return new GameWalletAccountState(0, balance);
        }
        catch (Exception exception) when ((exception is InvalidOperationException or Grpc.Core.RpcException) &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }
}
