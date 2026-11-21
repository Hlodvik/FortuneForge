using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Craps;

public sealed partial class FirestorePaidCrapsStore
{
    private async Task<CrapsRoundResponse> ExecuteAsync(string userId, string operationId, string fingerprint, Guid roundId, string kind,
        Func<PaidCrapsRound?, Guid?, DateTime, PaidCrapsRound> prepare, CancellationToken token)
    {
        var operation = Operation(userId, operationId);
        var prepared = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, Player(userId), Round(roundId)], token);
            if (snapshots[0].Exists)
            {
                MatchOperation(snapshots[0], userId, fingerprint);
                if (!snapshots[0].ContainsField("walletStatus")) return new PreparedOrReplay(null, Replay(snapshots[0], userId, fingerprint));
                var pending = ReadPrepared(snapshots[0], userId);
                return new PreparedOrReplay(pending, null);
            }
            if (PendingOperation(snapshots[1], userId) is not null)
                throw new CrapsPaidException("craps-action-pending", "Recover the pending Craps action before submitting another one.", 409);
            var saved = snapshots[2].Exists ? ReadRound(snapshots[2], userId) : null;
            await RejectLegacyEvidenceAsync(transaction, userId, operationId, roundId, saved, token);
            var now = DateTime.UtcNow;
            var next = prepare(saved, ActiveId(snapshots[1], userId), now); ValidateStoredRound(next);
            var pendingAction = new PreparedCrapsAction(userId, operationId, fingerprint, kind, saved?.Revision ?? -1,
                saved is null ? null : Hash(JsonSerializer.Serialize(saved, RoundJson)), saved?.TotalWagerCents ?? 0, saved?.TotalReturnCents ?? 0, next,
                GameSecurityContext.Current is { } observation ? observation with { SessionId = null, IpAddress = null, RequestJson = null, Geo = null } : null);
            var fields = new Dictionary<string, object> { ["userId"] = userId, ["operationId"] = operationId, ["fingerprint"] = fingerprint,
                ["roundId"] = roundId.ToString("N"), ["kind"] = kind, ["preparedJson"] = JsonSerializer.Serialize(pendingAction, RoundJson),
                ["walletStatus"] = "pending", ["schemaVersion"] = 2L };
            transaction.Create(operation, fields);
            transaction.Set(Player(userId), new Dictionary<string, object> { ["userId"] = userId, ["pendingOperationId"] = operationId }, SetOptions.MergeAll);
            FirestoreGameAudit.WriteObservation(transaction, database, "craps", roundId.ToString("N") + ":prepared:" + operationId, next.Revision,
                now, new { pendingAction, phase = "prepared" }, [userId], "prepared");
            return new PreparedOrReplay(pendingAction, null);
        }, cancellationToken: token);
        return prepared.Replay ?? await CompleteAsync(prepared.Prepared!, token);
    }

    private async Task RejectLegacyEvidenceAsync(Transaction transaction, string userId, string operationId, Guid roundId,
        PaidCrapsRound? saved, CancellationToken token)
    {
        if (saved?.Actions?.Any(action => action.OperationId == operationId) == true)
            throw new InvalidOperationException("A Craps action survives without its immutable operation receipt.");
        var ledgers = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions")
            .WhereEqualTo("userId", userId).WhereEqualTo("idempotencyKey", operationId), token);
        if (ledgers.Documents.Any(ledger => ledger.Id.StartsWith("craps-", StringComparison.Ordinal) ||
            ledger.TryGetValue<string>("type", out var type) && type.StartsWith("craps-", StringComparison.Ordinal)))
            throw new InvalidOperationException("A historical Craps financial effect survives without its operation receipt.");
        if (saved?.SchemaVersion != 1) return;
        var events = await transaction.GetSnapshotAsync(database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("gameId", "craps").WhereEqualTo("instanceId", roundId.ToString("N")), token);
        foreach (var item in events.Documents)
        {
            try
            {
                using var payload = JsonDocument.Parse(item.GetValue<string>("payloadJson"));
                if (!payload.RootElement.TryGetProperty("request", out var request) || request.ValueKind == JsonValueKind.Null ||
                    !request.TryGetProperty("commandId", out var command) || command.ValueKind != JsonValueKind.String) continue;
                var raw = command.GetString();
                if (raw == operationId || Guid.TryParse(raw, out var key) && key.ToString("N") == operationId)
                    throw new InvalidOperationException("A historical Craps action witness survives without its operation receipt.");
            }
            catch (JsonException error) { throw new InvalidOperationException("The historical Craps action witness is corrupt.", error); }
        }
    }

    private async Task<CrapsRoundResponse> CompleteAsync(PreparedCrapsAction action, CancellationToken token)
    {
        var reference = Operation(action.UserId, action.OperationId);
        var snapshot = await reference.GetSnapshotAsync(token);
        var status = snapshot.GetValue<string>("walletStatus");
        if (status is "settled" or "declined") return ReadSettled(snapshot, action);
        if (status != "pending") throw new InvalidOperationException("The Craps operation status is corrupt.");
        var request = WalletRequest(action);
        var receipt = await wallet.SettleAsync(action.UserId, request, token);
        RequireReceipt(action, receipt);
        return await database.RunTransactionAsync(async transaction =>
        {
            var documents = await transaction.GetAllSnapshotsAsync([reference, Player(action.UserId), Round(action.Next.Id)], token);
            var stored = ReadPrepared(documents[0], action.UserId);
            if (JsonSerializer.Serialize(stored, RoundJson) != JsonSerializer.Serialize(action, RoundJson))
                throw new InvalidOperationException("The saved Craps action changed before wallet confirmation.");
            var currentStatus = documents[0].GetValue<string>("walletStatus");
            if (currentStatus is "settled" or "declined") return ReadSettled(documents[0], stored);
            if (currentStatus != "pending" || PendingOperation(documents[1], action.UserId) != action.OperationId ||
                (documents[2].Exists ? Hash(JsonSerializer.Serialize(ReadRound(documents[2], action.UserId), RoundJson)) : null) != action.BaseRoundSha256)
                throw new InvalidOperationException("The Craps pending state does not match its committed predecessor.");
            var declined = receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch;
            var fields = new Dictionary<string, object> { ["walletReceiptJson"] = JsonSerializer.Serialize(receipt), ["walletStatus"] = declined ? "declined" : "settled" };
            if (declined)
            {
                transaction.Set(reference, fields, SetOptions.MergeAll);
                transaction.Set(Player(action.UserId), new Dictionary<string, object> { ["pendingOperationId"] = "" }, SetOptions.MergeAll);
                return (CrapsRoundResponse?)null;
            }
            var actions = action.Next.Actions!.ToArray();
            actions[^1] = actions[^1] with { WalletBeforeCents = receipt.BalanceBeforeCents, WalletAfterCents = receipt.BalanceAfterCents };
            var now = receipt.SettledAtUtc.UtcDateTime;
            var next = action.Next with { Actions = actions,
                StartedAtUtc = action.Kind == "start" ? now : action.Next.StartedAtUtc,
                CompletedAtUtc = action.Next.CompletedAtUtc is not null ? now : null }; ValidateStoredRound(next);
            var result = ToResponse(next, receipt.BalanceAfterCents);
            transaction.Set(Round(next.Id), RoundData(next));
            var player = PlayerData(next.UserId, next.State.Phase == CrapsPassLinePhase.Resolved ? null : next.Id, receipt.SettledAtUtc.UtcDateTime);
            player["pendingOperationId"] = ""; transaction.Set(Player(action.UserId), player, SetOptions.MergeAll);
            fields["responseJson"] = JsonSerializer.Serialize(result); transaction.Set(reference, fields, SetOptions.MergeAll);
            using var scope = action.Observation is null ? null : GameSecurityContext.Push(action.Observation);
            var eventKind = action.Kind switch { "start" => "started", "roll" => "rolled", _ => "wager-adjusted" };
            WriteObservation(transaction, next, eventKind, receipt.SettledAtUtc.UtcDateTime);
            FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "craps", action.UserId, receipt.SettledAtUtc.UtcDateTime);
            if (next.State.Phase == CrapsPassLinePhase.Resolved) WriteSettlement(transaction, next, receipt.SettledAtUtc.UtcDateTime);
            return result;
        }, cancellationToken: token) ?? Declined(action, receipt);
    }
    private async Task RecoverPendingAsync(string userId, CancellationToken token)
    {
        var player = await Player(userId).GetSnapshotAsync(token);
        if (PendingOperation(player, userId) is { } pending)
            await CompleteAsync(ReadPrepared(await Operation(userId, pending).GetSnapshotAsync(token), userId), token);
    }
    private async Task<long> ReadAccountBalance(string userId, CancellationToken token) =>
        (await accounts.GetStateAsync(userId, token)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();
    private static string? PendingOperation(DocumentSnapshot player, string userId)
    {
        if (!player.Exists) return null;
        _ = ActiveId(player, userId);
        if (!player.TryGetValue<string>("pendingOperationId", out var value) || value.Length == 0) return null;
        if (!Guid.TryParseExact(value, "N", out _)) throw new InvalidOperationException("The Craps pending operation pointer is corrupt.");
        return value;
    }
    private static void MatchOperation(DocumentSnapshot snapshot, string userId, string fingerprint)
    {
        if (snapshot.GetValue<string>("userId") != userId || snapshot.GetValue<string>("fingerprint") != fingerprint)
            throw new CrapsPaidException("craps-operation-conflict", "This operation ID was already used for another action.", 409);
    }
    private static PreparedCrapsAction ReadPrepared(DocumentSnapshot snapshot, string userId)
    {
        try { return DecodePrepared(snapshot, userId); }
        catch (Exception error) when (error is JsonException or ArgumentException or CrapsRuleException or OverflowException)
        { throw new InvalidOperationException("The pending Craps action could not be verified.", error); }
    }
    private static PreparedCrapsAction DecodePrepared(DocumentSnapshot snapshot, string userId)
    {
        var action = JsonSerializer.Deserialize<PreparedCrapsAction>(snapshot.GetValue<string>("preparedJson"), RoundJson)
            ?? throw new InvalidOperationException("The prepared Craps action is corrupt.");
        if (action.UserId != userId || action.Next.UserId != userId || action.OperationId != snapshot.GetValue<string>("operationId") ||
            action.Fingerprint != snapshot.GetValue<string>("fingerprint") || action.Next.Id.ToString("N") != snapshot.GetValue<string>("roundId") ||
            action.Kind != snapshot.GetValue<string>("kind") || snapshot.Id != Hash(userId + "\n" + action.OperationId) || action.Next.Revision != action.BaseRevision + 1 ||
            action.BaseTotalWagerCents < 0 || action.BaseTotalReturnCents < 0 || action.Next.TotalReturnCents < action.BaseTotalReturnCents)
            throw new InvalidOperationException("The prepared Craps action is corrupt.");
        ValidateStoredRound(action.Next);
        return action;
    }
    private static GameWalletSettlementRequest WalletRequest(PreparedCrapsAction action)
    {
        var delta = checked(action.Next.TotalWagerCents - action.BaseTotalWagerCents);
        return new(action.OperationId, Hash(JsonSerializer.Serialize(action, RoundJson)), Math.Max(0, delta),
            checked(action.Next.TotalReturnCents - action.BaseTotalReturnCents + Math.Max(0, -delta)));
    }
    private static void RequireReceipt(PreparedCrapsAction action, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "craps", action.UserId, WalletRequest(action)))
            throw new InvalidOperationException("The Craps wallet receipt does not match its saved action.");
    }
    private static CrapsRoundResponse ReadSettled(DocumentSnapshot snapshot, PreparedCrapsAction action)
    {
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(snapshot.GetValue<string>("walletReceiptJson"))
            ?? throw new InvalidOperationException("The Craps wallet receipt is missing.");
        RequireReceipt(action, receipt);
        if (snapshot.GetValue<string>("walletStatus") == "declined")
        {
            if (!receipt.InsufficientFunds && !receipt.InsufficientLegacyFreeGames && !receipt.LegacyFreeGamesCountMismatch)
                throw new InvalidOperationException("An accepted Craps wallet receipt was marked declined.");
            return Declined(action, receipt);
        }
        if (receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch)
            throw new InvalidOperationException("A declined Craps action was marked settled.");
        return Replay(snapshot, action.UserId, action.Fingerprint);
    }
    private static CrapsRoundResponse Declined(PreparedCrapsAction action, GameWalletSettlementReceipt receipt) =>
        throw new CrapsPaidException("craps-insufficient-credits", "The wallet has insufficient credits for this wager.", 409);
    internal static (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)? VerifiedWalletAction(DocumentSnapshot snapshot, string userId, PaidCrapsAction expected)
    {
        if (!snapshot.ContainsField("walletStatus")) return null;
        var prepared = ReadPrepared(snapshot, userId);
        if (snapshot.GetValue<string>("walletStatus") != "settled") throw new InvalidOperationException("An accepted Craps action is missing its wallet confirmation.");
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(snapshot.GetValue<string>("walletReceiptJson"))
            ?? throw new InvalidOperationException("The Craps wallet receipt is missing.");
        RequireReceipt(prepared, receipt);
        var accepted = prepared.Next.Actions![^1] with { WalletBeforeCents = receipt.BalanceBeforeCents, WalletAfterCents = receipt.BalanceAfterCents };
        if (accepted != expected || receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch)
            throw new InvalidOperationException("The Craps wallet confirmation differs from its accepted action.");
        return (WalletRequest(prepared), receipt);
    }
    private sealed record PreparedOrReplay(PreparedCrapsAction? Prepared, CrapsRoundResponse? Replay);
    private sealed record PreparedCrapsAction(string UserId, string OperationId, string Fingerprint, string Kind,
        long BaseRevision, string? BaseRoundSha256, long BaseTotalWagerCents, long BaseTotalReturnCents, PaidCrapsRound Next, GameRequestObservation? Observation);

}
