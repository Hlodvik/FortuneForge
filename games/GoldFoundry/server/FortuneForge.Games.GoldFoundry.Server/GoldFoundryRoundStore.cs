using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.GoldFoundry.Server;

public sealed class GoldFoundryRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "goldFoundryPreparedRounds";
    private const string GameId = GoldFoundryEngine.GameId;
    private const string Operations = "slotPreparedOperations";
    private const string Pending = "pending";
    private const string Settled = "settled";
    private const string Declined = "declined";
    private const long SchemaVersion = 1;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    internal Task<GoldFoundryRoundStateReply> ReadStateAsync(string account, long? balanceCents, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(Guard(account), token);
            var pending = await ReadPendingAsync(transaction, guard, account, token);
            var before = pending?.Before ?? await GoldFoundryRoundStateCodec.ReadAsync(transaction, database, guard, account, token);
            return GoldFoundryRoundStateReply.From(before, pending?.Request, balanceCents);
        }, cancellationToken: token);

    internal Task<GoldFoundryStoredRound> PrepareOrReadAsync(string account, GoldFoundryRoundRequest request, bool allowStart,
        CancellationToken token)
    {
        var operation = Operation(account, request.OperationId);
        var guard = Guard(account);
        var observed = GameSecurityContext.Current;
        if (observed is not null && (observed.UserId != account || observed.GameId != GoldFoundryPaidRouteContract.AuditGameId))
            throw new ArgumentException("The Gold Foundry request belongs to a different account or game.");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], token);
            var index = snapshots[0];
            var current = snapshots[1];
            var requestedTerms = Hash(JsonSerializer.Serialize(request, Json));
            if (index.Exists)
                return await ReadIndexedAsync(transaction, index, current, account, request, requestedTerms, token);
            if (await ReadPendingAsync(transaction, current, account, token) is { } pending)
                throw new GoldFoundryRoundConflictException("Finish the prepared Gold Foundry spin first.", pending.Request);
            if (Text(current, SlotSpinGuard.ActiveAdmissionField) is not null || Text(current, SlotSpinGuard.PreparedAdmissionField) is not null)
                throw new GoldFoundryRoundConflictException("Finish the existing Gold Foundry spin first.");
            if (!allowStart) throw new GoldFoundryRoundPausedException();
            var now = clock.GetUtcNow().UtcDateTime;
            if (current.Exists && current.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var started) &&
                started.ToDateTime().Add(Cooldown) > now)
                throw new GoldFoundryRoundCooldownException(started.ToDateTime().Add(Cooldown) - now);

            var before = await GoldFoundryRoundStateCodec.ReadAsync(transaction, database, current, account, token);
            var round = GoldFoundryRoundPolicy.Prepare(account, request, before, now, observed, random);
            var raw = Identity(round);
            raw["status"] = Pending;
            raw["startedAtUtc"] = Timestamp.FromDateTime(now);
            WriteFrozen(raw, round);
            var key = Identity(round);
            key["requestSha256"] = requestedTerms;
            var fields = GoldFoundryRoundStateCodec.Write(before);
            fields["userId"] = account;
            fields[SlotSpinGuard.ActiveAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.PreparedAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(now);
            fields["updatedAt"] = Timestamp.FromDateTime(now);
            transaction.Create(Record(round.AdmissionId), raw);
            transaction.Create(operation, key);
            transaction.Set(guard, fields, SetOptions.MergeAll);
            return new GoldFoundryStoredRound(round, null);
        }, cancellationToken: token);
    }

    internal Task<GoldFoundryRoundResult> FinalizeAsync(GoldFoundryPreparedRound round, GameWalletSettlementReceipt receipt,
        CancellationToken token)
    {
        ValidateWallet(round, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var guard = Guard(round.AccountId);
        var record = Record(round.AdmissionId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([record, guard], token);
            var saved = ReadRecord(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (saved.Round.Request != round.Request || saved.Round.Outcome.SpinId != round.Outcome.SpinId ||
                saved.Round.OutcomeSha256 != round.OutcomeSha256 || saved.Round.WagerCents != round.WagerCents ||
                saved.Round.PayoutCents != round.PayoutCents)
                throw new GameWalletUnavailableException();
            if (saved.Final is { } existing)
                return existing.Wallet == receipt ? existing : throw new GameWalletUnavailableException();
            if (Text(snapshots[0], "preparedSha256") != Hash(JsonSerializer.Serialize(round, Json)))
                throw new GameWalletUnavailableException();
            ValidateGuard(snapshots[1], saved.Round);

            var walletTime = receipt.SettledAtUtc.UtcDateTime;
            var generated = round.Outcome.EngineAudit.GeneratedAtUtc;
            var completed = walletTime < generated ? generated : walletTime;
            var settled = !receipt.InsufficientFunds;
            var guardFields = settled ? GoldFoundryRoundStateCodec.Write(round.After) : new Dictionary<string, object>();
            guardFields[SlotSpinGuard.ActiveAdmissionField] = string.Empty;
            guardFields[SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete;
            guardFields["updatedAt"] = Timestamp.FromDateTime(completed);
            var reply = new GoldFoundryRoundResult(settled
                ? GoldFoundryRoundStateReply.From(round.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };
            if (settled)
                GoldFoundrySpinReport.Write(transaction, database, round, reply, completed);

            var resultJson = JsonSerializer.Serialize(reply, Json);
            var data = new Dictionary<string, object>
            {
                ["status"] = settled ? Settled : Declined, ["completedAt"] = Timestamp.FromDateTime(completed),
                ["resultJson"] = resultJson, ["resultSha256"] = Hash(resultJson)
            };
            if (round.Observation is not null)
                WriteFrozen(data, round with { Observation = null });
            transaction.Set(record, data, SetOptions.MergeAll);
            transaction.Set(guard, guardFields, SetOptions.MergeAll);
            return reply;
        }, cancellationToken: token);
    }

    private async Task<GoldFoundryStoredRound> ReadIndexedAsync(Transaction transaction, DocumentSnapshot index,
        DocumentSnapshot guard, string account, GoldFoundryRoundRequest request, string requestedTerms, CancellationToken token)
    {
        var id = CheckIdentity(index, account, request.OperationId);
        if (Text(index, "requestSha256") != requestedTerms)
            throw new GoldFoundryRoundConflictException("This operation ID was already used for different spin options.");
        var raw = await transaction.GetSnapshotAsync(Record(id), token);
        var saved = ReadRecord(raw, id, account, request.OperationId);
        if (saved.Round.Request != request) throw new GameWalletUnavailableException();
        if (saved.Final is null)
        {
            ValidateGuard(guard, saved.Round);
            GoldFoundryRoundPolicy.Validate(saved.Round);
        }
        return saved;
    }

    private async Task<GoldFoundryPreparedRound?> ReadPendingAsync(Transaction transaction, DocumentSnapshot guard,
        string account, CancellationToken token)
    {
        if (Text(guard, GoldFoundryRoundStateCodec.OwnerField) != GoldFoundryRoundStateCodec.Owner ||
            !Guid.TryParseExact(Text(guard, SlotSpinGuard.PreparedAdmissionField), "N", out var pending))
            return null;
        var raw = await transaction.GetSnapshotAsync(Record(pending), token);
        var saved = ReadRecord(raw, pending, account, Text(raw, "operationId"));
        ValidateGuard(guard, saved.Round);
        if (saved.Final is not null) throw new GameWalletUnavailableException();
        return saved.Round;
    }

    private static void ValidateGuard(DocumentSnapshot guard, GoldFoundryPreparedRound round)
    {
        var admission = round.AdmissionId.ToString("N");
        if (Text(guard, GoldFoundryRoundStateCodec.OwnerField) != GoldFoundryRoundStateCodec.Owner ||
            Text(guard, SlotSpinGuard.ActiveAdmissionField) != admission ||
            Text(guard, SlotSpinGuard.PreparedAdmissionField) != admission ||
            GoldFoundryRoundStateCodec.Read(guard) != round.Before)
            throw new GameWalletUnavailableException();
    }

    private static GoldFoundryStoredRound ReadRecord(DocumentSnapshot snapshot, Guid admission, string account, string? operation)
    {
        if (operation is null || CheckIdentity(snapshot, account, operation) != admission)
            throw new GameWalletUnavailableException();
        var round = ReadChecked<GoldFoundryPreparedRound>(snapshot, "prepared");
        if (round.AdmissionId != admission || round.AccountId != account || round.Request.OperationId != operation ||
            round.Outcome.SpinId.ToString("N") != Text(snapshot, "spinId"))
            throw new GameWalletUnavailableException();
        var status = Text(snapshot, "status");
        if (status == Pending) return new(round, null);
        if (status is not (Settled or Declined)) throw new GameWalletUnavailableException();
        var result = ReadChecked<GoldFoundryRoundResult>(snapshot, "result");
        if ((status == Settled) != (result.Features is not null) ||
            (status == Declined) != result.Wallet.InsufficientFunds || result.ChargedWagerCents != round.WagerCents)
            throw new GameWalletUnavailableException();
        ValidateWallet(round, result.Wallet);
        return new(round, result with { Round = status == Settled ? round : null });
    }

    private static void ValidateWallet(GoldFoundryPreparedRound round, GameWalletSettlementReceipt wallet)
    {
        var spin = round.Outcome.SpinId.ToString("N");
        var approved = !wallet.InsufficientFunds;
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(GameId, round.AccountId, spin) ||
            wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch || wallet.LegacyFreeGameTransactionId is not null ||
            wallet.LegacyFreeGamesRemaining < 0 ||
            wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents < 0 || wallet.BalanceAfterCents < 0 ||
            wallet.InsufficientFunds != (wallet.BalanceBeforeCents < round.WagerCents) ||
            wallet.WagerTransactionId != (approved ? $"{spin}-wager" : null) ||
            wallet.PayoutTransactionId != (approved && round.PayoutCents > 0 ? $"{spin}-payout" : null) ||
            wallet.BalanceAfterWagerCents != checked(wallet.BalanceBeforeCents - (approved ? round.WagerCents : 0)) ||
            wallet.BalanceAfterCents != checked(wallet.BalanceAfterWagerCents + (approved ? round.PayoutCents : 0)))
            throw new GameWalletUnavailableException();
    }

    private static T ReadChecked<T>(DocumentSnapshot snapshot, string prefix)
    {
        if (Text(snapshot, prefix + "Json") is not { } value || Text(snapshot, prefix + "Sha256") != Hash(value))
            throw new GameWalletUnavailableException();
        return JsonSerializer.Deserialize<T>(value, Json) ?? throw new GameWalletUnavailableException();
    }

    private static void WriteFrozen(IDictionary<string, object> fields, GoldFoundryPreparedRound round)
    {
        var json = JsonSerializer.Serialize(round, Json);
        fields["preparedJson"] = json;
        fields["preparedSha256"] = Hash(json);
    }

    private static Dictionary<string, object> Identity(GoldFoundryPreparedRound round) => new()
    {
        ["owner"] = GoldFoundryRoundStateCodec.Owner, ["schemaVersion"] = SchemaVersion,
        ["userId"] = round.AccountId, ["gameId"] = GameId,
        ["admissionId"] = round.AdmissionId.ToString("N"), ["operationId"] = round.Request.OperationId,
        ["spinId"] = round.Outcome.SpinId.ToString("N")
    };

    private static Guid CheckIdentity(DocumentSnapshot document, string account, string operation)
    {
        if (Text(document, "owner") != GoldFoundryRoundStateCodec.Owner || Text(document, "userId") != account ||
            !document.TryGetValue<long>("schemaVersion", out var schema) || schema != SchemaVersion ||
            Text(document, "gameId") != GameId || Text(document, "operationId") != operation ||
            !Guid.TryParseExact(Text(document, "admissionId"), "N", out var id) || id == Guid.Empty)
            throw new GameWalletUnavailableException();
        return id;
    }

    private static string Hash(string value) => GameSecurityEventDocuments.Hash(value);
    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    private DocumentReference Guard(string account) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, GameId));
    private DocumentReference Record(Guid admission) => database.Collection(RawCollectionName).Document(admission.ToString("N"));
    private DocumentReference Operation(string account, string operation) =>
        database.Collection(Operations).Document(Hash($"{GameId}\n{account}\n{operation}"));
}
