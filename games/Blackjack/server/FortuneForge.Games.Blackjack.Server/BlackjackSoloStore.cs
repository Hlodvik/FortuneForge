using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack;

/// <summary>
/// Freezes each command before contacting the wallet. A pending command fences the hand until
/// its original wallet receipt is reconciled. Game documents retain the historical field schema.
/// </summary>
public sealed partial class BlackjackSoloStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts,
    Func<bool>? allowStarts = null) : IBlackjackStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string Commands = BlackjackBackend.SoloCommands;
    private const string PendingField = "pendingWalletCommand";

    public async Task<BlackjackStoreResult> StartAsync(string userId, string idempotencyKey, long wagerCents,
        IReadOnlyList<string> shuffledDeck, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var gameId = Hash($"{userId}\n{idempotencyKey}");
        var commandId = Hash($"{userId}\nstart\n{idempotencyKey}");
        var fingerprint = Hash($"{gameId}\nstart\n{wagerCents}");
        var prepared = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Command(commandId), Game(gameId)], cancellationToken);
            if (snapshots[0].Exists) return ReadCommand(snapshots[0], userId, fingerprint);
            if (snapshots[1].Exists)
            {
                var legacy = BlackjackSoloCodec.Read(snapshots[1]);
                if (legacy.UserId != userId || legacy.WagerCents != wagerCents)
                    throw new BlackjackConflictException("This Idempotency-Key was already used with a different Blackjack request.");
                // A pre-migration hand has already paid its opening wager.
                return null;
            }
            if (allowStarts?.Invoke() is false) throw new BlackjackAdmissionPausedException();
            var next = BlackjackRules.Deal(gameId, userId, wagerCents, shuffledDeck, nowUtc);
            var operation = new SoloCommand(commandId, userId, gameId, fingerprint, "deal", idempotencyKey,
                0, next, wagerCents, next.PayoutCents, GameSecurityContext.Current, BlackjackFinancialSnapshot.Empty, null, null);
            transaction.Create(Command(commandId), Encode(operation));
            var fields = BlackjackSoloCodec.Create(next, idempotencyKey);
            fields[PendingField] = commandId;
            transaction.Create(Game(gameId), fields);
            return operation;
        }, cancellationToken: cancellationToken);
        if (prepared is null)
            return await GetAsync(userId, gameId, cancellationToken) ?? throw new BlackjackNotFoundException();
        return Result(await CompleteAsync(prepared, cancellationToken));
    }

    public async Task<BlackjackStoreResult?> GetAsync(string userId, string gameId, CancellationToken cancellationToken)
    {
        DocumentSnapshot snapshot;
        do
        {
            await RecoverAsync(userId, gameId, cancellationToken);
            snapshot = await Game(gameId).GetSnapshotAsync(cancellationToken);
        } while (snapshot.Exists && Text(snapshot, "userId") == userId && Text(snapshot, PendingField) is { Length: > 0 });
        if (!snapshot.Exists) return null;
        var game = BlackjackSoloCodec.Read(snapshot);
        if (game.UserId != userId) return null;
        var state = await accounts.GetStateAsync(userId, cancellationToken);
        return new(game, state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException());
    }

    public async Task<BlackjackStoreResult> ActAsync(string userId, string gameId, string idempotencyKey,
        int expectedVersion, string action, DateTime nowUtc, CancellationToken cancellationToken)
    {
        action = action.Trim().ToLowerInvariant();
        var commandId = Hash($"{userId}\naction\n{idempotencyKey}");
        var fingerprint = Hash($"{gameId}\n{expectedVersion}\n{action}");
        var recorded = await Command(commandId).GetSnapshotAsync(cancellationToken);
        if (recorded.Exists)
            return Result(await CompleteAsync(ReadCommand(recorded, userId, fingerprint), cancellationToken));
        await RecoverAsync(userId, gameId, cancellationToken);
        var prepared = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [Command(commandId), Game(gameId), LegacyAction(userId, idempotencyKey)], cancellationToken);
            if (snapshots[0].Exists) return ReadCommand(snapshots[0], userId, fingerprint);
            if (!snapshots[1].Exists) throw new BlackjackNotFoundException();
            var before = BlackjackSoloCodec.Read(snapshots[1]);
            if (before.UserId != userId) throw new BlackjackNotFoundException();
            if (snapshots[2].Exists)
            {
                if (Text(snapshots[2], "gameId") != gameId || Text(snapshots[2], "action") != action ||
                    snapshots[2].GetValue<long>("expectedVersion") != expectedVersion)
                    throw new BlackjackConflictException("This Idempotency-Key was already used with a different Blackjack action.");
                return null;
            }
            if (Text(snapshots[1], PendingField) is { Length: > 0 })
                throw new BlackjackConflictException("Retry the pending Blackjack action before choosing another action.");
            if (before.Version != expectedVersion)
                throw new BlackjackConflictException("The Blackjack hand changed. Reload it before choosing another action.");
            var next = BlackjackRules.ApplyAction(before, action, nowUtc);
            var priorProof = await ReadPriorFinancialEvidenceAsync(transaction, before, cancellationToken);
            var operation = new SoloCommand(commandId, userId, gameId, fingerprint, action, idempotencyKey,
                expectedVersion, next, checked(next.TotalWagerCents - before.TotalWagerCents),
                next.Status == BlackjackStatuses.Completed ? next.PayoutCents : 0, GameSecurityContext.Current, priorProof, null, null);
            transaction.Create(Command(commandId), Encode(operation));
            transaction.Update(Game(gameId), new Dictionary<string, object> { [PendingField] = commandId });
            return operation;
        }, cancellationToken: cancellationToken);
        if (prepared is null)
            return await GetAsync(userId, gameId, cancellationToken) ?? throw new BlackjackNotFoundException();
        return Result(await CompleteAsync(prepared, cancellationToken));
    }

    private async Task RecoverAsync(string userId, string gameId, CancellationToken token)
    {
        var snapshot = await Game(gameId).GetSnapshotAsync(token);
        if (!snapshot.Exists || Text(snapshot, "userId") != userId || Text(snapshot, PendingField) is not { Length: > 0 } pending)
            return;
        var stored = await Command(pending).GetSnapshotAsync(token);
        if (!stored.Exists) throw new GameWalletUnavailableException();
        var command = ReadCommand(stored, userId);
        if (command.GameId != gameId) throw new GameWalletUnavailableException();
        await CompleteAsync(command, token);
    }

    private async Task<SoloCommand> CompleteAsync(SoloCommand command, CancellationToken token)
    {
        if (command.Wallet is not null) return command;
        var receipt = await wallet.SettleAsync(command.UserId, WalletRequest(command), token);
        receipt = receipt with { WasAlreadySettled = false };
        return await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Command(command.Id), Game(command.GameId)], token);
            var current = ReadCommand(snapshots[0], command.UserId, command.Fingerprint);
            if (current.Wallet is not null) return current;
            if (OutcomeHash(current) != OutcomeHash(command) || Text(snapshots[1], PendingField) != command.Id)
                throw new GameWalletUnavailableException();
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "blackjack", command.UserId, WalletRequest(command)))
                throw new GameWalletUnavailableException();
            var final = command with { Wallet = receipt, BalanceCents = receipt.BalanceAfterCents };
            if (receipt.InsufficientFunds)
            {
                if (command.ExpectedVersion == 0) transaction.Delete(Game(command.GameId));
                else transaction.Update(Game(command.GameId), new Dictionary<string, object> { [PendingField] = FieldValue.Delete });
            }
            else
            {
                var fields = BlackjackSoloCodec.Update(command.After);
                fields[PendingField] = FieldValue.Delete;
                fields["backendOwner"] = BlackjackBackend.Owner;
                fields["walletCommandIds"] = FieldValue.ArrayUnion(command.Id);
                transaction.Update(Game(command.GameId), fields);
                if (command.ExpectedVersion > 0)
                    transaction.Create(LegacyAction(command.UserId, command.IdempotencyKey), new Dictionary<string, object>
                    {
                        ["userId"] = command.UserId, ["gameId"] = command.GameId, ["action"] = command.Action,
                        ["expectedVersion"] = command.ExpectedVersion, ["resultVersion"] = command.After.Version,
                        ["createdAt"] = Timestamp.FromDateTime(command.After.UpdatedAtUtc)
                    });
                Record(transaction, final);
            }
            transaction.Set(Command(command.Id), Encode(final));
            return final;
        }, cancellationToken: token);
    }

    private void Record(Transaction transaction, SoloCommand command)
    {
        using var originalRequest = command.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
        var game = command.After;
        if (!BlackjackSoloReplay.Verify(game)) throw new GameWalletUnavailableException();
        FirestoreGameJournal.Write(transaction, database, "blackjack", game.GameId, game.Version,
            game.UpdatedAtUtc, game, [game.UserId], command.Action);
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "blackjack", game.UserId, game.UpdatedAtUtc);
        if (game.Status != BlackjackStatuses.Completed) return;
        var round = SinglePlayerRoundRecord.Create("blackjack", game.GameId, game.UserId, game.TotalWagerCents,
            game.PayoutCents, game.CreatedAtUtc, game.UpdatedAtUtc, BlackjackBackend.SoloGames, game.GameId,
            new { game, terminalAction = command.Action }, "legacy-blackjack-cards-and-final-action-v1",
            settlementVerified: true, outcomeAuditorVersion: BlackjackSoloReplay.Version);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(round.Settlement.Id), round.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, BlackjackFinancialProof.Protect(round.Audit, FinancialEvidence(command)));
    }

    private static BlackjackStoreResult Result(SoloCommand command)
    {
        if (command.Wallet is not { } receipt || command.BalanceCents is not { } balance || receipt.BalanceAfterCents != balance)
            throw new GameWalletUnavailableException();
        if (receipt.InsufficientFunds) throw new BlackjackInsufficientCreditsException(receipt.BalanceBeforeCents, command.WagerCents);
        return new(command.After, balance);
    }

    private static string OutcomeHash(SoloCommand command) => Hash(JsonSerializer.Serialize(command with { Wallet = null, BalanceCents = null }, Json));
    private static GameWalletSettlementRequest WalletRequest(SoloCommand command) =>
        new(command.Id, OutcomeHash(command), command.WagerCents, command.PayoutCents);
    private static Dictionary<string, object> Encode(SoloCommand command)
    {
        var json = JsonSerializer.Serialize(command, Json);
        return new() { ["userId"] = command.UserId, ["gameId"] = command.GameId, ["fingerprint"] = command.Fingerprint,
            ["payloadJson"] = json, ["sha256"] = Hash(json), ["schemaVersion"] = 1L };
    }
    private static SoloCommand ReadCommand(DocumentSnapshot snapshot, string userId, string? fingerprint = null)
    {
        var json = Text(snapshot, "payloadJson") ?? throw new GameWalletUnavailableException();
        if (Text(snapshot, "sha256") != Hash(json)) throw new GameWalletUnavailableException();
        var command = JsonSerializer.Deserialize<SoloCommand>(json, Json) ?? throw new GameWalletUnavailableException();
        if (command.UserId != userId || command.Id != snapshot.Id || command.GameId != Text(snapshot, "gameId") || command.Fingerprint != Text(snapshot, "fingerprint") ||
            command.After.GameId != command.GameId || command.After.UserId != userId || !BlackjackSoloReplay.Verify(command.After) ||
            command.Observation is { } observation && (observation.UserId != userId || observation.GameId != "blackjack"))
            throw new GameWalletUnavailableException();
        if (fingerprint is not null && command.Fingerprint != fingerprint)
            throw new BlackjackConflictException("This Idempotency-Key was already used with a different Blackjack request.");
        if (command.Wallet is { } receipt && (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "blackjack", userId, WalletRequest(command)) ||
            command.BalanceCents != receipt.BalanceAfterCents)) throw new GameWalletUnavailableException();
        return command;
    }

    private DocumentReference Command(string id) => database.Collection(Commands).Document(id);
    private DocumentReference Game(string id) => database.Collection(BlackjackBackend.SoloGames).Document(id);
    private DocumentReference LegacyAction(string user, string key) => database.Collection(BlackjackBackend.SoloActions).Document(Hash($"{user}\n{key}"));
    private static string? Text(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : null;
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record SoloCommand(string Id, string UserId, string GameId, string Fingerprint, string Action,
        string IdempotencyKey, int ExpectedVersion, BlackjackGame After, long WagerCents, long PayoutCents,
        GameRequestObservation? Observation,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        BlackjackFinancialSnapshot? PriorFinancialEvidence,
        GameWalletSettlementReceipt? Wallet, long? BalanceCents);
}

public sealed class BlackjackAdmissionPausedException() : Exception("New Blackjack hands are paused. Existing hands can still be recovered and finished.");
