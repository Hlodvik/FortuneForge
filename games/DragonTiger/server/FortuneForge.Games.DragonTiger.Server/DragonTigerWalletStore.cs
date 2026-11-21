using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DragonTiger.Server;

/// <summary>The package freezes its deal before asking the account service to settle it.</summary>
public sealed class DragonTigerWalletStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts, bool allowNewRounds = true) : IDragonTigerStore
{
    private static string GameId => DragonTigerModule.Descriptor.Id;
    private readonly DragonTigerFirestoreStore legacy = new(database);
    private sealed record Intent(string Key, DragonTigerStoredRound Round, string[] Shoe, DateTimeOffset StartedAtUtc, GameRequestObservation? Observation = null);
    private sealed record Saved(Intent Intent, string OutcomeSha256, GameWalletSettlementReceipt? Receipt);

    public async Task<DragonTigerStoreResult> StartAsync(string userId, string idempotencyKey, DragonTigerWagers wagers,
        IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        DragonTigerMoney.Validate(wagers);
        DragonTigerShoe.Validate(shuffledShoe);
        if (!ValidObservation(GameSecurityContext.Current, userId)) throw new GameWalletUnavailableException();
        var id = DragonTigerService.LookupKey(userId, idempotencyKey);
        var document = await Round(id).GetSnapshotAsync(cancellationToken);
        if (document.Exists && document.GetValue<long>("schemaVersion") == 1)
        {
            var old = await legacy.StartAsync(userId, idempotencyKey, wagers, shuffledShoe, nowUtc, cancellationToken);
            return old with { BalanceCents = await Balance(userId, cancellationToken) };
        }
        var saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(id), Shoe(userId)], cancellationToken);
            if (snapshots[0].Exists)
            {
                var existing = Read(snapshots[0]);
                if (existing.Intent.Round.UserId != userId || existing.Intent.Key != idempotencyKey || existing.Intent.Round.Wagers != wagers)
                    throw new DragonTigerRoundConflictException();
                return existing;
            }
            await legacy.EnsureNoOrphansAsync(transaction, id, cancellationToken);
            if (!allowNewRounds) throw new DragonTigerDisabledException();
            var shoe = snapshots[1];
            if (Text(shoe, "pendingRoundId") is not null) throw new DragonTigerRoundConflictException();
            var cards = shoe.Exists ? ReadShoe(shoe, userId) : shuffledShoe;
            var index = shoe.Exists ? checked((int)shoe.GetValue<long>("nextIndex")) : 0;
            if (index >= DragonTigerRules.ReshuffleAt) { cards = shuffledShoe; index = 0; }
            var result = DragonTigerRules.Deal(cards.Skip(index).Take(2).ToArray());
            var round = new DragonTigerStoredRound(id, userId, wagers, result, index + 2, DragonTigerRules.ShoeSize - index - 2);
            var intent = new Intent(idempotencyKey, round, cards.Select(CardCode.Format).ToArray(), nowUtc, GameSecurityContext.Current);
            var json = JsonSerializer.Serialize(intent);
            var digest = Hash(json);
            transaction.Create(Round(id), new Dictionary<string, object>
            {
                ["schemaVersion"] = 2L, ["roundId"] = id, ["userId"] = userId,
                ["status"] = "pending", ["intentJson"] = json, ["intentSha256"] = digest,
            });
            transaction.Set(Shoe(userId), new Dictionary<string, object>
            {
                ["userId"] = userId, ["schemaVersion"] = 1L, ["pendingRoundId"] = id,
                ["cards"] = intent.Shoe, ["nextIndex"] = (long)index,
            }, SetOptions.MergeAll);
            return new Saved(intent, digest, null);
        }, cancellationToken: cancellationToken);
        return await Resume(saved, cancellationToken);
    }

    public async Task<DragonTigerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshot = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || Text(snapshot, "userId") != userId) return null;
        if (snapshot.GetValue<long>("schemaVersion") == 1)
        {
            var old = await legacy.GetAsync(userId, roundId, cancellationToken);
            return old is null ? null : old with { BalanceCents = await Balance(userId, cancellationToken) };
        }
        return await Resume(Read(snapshot), cancellationToken);
    }

    public async Task<DragonTigerStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken)
    {
        var shoe = await Shoe(userId).GetSnapshotAsync(cancellationToken);
        if (!shoe.Exists) return null;
        _ = ReadShoe(shoe, userId);
        var id = Text(shoe, "pendingRoundId") ?? Text(shoe, "latestRoundId");
        return id is null ? null : await GetAsync(userId, id, cancellationToken) ?? throw new GameWalletUnavailableException();
    }

    private async Task<DragonTigerStoreResult> Resume(Saved saved, CancellationToken token)
    {
        var intent = saved.Intent;
        var digest = saved.OutcomeSha256;
        if (saved.Receipt is { } prior) return Result(intent, prior);
        saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(intent.Round.RoundId), Shoe(intent.Round.UserId)], token);
            var current = Read(snapshots[0]);
            if (current.OutcomeSha256 != digest || JsonSerializer.Serialize(current.Intent) != JsonSerializer.Serialize(intent)) throw new GameWalletUnavailableException();
            if (current.Receipt is null)
            {
                var shoe = snapshots[1];
                if (Text(shoe, "pendingRoundId") != intent.Round.RoundId ||
                    shoe.GetValue<long>("nextIndex") != intent.Round.ShoeCardsUsed - 2 ||
                    !ReadShoe(shoe, intent.Round.UserId).Select(CardCode.Format).SequenceEqual(intent.Shoe)) throw new GameWalletUnavailableException();
            }
            return current;
        }, cancellationToken: token);
        if (saved.Receipt is { } confirmed) return Result(intent, confirmed);
        var receipt = await wallet.SettleAsync(intent.Round.UserId,
            Settlement(intent, digest), token);
        receipt = receipt with { WasAlreadySettled = false };
        ValidateReceipt(intent, digest, receipt);
        var final = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(intent.Round.RoundId), Shoe(intent.Round.UserId)], token);
            var current = Read(snapshots[0]);
            if (current.OutcomeSha256 != digest || JsonSerializer.Serialize(current.Intent) != JsonSerializer.Serialize(intent)) throw new GameWalletUnavailableException();
            if (current.Receipt is { } existing)
                return existing == receipt ? existing : throw new GameWalletUnavailableException();
            if (Text(snapshots[1], "pendingRoundId") != intent.Round.RoundId) throw new GameWalletUnavailableException();
            var declined = receipt.InsufficientFunds;
            transaction.Set(Round(intent.Round.RoundId), new Dictionary<string, object>
            {
                ["status"] = declined ? "declined" : "settled", ["receiptJson"] = JsonSerializer.Serialize(receipt),
                ["receiptSha256"] = Hash(JsonSerializer.Serialize(receipt)),
            }, SetOptions.MergeAll);
            var fields = new Dictionary<string, object> { ["pendingRoundId"] = FieldValue.Delete };
            if (!declined)
            {
                fields["nextIndex"] = (long)intent.Round.ShoeCardsUsed;
                fields["latestRoundId"] = intent.Round.RoundId;
                Report(transaction, intent, receipt);
            }
            transaction.Set(Shoe(intent.Round.UserId), fields, SetOptions.MergeAll);
            return receipt;
        }, cancellationToken: token);
        return Result(intent, final);
    }

    private void Report(Transaction transaction, Intent intent, GameWalletSettlementReceipt receipt)
    {
        using var originalRequest = intent.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        var round = intent.Round;
        var completed = receipt.SettledAtUtc < intent.StartedAtUtc ? intent.StartedAtUtc : receipt.SettledAtUtc;
        var report = SinglePlayerRoundRecord.Create(GameId, round.RoundId, round.UserId, round.Wagers.TotalCents,
            Return(round), intent.StartedAtUtc.UtcDateTime, completed.UtcDateTime, "dragonTigerRounds", round.RoundId,
            round, "dragon-tiger-cards-and-wagers-v1", DragonTigerOutcomeAudit.Verify(round, round.Wagers.TotalCents, Return(round)), DragonTigerOutcomeAudit.Version);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, report.Audit);
        FirestoreGameAudit.WriteObservation(transaction, database, GameId, round.RoundId, round.ShoeCardsUsed,
            completed.UtcDateTime, new { stored = round, shoe = intent.Shoe }, [round.UserId], "settlement");
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, GameId, round.UserId, completed.UtcDateTime);
    }

    private static Saved Read(DocumentSnapshot snapshot)
    {
        try { return ReadCore(snapshot); }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or NullReferenceException or InvalidOperationException)
        { throw new GameWalletUnavailableException(error); }
    }
    private static Saved ReadCore(DocumentSnapshot snapshot)
    {
        if (snapshot.GetValue<long>("schemaVersion") != 2) throw new GameWalletUnavailableException();
        var json = Text(snapshot, "intentJson") ?? throw new GameWalletUnavailableException();
        var digest = Hash(json);
        if (Text(snapshot, "intentSha256") != digest) throw new GameWalletUnavailableException();
        var intent = JsonSerializer.Deserialize<Intent>(json) ?? throw new GameWalletUnavailableException();
        var round = intent.Round;
        if (snapshot.Id != round.RoundId || Text(snapshot, "roundId") != round.RoundId || Text(snapshot, "userId") != round.UserId ||
            intent.StartedAtUtc == default || intent.StartedAtUtc.Offset != TimeSpan.Zero ||
            DragonTigerService.LookupKey(round.UserId, intent.Key) != round.RoundId ||
            !ValidObservation(intent.Observation, round.UserId) ||
            !DragonTigerOutcomeAudit.Verify(round, round.Wagers.TotalCents, Return(round))) throw new GameWalletUnavailableException();
        var cards = intent.Shoe.Select(CardCode.Parse).ToArray();
        DragonTigerShoe.Validate(cards);
        var expected = DragonTigerRules.Deal(cards.Skip(round.ShoeCardsUsed - 2).Take(2).ToArray());
        if (expected != round.Round) throw new GameWalletUnavailableException();
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
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, GameId, intent.Round.UserId, Settlement(intent, digest)))
            throw new GameWalletUnavailableException();
    }
    private static GameWalletSettlementRequest Settlement(Intent intent, string digest) =>
        new(intent.Round.RoundId, digest,
            intent.Round.Wagers.TotalCents, Return(intent.Round));

    private static DragonTigerStoreResult Result(Intent intent, GameWalletSettlementReceipt receipt) => receipt.InsufficientFunds
        ? throw new DragonTigerInsufficientCreditsException(receipt.BalanceBeforeCents, intent.Round.Wagers.TotalCents)
        : new(intent.Round, receipt.BalanceAfterCents);
    private static IReadOnlyList<PlayingCard> ReadShoe(DocumentSnapshot snapshot, string account)
    {
        try { return ReadShoeCore(snapshot, account); }
        catch (Exception error) when (error is ArgumentException or OverflowException or InvalidOperationException)
        { throw new GameWalletUnavailableException(error); }
    }
    private static IReadOnlyList<PlayingCard> ReadShoeCore(DocumentSnapshot snapshot, string account)
    {
        if (Text(snapshot, "userId") != account || snapshot.GetValue<long>("schemaVersion") != 1) throw new GameWalletUnavailableException();
        var index = snapshot.GetValue<long>("nextIndex");
        if (index < 0 || index > DragonTigerRules.ReshuffleAt || index % 2 != 0) throw new GameWalletUnavailableException();
        var cards = snapshot.GetValue<string[]>("cards").Select(CardCode.Parse).ToArray();
        DragonTigerShoe.Validate(cards);
        return cards;
    }
    private async Task<long> Balance(string account, CancellationToken token) =>
        (await accounts.GetStateAsync(account, token)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();
    private static long Return(DragonTigerStoredRound round) => DragonTigerRules.Settle(round.Wagers, round.Round).Sum(bet => bet.TotalReturnCents);
    private static bool ValidObservation(GameRequestObservation? observation, string account) => observation is null ||
        observation.UserId == account && observation.GameId == GameId &&
        observation.Path.StartsWith("/api/games/" + GameId + "/", StringComparison.Ordinal);
    private static string Hash(string value) => GameSecurityEventDocuments.Hash(value);
    private static string? Text(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    private DocumentReference Round(string id) => database.Collection("dragonTigerRounds").Document(id);
    private DocumentReference Shoe(string account) => database.Collection("dragonTigerShoes").Document(DragonTigerService.Hash(account));
}

public sealed class DragonTigerDisabledException() : Exception("Dragon Tiger is not accepting new wagers. Existing rounds can still be restored.");
