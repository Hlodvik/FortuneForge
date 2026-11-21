using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.JadeDragon.Server;

public sealed class JadeRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "jadeDragonPreparedRounds";
    private const string GameId = JadeDragonEngine.GameId;
    private const string Operations = "slotPreparedOperations";
    private const string Pending = "pending";
    private const string Settled = "settled";
    private const string Declined = "declined";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    internal Task<JadeRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(Guard(account), token);
            var pending = await ReadPendingAsync(transaction, guard, account, token);
            var before = pending?.Before ?? await JadeRoundStateCodec.ReadAsync(transaction, database,
                guard, account, wallet.LegacyFreeGamesRemaining, token);
            return JadeRoundStateReply.From(before, pending?.Request, wallet.SlotsCreditsBalanceCents);
        }, cancellationToken: token);

    internal Task<JadeStoredRound?> FindAsync(string account, JadeRoundRequest request, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var index = await transaction.GetSnapshotAsync(Operation(account, request.OperationId), token);
            if (!index.Exists) return null;
            var guard = await transaction.GetSnapshotAsync(Guard(account), token);
            return await ReadIndexedAsync(transaction, index, guard, account, request, token);
        }, cancellationToken: token);

    internal Task<JadeStoredRound> PrepareAsync(string account, JadeRoundRequest request, long legacyCount,
        CancellationToken token)
    {
        var operation = Operation(account, request.OperationId);
        var guard = Guard(account);
        var observed = GameSecurityContext.Current;
        if (observed is not null && (observed.UserId != account || observed.GameId != JadePaidRouteContract.AuditGameId))
            throw new ArgumentException("The Jade Dragon request belongs to a different account or game.");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], token);
            var current = snapshots[1];
            if (snapshots[0].Exists)
                return await ReadIndexedAsync(transaction, snapshots[0], current, account, request, token);
            if (await ReadPendingAsync(transaction, current, account, token) is { } pending)
                throw new JadeRoundConflictException("Finish the prepared Jade Dragon spin first.", pending.Request);
            // An old unkeyed admission may already have consumed a free-game unit or boost.
            // Only the original host recovery can release that admission safely.
            if (Text(current, SlotSpinGuard.ActiveAdmissionField) is not null || Text(current, SlotSpinGuard.PreparedAdmissionField) is not null)
                throw new JadeRoundConflictException("Finish the existing Jade Dragon spin first.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (current.Exists && current.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var started) &&
                started.ToDateTime().Add(Cooldown) > now)
                throw new JadeRoundCooldownException(started.ToDateTime().Add(Cooldown) - now);

            var before = await JadeRoundStateCodec.ReadAsync(transaction, database, current, account, legacyCount, token);
            var round = JadeRoundPolicy.Prepare(account, request, before, now, observed, random)
                with { ImportedLegacyState = Text(current, JadeRoundStateCodec.OwnerField) is null };
            var raw = Identity(round);
            raw["status"] = Pending;
            raw["startedAtUtc"] = Timestamp.FromDateTime(now);
            WriteFrozen(raw, round);
            var index = Identity(round);
            index["requestSha256"] = Hash(JsonSerializer.Serialize(request, Json));
            var fields = JadeRoundStateCodec.Write(before);
            fields["userId"] = account;
            fields[SlotSpinGuard.ActiveAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.PreparedAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(now);
            fields["updatedAt"] = Timestamp.FromDateTime(now);
            transaction.Create(Record(round.AdmissionId), raw);
            transaction.Create(operation, index);
            transaction.Set(guard, fields, SetOptions.MergeAll);
            return new JadeStoredRound(round, null);
        }, cancellationToken: token);
    }

    internal Task<JadeRoundResult> FinalizeAsync(JadePreparedRound round, GameWalletSettlementReceipt receipt, CancellationToken token)
    {
        ValidateWallet(round, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var guard = Guard(round.AccountId);
        var record = Record(round.AdmissionId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([record, guard], token);
            var saved = ReadRecord(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (saved.Round.Request != round.Request || saved.Round.SpinId != round.SpinId ||
                saved.Round.OutcomeSha256 != round.OutcomeSha256 || saved.Round.WagerCents != round.WagerCents ||
                saved.Round.PayoutCents != round.PayoutCents || saved.Round.SpendsLegacy != round.SpendsLegacy ||
                saved.Round.Before.LegacyRemaining != round.Before.LegacyRemaining ||
                saved.Round.ImportedLegacyState != round.ImportedLegacyState)
                throw new GameWalletUnavailableException();
            if (saved.Final is { } existing)
                return existing.Wallet == receipt ? existing : throw new GameWalletUnavailableException();

            if (Text(snapshots[0], "preparedSha256") != Hash(JsonSerializer.Serialize(round, Json)))
                throw new GameWalletUnavailableException();
            ValidateGuard(snapshots[1], saved.Round);
            var walletTime = receipt.SettledAtUtc.UtcDateTime;
            var generated = round.Outcome.Cascade?.GeneratedAtUtc ?? round.Outcome.LegacyAudit!.GeneratedAtUtc;
            var completed = walletTime < generated ? generated : walletTime;
            var settled = !IsDecline(receipt);
            var guardFields = settled ? JadeRoundStateCodec.Write(round.Progress.After) : new Dictionary<string, object>();
            if (receipt.LegacyFreeGamesCountMismatch && round.ImportedLegacyState)
                JadeRoundStateCodec.UndoImport(guardFields);
            guardFields[SlotSpinGuard.ActiveAdmissionField] = string.Empty;
            guardFields[SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete;
            guardFields["updatedAt"] = Timestamp.FromDateTime(completed);
            var reply = new JadeRoundResult(settled ? JadeRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };
            if (settled)
                JadeSpinReport.Write(transaction, database, round, reply, completed);

            var resultJson = JsonSerializer.Serialize(reply, Json);
            var data = new Dictionary<string, object>
            {
                ["status"] = settled ? Settled : Declined, ["completedAt"] = Timestamp.FromDateTime(completed),
                ["resultJson"] = resultJson, ["resultSha256"] = Hash(resultJson)
            };
            // Request IP/session evidence lives in the protected audit after settlement.
            // A declined round retains only the immutable terms needed for exact replay.
            if (round.Observation is not null)
                WriteFrozen(data, round with { Observation = null });
            transaction.Set(record, data, SetOptions.MergeAll);
            transaction.Set(guard, guardFields, SetOptions.MergeAll);
            return reply;
        }, cancellationToken: token);
    }

    private async Task<JadeStoredRound> ReadIndexedAsync(Transaction transaction, DocumentSnapshot index,
        DocumentSnapshot guard, string account, JadeRoundRequest request, CancellationToken token)
    {
        var id = CheckIdentity(index, account, request.OperationId);
        if (Text(index, "requestSha256") != Hash(JsonSerializer.Serialize(request, Json)))
            throw new JadeRoundConflictException("This operation ID was already used for different spin options.");
        var raw = await transaction.GetSnapshotAsync(Record(id), token);
        var saved = ReadRecord(raw, id, account, request.OperationId);
        if (saved.Round.Request != request) throw new GameWalletUnavailableException();
        if (saved.Final is null)
        {
            ValidateGuard(guard, saved.Round);
            JadeRoundPolicy.Validate(saved.Round);
        }
        return saved;
    }

    private async Task<JadePreparedRound?> ReadPendingAsync(Transaction transaction, DocumentSnapshot guard,
        string account, CancellationToken token)
    {
        if (Text(guard, JadeRoundStateCodec.OwnerField) != JadeRoundStateCodec.Owner ||
            !Guid.TryParseExact(Text(guard, SlotSpinGuard.PreparedAdmissionField), "N", out var pending))
            return null;
        var raw = await transaction.GetSnapshotAsync(Record(pending), token);
        var saved = ReadRecord(raw, pending, account, Text(raw, "operationId"));
        ValidateGuard(guard, saved.Round);
        if (saved.Final is not null) throw new GameWalletUnavailableException();
        return saved.Round;
    }

    private static void ValidateGuard(DocumentSnapshot guard, JadePreparedRound round)
    {
        var admission = round.AdmissionId.ToString("N");
        if (Text(guard, JadeRoundStateCodec.OwnerField) != JadeRoundStateCodec.Owner ||
            Text(guard, SlotSpinGuard.ActiveAdmissionField) != admission ||
            Text(guard, SlotSpinGuard.PreparedAdmissionField) != admission ||
            !JadeRoundStateCodec.Read(guard, round.Before.LegacyRemaining).Matches(round.Before))
            throw new GameWalletUnavailableException();
    }

    private static JadeStoredRound ReadRecord(DocumentSnapshot snapshot, Guid admission, string account, string? operation)
    {
        if (operation is null || CheckIdentity(snapshot, account, operation) != admission)
            throw new GameWalletUnavailableException();
        var round = ReadChecked<JadePreparedRound>(snapshot, "prepared");
        if (round.AdmissionId != admission || round.AccountId != account || round.Request.OperationId != operation ||
            round.SpinId.ToString("N") != Text(snapshot, "spinId"))
            throw new GameWalletUnavailableException();
        var status = Text(snapshot, "status");
        if (status == Pending) return new(round, null);
        if (status is not (Settled or Declined)) throw new GameWalletUnavailableException();
        var result = ReadChecked<JadeRoundResult>(snapshot, "result");
        if ((status == Settled) != (result.Features is not null) ||
            (status == Declined) != IsDecline(result.Wallet) || result.ChargedWagerCents != round.WagerCents)
            throw new GameWalletUnavailableException();
        ValidateWallet(round, result.Wallet);
        return new(round, result with { Round = status == Settled ? round : null });
    }

    private static void ValidateWallet(JadePreparedRound round, GameWalletSettlementReceipt wallet)
    {
        var spin = round.SpinId.ToString("N");
        var declined = IsDecline(wallet);
        var beforeLegacy = round.Before.LegacyRemaining;
        var countOkay = wallet.LegacyFreeGamesCountMismatch
            ? wallet.LegacyFreeGamesRemaining >= 0 && wallet.LegacyFreeGamesRemaining != beforeLegacy
            : wallet.LegacyFreeGamesRemaining == beforeLegacy - (!declined && round.SpendsLegacy ? 1 : 0);
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(GameId, round.AccountId, spin) ||
            !countOkay || wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents < 0 || wallet.BalanceAfterCents < 0 ||
            (wallet.InsufficientFunds ? 1 : 0) + (wallet.InsufficientLegacyFreeGames ? 1 : 0) +
                (wallet.LegacyFreeGamesCountMismatch ? 1 : 0) > 1 ||
            wallet.WagerTransactionId != (!declined && round.WagerCents > 0 ? $"{spin}-wager" : null) ||
            wallet.PayoutTransactionId != (!declined && round.PayoutCents > 0 ? $"{spin}-payout" : null) ||
            wallet.LegacyFreeGameTransactionId != (!declined && round.SpendsLegacy ? $"{spin}-free-game-use" : null) ||
            wallet.BalanceAfterWagerCents != checked(wallet.BalanceBeforeCents - (declined ? 0 : round.WagerCents)) ||
            wallet.BalanceAfterCents != checked(wallet.BalanceAfterWagerCents + (declined ? 0 : round.PayoutCents)))
            throw new GameWalletUnavailableException();
    }

    private static bool IsDecline(GameWalletSettlementReceipt wallet) =>
        wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch;

    private static T ReadChecked<T>(DocumentSnapshot snapshot, string prefix)
    {
        if (Text(snapshot, prefix + "Json") is not { } value || Text(snapshot, prefix + "Sha256") != Hash(value))
            throw new GameWalletUnavailableException();
        return JsonSerializer.Deserialize<T>(value, Json) ?? throw new GameWalletUnavailableException();
    }

    private static void WriteFrozen(IDictionary<string, object> fields, JadePreparedRound round)
    {
        var json = JsonSerializer.Serialize(round, Json);
        fields["preparedJson"] = json;
        fields["preparedSha256"] = Hash(json);
    }

    private static Dictionary<string, object> Identity(JadePreparedRound round) => new()
    {
        ["owner"] = JadeRoundStateCodec.Owner, ["userId"] = round.AccountId, ["gameId"] = GameId,
        ["admissionId"] = round.AdmissionId.ToString("N"), ["operationId"] = round.Request.OperationId,
        ["spinId"] = round.SpinId.ToString("N")
    };

    private static Guid CheckIdentity(DocumentSnapshot document, string account, string operation)
    {
        if (Text(document, "owner") != JadeRoundStateCodec.Owner || Text(document, "userId") != account ||
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
