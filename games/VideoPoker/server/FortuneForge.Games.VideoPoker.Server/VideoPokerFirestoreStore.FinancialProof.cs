using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.VideoPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.VideoPoker;

internal sealed record VideoPokerFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations)
{
    internal static VideoPokerFinancialSnapshot Empty => new([], []);
}

public sealed partial class VideoPokerFirestoreStore
{
    private async Task<VideoPokerFinancialSnapshot> ReadOpeningFinancialEvidenceAsync(Transaction transaction,
        DocumentSnapshot roundSnapshot, VideoPokerRound opening, CancellationToken token)
    {
        var user = roundSnapshot.GetValue<string>("userId");
        var roundId = roundSnapshot.Id;
        if (Text(roundSnapshot, "startIdempotencyKey") is { } openingKey)
        {
            var saved = await transaction.GetSnapshotAsync(Operation(user, openingKey), token);
            if (saved.Exists)
            {
                var original = OpenOperation(saved, user, VideoPokerPreparedOperation.RequestHash("deal", roundId,
                    opening.CoinsWagered, opening.HandCount));
                if (original.Prepared is not { Kind: "deal" } prepared || original.Final is null ||
                    original.Receipt is not { InsufficientFunds: false } receipt || prepared.RoundId != roundId ||
                    Serialize(prepared.Round) != Serialize(opening))
                    throw new GameWalletUnavailableException();
                ValidateReceipt(prepared, receipt);
                return new(CurrentFinancialEntries(prepared, receipt), [FinancialOperation(prepared, receipt)]);
            }
        }

        var wager = VideoPokerMoney.WagerCents(opening.CoinsWagered, opening.HandCount);
        var reference = database.Collection("balanceTransactions").Document($"{VideoPokerBackend.GameId}-{roundId}-wager");
        var legacy = await transaction.GetSnapshotAsync(reference, token);
        var evidence = legacy.Exists ? FirestoreWalletEvidence.Legacy(legacy, roundId)
            : new("balanceTransactions", reference.Id, user, "video-poker-wager", roundId,
                -wager, RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false);
        if (evidence.UserId != user || evidence.Kind != "video-poker-wager" || evidence.AmountCents != -wager)
            throw new GameWalletUnavailableException();
        return new([evidence], []);
    }

    private Task<VideoPokerFinancialSnapshot> RecoverFinancialEvidenceAsync(VideoPokerPreparedOperation operation, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var source = await transaction.GetSnapshotAsync(Round(operation.RoundId), token);
            if (!source.Exists || source.GetValue<string>("userId") != operation.UserId) throw new VideoPokerRoundNotFoundException();
            var opening = VideoPokerRoundCodec.ReadOriginalDeal(source);
            if (operation.Round.HeldCardPositions is null || Serialize(VideoPokerRoundEngine.Draw(opening, operation.Round.HeldCardPositions)) != Serialize(operation.Round))
                throw new GameWalletUnavailableException();
            return await ReadOpeningFinancialEvidenceAsync(transaction, source, opening, token);
        }, cancellationToken: token);

    internal static RoundAuditCapture ProtectCompletion(RoundAuditCapture capture, VideoPokerPreparedOperation operation,
        VideoPokerFinancialSnapshot previous, GameWalletSettlementReceipt receipt) =>
        RoundAuditFinancialProof.Attach(capture, VideoPokerBackend.Owner, VideoPokerBackend.FinancialEvidenceContract,
            previous.Entries.Concat(CurrentFinancialEntries(operation, receipt)).ToArray(),
            previous.Operations.Append(FinancialOperation(operation, receipt)).ToArray());

    private static RoundAuditLedgerEntry[] CurrentFinancialEntries(VideoPokerPreparedOperation operation, GameWalletSettlementReceipt receipt)
    {
        var result = new List<RoundAuditLedgerEntry>();
        var operationId = CreateLookupKey($"{operation.UserId}\n{operation.IdempotencyKey}");
        if (receipt.WagerTransactionId is { } wager)
            result.Add(new("balanceTransactions", wager, operation.UserId, "video-poker-wager", operation.RoundId,
                -operation.WagerCents, RoundAuditFinancialProof.SignedWallet, operationId, receipt.SettlementId));
        if (receipt.PayoutTransactionId is { } payout)
            result.Add(new("balanceTransactions", payout, operation.UserId, "video-poker-payout", operation.RoundId,
                operation.PayoutCents, RoundAuditFinancialProof.SignedWallet, operationId, receipt.SettlementId));
        return result.ToArray();
    }

    private static RoundAuditWalletOperation FinancialOperation(VideoPokerPreparedOperation operation, GameWalletSettlementReceipt receipt) =>
        new(VideoPokerBackend.OperationCollection, CreateLookupKey($"{operation.UserId}\n{operation.IdempotencyKey}"), operation.OutcomeSha256,
            CurrentFinancialEntries(operation, receipt).Select(entry => entry.DocumentId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt>(StringComparer.Ordinal) { [operation.UserId] = receipt });
}
