using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.FrontierFortune.Server;

public interface IFrontierRoundStore
{
    Task<FrontierReadState> ReadAsync(string account, long legacyFreeGames, CancellationToken token);
    Task<FrontierStoredRound?> FindAsync(string account, FrontierCommand command, CancellationToken token);
    Task<FrontierStoredRound> PrepareAsync(string account, FrontierCommand command, long legacyFreeGames,
        Func<FrontierAccountState, FrontierPreparedRound> prepare, CancellationToken token);
    Task<FrontierReply> FinalizeAsync(FrontierPreparedRound round, GameWalletSettlementReceipt receipt, CancellationToken token);
}

/// <summary>The durable lock, prepared outcome and command receipt survive worker/process failures.</summary>
public sealed class FrontierRoundStore(FirestoreDb database, TimeProvider clock) : IFrontierRoundStore
{
    public const string OwnerField = "frontierFortuneRoundOwner";
    public const string RoundsCollection = "frontierFortuneRounds";
    private const string StateField = "frontierFortuneStateJson";
    private const string PendingField = "frontierFortunePendingKey";
    private const string Schema = "frontier-fortune-state-v1";
    private const string OutlawSchema = "frontier-fortune-state-v2";

    public Task<FrontierReadState> ReadAsync(string account, long legacyFreeGames, CancellationToken token) =>
        database.RunTransactionAsync(async tx =>
        {
            var guard = await tx.GetSnapshotAsync(Guard(account), token);
            var fields = Fields(guard);
            var pending = Text(fields, PendingField);
            FrontierLegacyGate.Validate(fields, legacyFreeGames, pending is not null);
            if (pending is null) return new FrontierReadState(ReadState(fields), null);
            var record = await tx.GetSnapshotAsync(database.Collection(RoundsCollection).Document(pending), token);
            var saved = Decode(record, account);
            ValidateGuard(fields, saved.Prepared);
            if (saved.Final is not null) throw InvalidStorage();
            return new FrontierReadState(saved.Prepared.Before, saved.Prepared.Command);
        }, cancellationToken: token);

    public async Task<FrontierStoredRound?> FindAsync(string account, FrontierCommand command, CancellationToken token)
    {
        var document = await Record(account, command.OperationId).GetSnapshotAsync(token);
        if (!document.Exists) return null;
        var saved = Decode(document, account);
        MatchCommand(saved.Prepared, command);
        return saved;
    }

    public Task<FrontierStoredRound> PrepareAsync(string account, FrontierCommand command, long legacyFreeGames,
        Func<FrontierAccountState, FrontierPreparedRound> prepare, CancellationToken token)
    {
        FrontierPreparedRound? candidate = null;
        return database.RunTransactionAsync(async tx =>
        {
            var documents = await tx.GetAllSnapshotsAsync([Record(account, command.OperationId), Guard(account)], token);
            if (documents[0].Exists)
            {
                var existing = Decode(documents[0], account);
                MatchCommand(existing.Prepared, command);
                return existing;
            }
            var fields = Fields(documents[1]);
            FrontierLegacyGate.Validate(fields, legacyFreeGames, Text(fields, PendingField) is not null);
            if (Text(fields, PendingField) is not null)
                throw new FrontierConflictException("Retry the prepared Frontier operation before starting another action.", "frontier-operation-pending");
            var before = ReadState(fields);
            if (candidate is not null && FrontierRoundIdentity.Hash(candidate.Before) != FrontierRoundIdentity.Hash(before))
                throw new FrontierConflictException("The game changed while this operation was being prepared. Refresh and retry.");
            candidate ??= prepare(before);
            FrontierRoundPolicy.Validate(candidate);
            if (candidate.AccountId != account || FrontierRoundIdentity.Hash(candidate.Command) != FrontierRoundIdentity.Hash(command)) throw InvalidStorage();
            var saved = new FrontierStoredRound(candidate);
            tx.Create(documents[0].Reference, Encode(saved));
            tx.Set(documents[1].Reference, new Dictionary<string, object>
            {
                [OwnerField] = FrontierFortuneEngine.GameId, ["frontierFortuneSchema"] = candidate.Outlaw is null ? Schema : OutlawSchema,
                [StateField] = JsonSerializer.Serialize(before, FrontierRules.Json), [PendingField] = documents[0].Id,
                [SlotSpinGuard.ActiveAdmissionField] = candidate.AdmissionId,
                [SlotSpinGuard.PreparedAdmissionField] = candidate.AdmissionId,
            }, SetOptions.MergeAll);
            return saved;
        }, cancellationToken: token);
    }

    public Task<FrontierReply> FinalizeAsync(FrontierPreparedRound round, GameWalletSettlementReceipt receipt, CancellationToken token) =>
        database.RunTransactionAsync(async tx =>
        {
            var documents = await tx.GetAllSnapshotsAsync([Record(round.AccountId, round.Command.OperationId), Guard(round.AccountId)], token);
            var stored = Decode(documents[0], round.AccountId);
            if (stored.Prepared.OutcomeSha256 != round.OutcomeSha256) throw InvalidStorage();
            if (stored.Final is { } original) return original;
            var fields = Fields(documents[1]);
            ValidateGuard(fields, round);
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, FrontierFortuneEngine.GameId, round.AccountId,
                FrontierRoundIdentity.WalletRequest(round))) throw new GameWalletUnavailableException();
            var accepted = !receipt.InsufficientFunds && !receipt.InsufficientLegacyFreeGames && !receipt.LegacyFreeGamesCountMismatch;
            var state = accepted ? round.After : round.Before;
            var code = receipt.LegacyFreeGamesCountMismatch ? "frontier-legacy-obligation" : accepted ? null : "insufficient-slot-credits";
            var error = code is null ? null : code == "frontier-legacy-obligation" ? "Historical bonus obligations changed. Reconcile them before Frontier play."
                : "There are insufficient slot credits for that wager.";
            var reply = round.Outlaw is null ? FrontierReply.Create(state, receipt.BalanceAfterCents, accepted: accepted, code: code, error: error, action: accepted ? round.Shot : null)
                : FrontierReply.CreateOutlaw(state, receipt.BalanceAfterCents, accepted: accepted, code: code, error: error);
            var now = clock.GetUtcNow().UtcDateTime;
            tx.Set(documents[0].Reference, Encode(stored with { Final = reply }));
            tx.Set(documents[1].Reference, new Dictionary<string, object>
            {
                [StateField] = JsonSerializer.Serialize(state, FrontierRules.Json), [PendingField] = FieldValue.Delete,
                [SlotSpinGuard.ActiveAdmissionField] = "", [SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete,
            }, SetOptions.MergeAll);
            if (accepted)
            {
                var capture = RoundAuditCapture.Create("high-noon-fortune", round.AdmissionId, round.AdmissionId, 1,
                    round.StartedAtUtc, now, new { contractVersion = round.Outlaw is null ? 1 : 2, kind = "frontier-fortune", round, wallet = receipt },
                    round.Outlaw is null ? "frontier-fortune-v1" : "frontier-fortune-v2", round.Outlaw is null ? "frontier-replay-v1" : "frontier-replay-v2",
                    [new(round.AccountId, "Player", true, round.WagerCents, round.PayoutCents, round.PayoutCents - round.WagerCents)], []);
                FirestoreGameAudit.WriteObservation(tx, database, "high-noon-fortune", round.AdmissionId, 1,
                    now, new { round, wallet = receipt }, [round.AccountId], "frontier-settlement");
                FirestoreGameAudit.WriteCompletion(tx, database, capture);
                if (round.Command.Kind == "spin" || round.PayoutCents > 0)
                {
                    var analytics = GameAnalyticsOutbox.Create(new("high-noon-fortune", round.AdmissionId,
                        round.AccountId, "paid", true, now, round.StartedAtUtc, round.WagerCents, round.PayoutCents,
                        RoundsCollection, documents[0].Id));
                    if (analytics is not null) tx.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(analytics.Id), analytics.Fields);
                }
                if (round.Command.Kind == "spin")
                    FirestoreGameAudit.WriteAcceptedParticipation(tx, database, "high-noon-fortune", round.AccountId, now);
            }
            return reply;
        }, cancellationToken: token);

    private DocumentReference Guard(string account) => database.Collection(SlotSpinGuard.Collection)
        .Document(SlotSpinGuard.DocumentId(account, FrontierFortuneEngine.GameId));
    private DocumentReference Record(string account, string operation) => database.Collection(RoundsCollection)
        .Document(FrontierRoundIdentity.Key(account, operation));
    private static IReadOnlyDictionary<string, object> Fields(DocumentSnapshot snapshot) => snapshot.Exists ? snapshot.ToDictionary() : new Dictionary<string, object>();
    private static string? Text(IReadOnlyDictionary<string, object> data, string key) => data.GetValueOrDefault(key) switch
    { null or "" => null, string value => value, _ => throw InvalidStorage() };
    private static FrontierAccountState ReadState(IReadOnlyDictionary<string, object> fields)
    {
        if (Text(fields, OwnerField) is null)
        {
            if (fields.ContainsKey(StateField) || fields.ContainsKey(PendingField)) throw InvalidStorage();
            return FrontierAccountState.Empty();
        }
        if (Text(fields, OwnerField) != FrontierFortuneEngine.GameId || Text(fields, "frontierFortuneSchema") is not (Schema or OutlawSchema) ||
            Text(fields, StateField) is not { } json) throw InvalidStorage();
        var state = Deserialize<FrontierAccountState>(json);
        state.Validate();
        return state;
    }
    private static Dictionary<string, object> Encode(FrontierStoredRound saved) => new()
    {
        ["schema"] = saved.Prepared.Outlaw is null ? Schema : OutlawSchema, ["accountId"] = saved.Prepared.AccountId, ["operationId"] = saved.Prepared.Command.OperationId,
        ["outcomeSha256"] = saved.Prepared.OutcomeSha256, ["json"] = JsonSerializer.Serialize(saved, FrontierRules.Json)
    };
    private static FrontierStoredRound Decode(DocumentSnapshot document, string account)
    {
        if (!document.Exists || document.GetValue<string>("schema") is not (Schema or OutlawSchema) || document.GetValue<string>("accountId") != account)
            throw InvalidStorage();
        var saved = Deserialize<FrontierStoredRound>(document.GetValue<string>("json"));
        var round = saved.Prepared;
        if (document.GetValue<string>("schema") != (round.Outlaw is null ? Schema : OutlawSchema)) throw InvalidStorage();
        if (round.AccountId != account || document.Id != FrontierRoundIdentity.Key(account, round.Command.OperationId) ||
            document.GetValue<string>("operationId") != round.Command.OperationId || document.GetValue<string>("outcomeSha256") != round.OutcomeSha256)
            throw InvalidStorage();
        FrontierRoundPolicy.Validate(round);
        return saved;
    }
    private static T Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, FrontierRules.Json) ?? throw InvalidStorage(); }
        catch (JsonException) { throw InvalidStorage(); }
    }
    private static void MatchCommand(FrontierPreparedRound round, FrontierCommand command)
    {
        if (FrontierRoundIdentity.Hash(round.Command) != FrontierRoundIdentity.Hash(command)) throw new FrontierConflictException("That operation ID is already bound to different terms.");
    }
    private static void ValidateGuard(IReadOnlyDictionary<string, object> fields, FrontierPreparedRound round)
    {
        if (Text(fields, OwnerField) != FrontierFortuneEngine.GameId ||
            Text(fields, PendingField) != FrontierRoundIdentity.Key(round.AccountId, round.Command.OperationId) ||
            Text(fields, SlotSpinGuard.ActiveAdmissionField) != round.AdmissionId ||
            Text(fields, SlotSpinGuard.PreparedAdmissionField) != round.AdmissionId ||
            FrontierRoundIdentity.Hash(ReadState(fields)) != FrontierRoundIdentity.Hash(round.Before)) throw InvalidStorage();
    }
    private static InvalidOperationException InvalidStorage() => new("The stored Frontier round could not be verified. Retry the original operation; no new wager was accepted.");
}
