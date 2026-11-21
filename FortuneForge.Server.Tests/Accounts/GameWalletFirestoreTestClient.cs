using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Tests.Accounts;

internal sealed class GameWalletFirestoreTestClient(FirestoreDb database, string gameId) : IGameWalletClient, IGameWalletAccountReader
{
    public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson)));
        var receipt = await new FirestoreWalletRoundSettlementStore(database).SettleAsync(new(gameId, accountId,
            request.OperationId, request.OutcomeSha256, request.WagerCents, request.PayoutCents,
            RequestBodySha256: hash, Phase: request.Phase, ReservationSha256: request.ReservationSha256,
            MinimumBalanceBeforeCents: request.MinimumBalanceBeforeCents), DateTimeOffset.UtcNow, cancellationToken);
        return receipt.ToWireReceipt();
    }

    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
        new(0, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, accountId, false, cancellationToken));
}
