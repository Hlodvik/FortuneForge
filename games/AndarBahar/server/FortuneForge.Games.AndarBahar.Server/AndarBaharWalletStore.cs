using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.AndarBahar.Server;

/// <summary>Persists the package's deal before requesting an idempotent account-wallet settlement.</summary>
public sealed class AndarBaharWalletStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts) : IAndarBaharStore
{
    private static string GameId => AndarBaharModule.Descriptor.Id;
    private readonly AndarBaharFirestoreStore legacy = new(database);
    private sealed record Intent(PersistedAndarBaharRound Deal, GameRequestObservation? Observation = null);
    private sealed record Saved(Intent Intent, string OutcomeSha256, GameWalletSettlementReceipt? Receipt);

    public async Task<long> BalanceAsync(string userId, CancellationToken cancellationToken) =>
        (await accounts.GetStateAsync(userId, cancellationToken)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();

    public async Task<AndarBaharStoreResult> StartAsync(string userId, string key, AndarBaharSide side, long stakeCents,
        IReadOnlyList<PlayingCard> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken, bool allowNewRounds = true)
    {
        AndarBaharMoney.Validate(side, stakeCents);
        if (!ValidObservation(GameSecurityContext.Current, userId)) throw new GameWalletUnavailableException();
        var id = AndarBaharService.LookupKey(userId, key);
        var document = await Round(id).GetSnapshotAsync(cancellationToken);
        if (document.Exists && document.GetValue<long>("schemaVersion") is 1 or 2)
        {
            var old = await legacy.StartAsync(userId, key, side, stakeCents, shuffledDeck, nowUtc, cancellationToken, false);
            return old with { BalanceCents = await BalanceAsync(userId, cancellationToken) };
        }
        var saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(id), Account(userId)], cancellationToken);
            if (snapshots[0].Exists)
            {
                var existing = Read(snapshots[0]);
                var prior = AndarBaharPersistence.Decode(existing.Intent.Deal);
                if (prior.UserId != userId || prior.IdempotencyKey != key || prior.Side != side || prior.StakeCents != stakeCents)
                    throw new AndarBaharRoundConflictException();
                return existing;
            }
            await legacy.EnsureNoOrphansAsync(transaction, id, cancellationToken);
            if (!allowNewRounds) throw new AndarBaharDisabledException();
            AndarBaharMoney.ValidateNewStake(stakeCents);
            if (snapshots[1].Exists && (Text(snapshots[1], "userId") != userId || snapshots[1].GetValue<long>("schemaVersion") != 1))
                throw new GameWalletUnavailableException();
            if (Text(snapshots[1], "pendingRoundId") is not null) throw new AndarBaharRoundConflictException();
            var deal = AndarBaharRules.Deal(shuffledDeck);
            var returned = AndarBaharRules.Settle(side, stakeCents, deal).TotalReturnCents;
            // The frozen deal contains no account balance; only the wallet receipt supplies those values.
            var stored = new AndarBaharStoredRound(id, userId, key, side, stakeCents, deal, 0, returned, nowUtc);
            var intent = new Intent(AndarBaharPersistence.Encode(stored), GameSecurityContext.Current);
            var json = JsonSerializer.Serialize(intent);
            var digest = Hash(json);
            transaction.Create(Round(id), new Dictionary<string, object>
            {
                ["schemaVersion"] = 3L, ["roundId"] = id, ["userId"] = userId,
                ["status"] = "pending", ["intentJson"] = json, ["intentSha256"] = digest,
            });
            transaction.Set(Account(userId), new Dictionary<string, object>
            {
                ["userId"] = userId, ["schemaVersion"] = 1L, ["pendingRoundId"] = id,
            }, SetOptions.MergeAll);
            return new Saved(intent, digest, null);
        }, cancellationToken: cancellationToken);
        return await Resume(saved, cancellationToken);
    }

    public async Task<AndarBaharStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshot = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || Text(snapshot, "userId") != userId) return null;
        if (snapshot.GetValue<long>("schemaVersion") is 1 or 2)
        {
            var old = await legacy.GetAsync(userId, roundId, cancellationToken);
            return old is null ? null : old with { BalanceCents = await BalanceAsync(userId, cancellationToken) };
        }
        return await Resume(Read(snapshot), cancellationToken);
    }

    public async Task<AndarBaharStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await Account(userId).GetSnapshotAsync(cancellationToken);
        if (!account.Exists) return null;
        if (Text(account, "userId") != userId || account.GetValue<long>("schemaVersion") != 1) throw new GameWalletUnavailableException();
        var id = Text(account, "pendingRoundId") ?? Text(account, "roundId");
        return id is null ? null : await GetAsync(userId, id, cancellationToken) ?? throw new GameWalletUnavailableException();
    }

    private async Task<AndarBaharStoreResult> Resume(Saved saved, CancellationToken token)
    {
        var intent = saved.Intent;
        var digest = saved.OutcomeSha256;
        if (saved.Receipt is { } prior) return Result(intent, prior);
        saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(intent.Deal.RoundId), Account(intent.Deal.UserId)], token);
            var current = Read(snapshots[0]);
            if (current.OutcomeSha256 != digest || JsonSerializer.Serialize(current.Intent) != JsonSerializer.Serialize(intent)) throw new GameWalletUnavailableException();
            if (current.Receipt is null && (Text(snapshots[1], "pendingRoundId") != intent.Deal.RoundId ||
                Text(snapshots[1], "userId") != intent.Deal.UserId || snapshots[1].GetValue<long>("schemaVersion") != 1)) throw new GameWalletUnavailableException();
            return current;
        }, cancellationToken: token);
        if (saved.Receipt is { } confirmed) return Result(intent, confirmed);
        var deal = intent.Deal;
        var receipt = await wallet.SettleAsync(deal.UserId,
            Settlement(intent, digest), token);
        receipt = receipt with { WasAlreadySettled = false };
        ValidateReceipt(intent, digest, receipt);
        var final = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(deal.RoundId), Account(deal.UserId)], token);
            var current = Read(snapshots[0]);
            if (current.OutcomeSha256 != digest || JsonSerializer.Serialize(current.Intent) != JsonSerializer.Serialize(intent)) throw new GameWalletUnavailableException();
            if (current.Receipt is { } existing)
                return existing == receipt ? existing : throw new GameWalletUnavailableException();
            if (Text(snapshots[1], "pendingRoundId") != deal.RoundId) throw new GameWalletUnavailableException();
            var declined = receipt.InsufficientFunds;
            transaction.Set(Round(deal.RoundId), new Dictionary<string, object>
            {
                ["status"] = declined ? "declined" : "settled", ["receiptJson"] = JsonSerializer.Serialize(receipt),
                ["receiptSha256"] = Hash(JsonSerializer.Serialize(receipt)),
            }, SetOptions.MergeAll);
            var fields = new Dictionary<string, object> { ["pendingRoundId"] = FieldValue.Delete };
            if (!declined)
            {
                fields["roundId"] = deal.RoundId;
                fields["updatedAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime);
                Report(transaction, intent, receipt);
            }
            transaction.Set(Account(deal.UserId), fields, SetOptions.MergeAll);
            return receipt;
        }, cancellationToken: token);
        return Result(intent, final);
    }

    private void Report(Transaction transaction, Intent intent, GameWalletSettlementReceipt receipt)
    {
        using var originalRequest = intent.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        var result = Result(intent, receipt);
        var stored = result.Round;
        var completed = receipt.SettledAtUtc < stored.CreatedAtUtc ? stored.CreatedAtUtc : receipt.SettledAtUtc;
        var report = SinglePlayerRoundRecord.Create(GameId, stored.RoundId, stored.UserId, stored.StakeCents, intent.Deal.TotalReturnCents,
            stored.CreatedAtUtc.UtcDateTime, completed.UtcDateTime, "andarBaharRounds", stored.RoundId, stored,
            "andar-bahar-deal-sequence-v1", AndarBaharOutcomeAudit.Verify(stored, intent.Deal.TotalReturnCents), AndarBaharOutcomeAudit.Version);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, report.Audit);
        var history = database.Collection("cardGameResults").Document("andar-bahar-" + stored.RoundId);
        var historyFields = AndarBaharFirestoreStore.History(history.Id, stored);
        historyFields["completedAt"] = Timestamp.FromDateTime(completed.UtcDateTime);
        transaction.Create(history, historyFields);
        FirestoreGameAudit.WriteObservation(transaction, database, GameId, stored.RoundId, 1,
            completed.UtcDateTime, stored, [stored.UserId], "settlement");
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, GameId, stored.UserId, completed.UtcDateTime);
    }

    private static Saved Read(DocumentSnapshot snapshot)
    {
        try { return ReadCore(snapshot); }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or NullReferenceException or InvalidOperationException)
        { throw new GameWalletUnavailableException(error); }
    }
    private static Saved ReadCore(DocumentSnapshot snapshot)
    {
        if (snapshot.GetValue<long>("schemaVersion") != 3) throw new GameWalletUnavailableException();
        var json = Text(snapshot, "intentJson") ?? throw new GameWalletUnavailableException();
        var digest = Hash(json);
        if (Text(snapshot, "intentSha256") != digest) throw new GameWalletUnavailableException();
        var intent = JsonSerializer.Deserialize<Intent>(json) ?? throw new GameWalletUnavailableException();
        var deal = intent.Deal;
        _ = AndarBaharPersistence.Decode(deal);
        if (snapshot.Id != deal.RoundId || Text(snapshot, "roundId") != deal.RoundId || Text(snapshot, "userId") != deal.UserId ||
            deal.BalanceAfterWagerCents != 0 || deal.BalanceAfterSettlementCents != deal.TotalReturnCents || deal.CreatedAtUtc.Offset != TimeSpan.Zero ||
            !ValidObservation(intent.Observation, deal.UserId))
            throw new GameWalletUnavailableException();
        var state = Text(snapshot, "status");
        if (state == "pending") return new(intent, digest, null);
        if (state is not ("settled" or "declined")) throw new GameWalletUnavailableException();
        var value = Text(snapshot, "receiptJson") ?? throw new GameWalletUnavailableException();
        if (Text(snapshot, "receiptSha256") != Hash(value)) throw new GameWalletUnavailableException();
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(value) ?? throw new GameWalletUnavailableException();
        ValidateReceipt(intent, digest, receipt);
        if ((state == "declined") != receipt.InsufficientFunds) throw new GameWalletUnavailableException();
        return new(intent, digest, receipt);
    }

    private static void ValidateReceipt(Intent intent, string digest, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, GameId, intent.Deal.UserId, Settlement(intent, digest)))
            throw new GameWalletUnavailableException();
    }
    private static GameWalletSettlementRequest Settlement(Intent intent, string digest) =>
        new(intent.Deal.RoundId, digest,
            intent.Deal.StakeCents, intent.Deal.TotalReturnCents);

    private static AndarBaharStoreResult Result(Intent intent, GameWalletSettlementReceipt receipt)
    {
        if (receipt.InsufficientFunds) throw new AndarBaharInsufficientCreditsException(receipt.BalanceBeforeCents, intent.Deal.StakeCents);
        var stored = AndarBaharPersistence.Decode(intent.Deal) with
        { BalanceAfterWagerCents = receipt.BalanceAfterWagerCents, BalanceAfterSettlementCents = receipt.BalanceAfterCents };
        return new(stored, receipt.BalanceAfterCents);
    }
    private static bool ValidObservation(GameRequestObservation? observation, string account) => observation is null ||
        observation.UserId == account && observation.GameId == GameId &&
        observation.Path.StartsWith("/api/games/" + GameId + "/", StringComparison.Ordinal);
    private static string Hash(string value) => GameSecurityEventDocuments.Hash(value);
    private static string? Text(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    private DocumentReference Round(string id) => database.Collection("andarBaharRounds").Document(id);
    private DocumentReference Account(string account) => database.Collection("andarBaharAccounts").Document(AndarBaharService.Hash(account));
}
