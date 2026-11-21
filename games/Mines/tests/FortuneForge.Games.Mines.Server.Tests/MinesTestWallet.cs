using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Mines.Server.Tests;

/// <summary>Wallet boundary test double: a separate transaction retains exact signed RPC semantics.</summary>
public sealed class MinesTestWallet(FirestoreDb database) : IGameWalletClient, IGameWalletAccountReader
{
    public bool DropNextReply { get; set; }
    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) =>
        new(0, ReadBalance(await Balance(accountId).GetSnapshotAsync(token)));

    public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
    {
        var id = request.Phase == GameWalletSettlementPhase.CompleteReservation
            ? GameWalletSettlementIdentity.CreateCompletionId("mines", accountId, request.OperationId)
            : GameWalletSettlementIdentity.CreateId("mines", accountId, request.OperationId);
        var document = database.Collection("minesTestWalletReceipts").Document(id);
        var receipt = await database.RunTransactionAsync(async transaction =>
        {
            var reads = await transaction.GetAllSnapshotsAsync([document, Balance(accountId)], token);
            if (reads[0].Exists)
            {
                if (reads[0].GetValue<string>("requestJson") != JsonSerializer.Serialize(request)) throw new GameWalletConflictException();
                return JsonSerializer.Deserialize<GameWalletSettlementReceipt>(reads[0].GetValue<string>("receiptJson"))! with { WasAlreadySettled = true };
            }
            if (request.Phase == GameWalletSettlementPhase.CompleteReservation)
            {
                var held = await transaction.GetSnapshotAsync(database.Collection("minesTestWalletReceipts")
                    .Document(GameWalletSettlementIdentity.CreateId("mines", accountId, request.OperationId)), token);
                var reservation = held.Exists ? JsonSerializer.Deserialize<GameWalletSettlementRequest>(held.GetValue<string>("requestJson")) : null;
                if (reservation?.Phase != GameWalletSettlementPhase.Reserve || reservation.OutcomeSha256 != request.ReservationSha256)
                    throw new GameWalletConflictException();
            }
            var before = ReadBalance(reads[1]);
            var insufficient = before < request.WagerCents;
            var afterWager = insufficient ? before : checked(before - request.WagerCents);
            var after = insufficient ? before : checked(afterWager + request.PayoutCents);
            var wagerId = !insufficient && request.WagerCents > 0 ? id + "-wager" : null;
            var payoutId = !insufficient && request.PayoutCents > 0 ? id + "-payout" : null;
            var result = new GameWalletSettlementReceipt(id, wagerId, payoutId, before, afterWager, after,
                DateTimeOffset.UtcNow, false, insufficient);
            transaction.Create(document, new Dictionary<string, object>
            {
                ["requestJson"] = JsonSerializer.Serialize(request), ["receiptJson"] = JsonSerializer.Serialize(result),
            });
            if (!insufficient && (request.WagerCents > 0 || request.PayoutCents > 0))
            {
                transaction.Set(Balance(accountId), new Dictionary<string, object>
                {
                    ["available"] = after / 100, ["availableFractionalCents"] = after % 100, ["version"] = FieldValue.Increment(1),
                }, SetOptions.MergeAll);
                if (wagerId is not null) transaction.Create(database.Collection("balanceTransactions").Document(wagerId),
                    new Dictionary<string, object> { ["amountCents"] = -request.WagerCents, ["balanceAfterCents"] = afterWager });
                if (payoutId is not null) transaction.Create(database.Collection("balanceTransactions").Document(payoutId),
                    new Dictionary<string, object> { ["amountCents"] = request.PayoutCents, ["balanceAfterCents"] = after });
            }
            return result;
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: token);
        if (DropNextReply) { DropNextReply = false; throw new GameWalletUnavailableException(); }
        return receipt;
    }

    private DocumentReference Balance(string userId) => database.Collection("userBalances").Document(userId + "_slotsCredits");
    private static long ReadBalance(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        var whole = snapshot.GetValue<long>("available");
        var fraction = snapshot.TryGetValue<long>("availableFractionalCents", out var cents) ? cents : 0;
        if (whole < 0 || fraction is < 0 or > 99) throw new InvalidOperationException("Invalid wallet balance.");
        return checked(whole * 100 + fraction);
    }
}
