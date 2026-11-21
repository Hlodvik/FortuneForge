using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HouseHoldem;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Cards.HouseHoldem.HouseHoldemRoundCodec;

namespace FortuneForge.Server.Cards.HouseHoldem;

/// <summary>Freezes commands before wallet effects, then atomically publishes their exact response and game state.</summary>
internal sealed class HouseHoldemPreparedStore(FirestoreDb database)
{
    internal const string Commands = HouseHoldemBackend.CommandsCollection;
    private DocumentReference Operation(string id) => database.Collection(Commands).Document(id);
    private DocumentReference Round(string id) => database.Collection(HouseHoldemBackend.RoundsCollection).Document(id);
    private DocumentReference Active(string account) => database.Collection(HouseHoldemBackend.ActiveCollection).Document(account);
    private DocumentReference Event(string round, string key) => database.Collection(HouseHoldemBackend.EventsCollection).Document($"{round}-{Hash(key)}");

    internal async Task<HouseHoldemCommandRecord?> ExistingAsync(string account, string key, string roundId,
        string command, long ante, long trips, CancellationToken token)
    {
        var snapshot = await Operation($"{roundId}-{Hash(key)}").GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var saved = ReadCommand(snapshot);
        if (saved.Prepared.AccountId != account || saved.Prepared.RoundId != roundId || saved.Prepared.Command != command ||
            saved.Prepared.IdempotencyKey != key || command == "start" && (saved.Prepared.AnteCents != ante || saved.Prepared.TripsCents != trips))
            throw new HouseHoldemRoundConflictException("This Idempotency-Key belongs to a different House Hold'em request.");
        return saved;
    }

    internal Task<HouseHoldemCommandRecord> PrepareAsync(string account, string key, string roundId, string command,
        long ante, long trips, IReadOnlyList<string>? deck, DateTimeOffset now, long balance, bool allowNew,
        CancellationToken token)
    {
        var id = $"{roundId}-{Hash(key)}";
        var observed = GameSecurityContext.Current;
        if (observed is not null && (observed.UserId != account || observed.GameId != HouseHoldemBackend.GameId))
            throw new HouseHoldemRoundConflictException("The authenticated request belongs to a different account or game.");
        return HouseHoldemFirestoreStore.RunWithContentionRetryAsync(() => database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Operation(id), Active(account), Round(roundId), Event(roundId, key)], token);
            if (snapshots[0].Exists)
            {
                var saved = ReadCommand(snapshots[0]);
                if (saved.Prepared.AccountId != account || saved.Prepared.RoundId != roundId || saved.Prepared.Command != command ||
                    saved.Prepared.IdempotencyKey != key || command == "start" && (saved.Prepared.AnteCents != ante || saved.Prepared.TripsCents != trips))
                    throw new HouseHoldemRoundConflictException("This Idempotency-Key belongs to a different House Hold'em request.");
                return saved;
            }
            if (Text(snapshots[1], "pendingOperation") is { Length: > 0 })
                throw new HouseHoldemRoundConflictException("Recover the pending House Hold'em command before playing another.");

            HouseHoldemStoredRound? before = snapshots[2].Exists ? Read(snapshots[2]) : null;
            if (before is not null && before.UserId != account) throw new HouseHoldemRoundNotFoundException();
            if (command == "start" && before is not null && (before.Round.AnteCents != ante || before.Round.TripsCents != trips))
                throw new HouseHoldemRoundConflictException("This Idempotency-Key belongs to a different House Hold'em wager.");

            if (snapshots[3].Exists)
            {
                if (before is null || Text(snapshots[3], "userId") != account || Text(snapshots[3], "command") != command)
                    throw new HouseHoldemRoundConflictException("This Idempotency-Key belongs to a different House Hold'em command.");
                // Historical events already committed their wallet effects atomically. Import a replay receipt without charging again.
                var imported = new HouseHoldemPreparedCommand(id, account, roundId, key, command,
                    before.Round.AnteCents, before.Round.TripsCents, before, before,
                    Text(snapshots[2], "startIdempotencyKey") ?? key, 0, 0, 0, now, observed);
                var result = new HouseHoldemCommandCompletion(new(roundId, before.Round, balance), null, true);
                CreateCommand(transaction, imported, result);
                return new(imported, result);
            }
            if (command == "start" && before is not null)
                throw new InvalidOperationException("The recorded House Hold'em round is missing its initial event.");
            if (command == "start" && !allowNew)
                throw new HouseHoldemRoundPausedException();

            HouseHoldemStoredRound after;
            long wager;
            string startKey;
            if (command == "start")
            {
                if (Text(snapshots[1], "roundId") is { Length: > 0 })
                    throw new HouseHoldemRoundConflictException("Finish the current House Hold'em hand before starting another.");
                var round = HouseHoldemRules.StartRound(deck ?? throw new ArgumentNullException(nameof(deck)), ante, trips);
                after = new(roundId, account, round, deck.ToArray(), [], now);
                wager = round.TotalWagerCents;
                startKey = key;
            }
            else
            {
                if (before is null) throw new HouseHoldemRoundNotFoundException();
                if (Text(snapshots[1], "roundId") != roundId)
                    throw new HouseHoldemRoundConflictException("The saved House Hold'em hand is not active.");
                if (before.Round.Settlement is not null)
                    throw new HouseHoldemRoundConflictException("This House Hold'em hand has already ended.");
                var next = Deserialize<HouseHoldemRound>(Serialize(before.Round));
                try { Apply(next, command); }
                catch (InvalidOperationException) { throw new HouseHoldemRoundConflictException("That House Hold'em action is not legal at this stage of the hand."); }
                wager = checked(next.TotalWagerCents - before.Round.TotalWagerCents);
                after = before with { Round = next, Steps = before.Steps.Append(command).ToArray() };
                startKey = Text(snapshots[2], "startIdempotencyKey") ?? string.Empty;
            }
            var prepared = new HouseHoldemPreparedCommand(id, account, roundId, key, command, after.Round.AnteCents,
                after.Round.TripsCents, before, after, startKey, wager, after.Round.PayoutCents,
                command == "start" ? checked(wager + ante) : wager, now, observed);
            if (after.Round.Settlement is not null)
                prepared = prepared with { PriorFinancialEvidence = await HouseHoldemFinancialProof.ReadPriorAsync(
                    transaction, database, before, account, roundId, token) };
            CreateCommand(transaction, prepared, null);
            transaction.Set(Active(account), new Dictionary<string, object>
            {
                ["pendingOperation"] = id, ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime)
            }, SetOptions.MergeAll);
            return new(prepared, null);
        }, cancellationToken: token), token);
    }

    internal async Task<HouseHoldemCommandRecord?> PendingAsync(string account, CancellationToken token)
    {
        var active = await Active(account).GetSnapshotAsync(token);
        if (Text(active, "pendingOperation") is not { Length: > 0 } id) return null;
        var saved = ReadCommand(await Operation(id).GetSnapshotAsync(token));
        if (saved.Prepared.AccountId != account || saved.Completion is not null)
            throw new InvalidOperationException("The House Hold'em pending command pointer is corrupt.");
        return saved;
    }

    internal Task<HouseHoldemCommandCompletion> FinalizeAsync(HouseHoldemPreparedCommand prepared,
        GameWalletSettlementReceipt receipt, CancellationToken token)
    {
        ValidateWallet(prepared, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        return HouseHoldemFirestoreStore.RunWithContentionRetryAsync(() => database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Operation(prepared.Id), Active(prepared.AccountId), Round(prepared.RoundId)], token);
            var saved = ReadCommand(snapshots[0]);
            if (saved.Prepared.Digest != prepared.Digest) throw new GameWalletUnavailableException();
            if (saved.Completion is { } completed)
                return completed.Wallet == receipt ? completed : throw new GameWalletUnavailableException();
            if (Text(snapshots[1], "pendingOperation") != prepared.Id ||
                (prepared.Before is null ? snapshots[2].Exists : !snapshots[2].Exists || Serialize(Read(snapshots[2])) != Serialize(prepared.Before)))
                throw new InvalidOperationException("The frozen House Hold'em command no longer owns its saved hand.");
            var result = receipt.InsufficientFunds ? null : new HouseHoldemStoreResult(prepared.RoundId, prepared.After.Round, receipt.BalanceAfterCents);
            var completion = new HouseHoldemCommandCompletion(result, receipt, RequiredCents: prepared.MinimumBalanceBeforeCents);
            var financialEvidence = result?.Round.Settlement is null ? null : prepared.PriorFinancialEvidence ??
                await HouseHoldemFinancialProof.ReadPriorAsync(transaction, database, prepared.Before, prepared.AccountId, prepared.RoundId, token);
            var now = receipt.SettledAtUtc < prepared.PreparedAt ? prepared.PreparedAt : receipt.SettledAtUtc;
            var activeFields = new Dictionary<string, object>
            {
                ["pendingOperation"] = string.Empty, ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime)
            };
            if (result is not null)
            {
                transaction.Set(Round(prepared.RoundId), Write(prepared.After, now, prepared.StartKey), SetOptions.MergeAll);
                transaction.Create(Event(prepared.RoundId, prepared.IdempotencyKey), new Dictionary<string, object>
                {
                    ["roundId"] = prepared.RoundId, ["userId"] = prepared.AccountId, ["command"] = prepared.Command,
                    ["idempotencyKey"] = prepared.IdempotencyKey, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L
                });
                activeFields["roundId"] = prepared.After.Round.Settlement is null ? prepared.RoundId : string.Empty;
                HouseHoldemRoundReporting.Write(transaction, database, prepared, receipt, now, financialEvidence);
            }
            transaction.Set(Active(prepared.AccountId), activeFields, SetOptions.MergeAll);
            var json = Serialize(completion);
            transaction.Set(Operation(prepared.Id), new Dictionary<string, object>
            {
                ["completionJson"] = json, ["completionSha256"] = Hash(json),
                ["status"] = receipt.InsufficientFunds ? "declined" : "applied"
            }, SetOptions.MergeAll);
            return completion;
        }, cancellationToken: token), token);
    }

    internal async Task<HouseHoldemStoredRound?> ReadRoundAsync(string account, string id, CancellationToken token)
    {
        var snapshot = await Round(id).GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var stored = Read(snapshot);
        return stored.UserId == account ? stored : null;
    }

    internal async Task<string?> ActiveRoundAsync(string account, CancellationToken token) =>
        Text(await Active(account).GetSnapshotAsync(token), "roundId") is { Length: > 0 } id ? id : null;

    private void CreateCommand(Transaction transaction, HouseHoldemPreparedCommand prepared, HouseHoldemCommandCompletion? completion)
    {
        var payload = Serialize(prepared);
        var fields = new Dictionary<string, object>
        {
            ["payloadJson"] = payload, ["payloadSha256"] = Hash(payload), ["accountId"] = prepared.AccountId,
            ["roundId"] = prepared.RoundId, ["status"] = completion is null ? "pending" : "applied", ["schemaVersion"] = 1L
        };
        if (completion is not null)
        {
            var json = Serialize(completion);
            fields["completionJson"] = json;
            fields["completionSha256"] = Hash(json);
        }
        transaction.Create(Operation(prepared.Id), fields);
    }

    internal static HouseHoldemCommandRecord ReadCommand(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<long>("schemaVersion", out var schema) || schema != 1)
            throw new InvalidOperationException("The House Hold'em frozen command schema is unsupported.");
        var json = Text(snapshot, "payloadJson") ?? throw new InvalidOperationException("The frozen House Hold'em command is missing.");
        if (Hash(json) != Text(snapshot, "payloadSha256")) throw new InvalidOperationException("The frozen House Hold'em command digest is corrupt.");
        var prepared = Deserialize<HouseHoldemPreparedCommand>(json);
        if (prepared.Id != snapshot.Id || prepared.Id != $"{prepared.RoundId}-{Hash(prepared.IdempotencyKey)}" ||
            prepared.AccountId != Text(snapshot, "accountId") || prepared.RoundId != Text(snapshot, "roundId") ||
            prepared.After.RoundId != prepared.RoundId || prepared.After.UserId != prepared.AccountId)
            throw new InvalidOperationException("The House Hold'em command identity is corrupt.");
        ValidateReplay(prepared.After);
        if (prepared.Before is { } before)
        {
            ValidateReplay(before);
            if (before.UserId != prepared.AccountId || before.RoundId != prepared.RoundId)
                throw new InvalidOperationException("The House Hold'em command begins from a different hand.");
        }
        HouseHoldemCommandCompletion? completion = null;
        if (Text(snapshot, "completionJson") is { } result)
        {
            if (Hash(result) != Text(snapshot, "completionSha256")) throw new InvalidOperationException("The House Hold'em response digest is corrupt.");
            completion = Deserialize<HouseHoldemCommandCompletion>(result);
        }
        if (completion?.LegacyAlreadyApplied != true &&
            (prepared.WagerCents != prepared.After.Round.TotalWagerCents - (prepared.Before?.Round.TotalWagerCents ?? 0) ||
             prepared.PayoutCents != prepared.After.Round.PayoutCents ||
             prepared.MinimumBalanceBeforeCents != prepared.WagerCents + (prepared.Command == "start" ? prepared.AnteCents : 0)))
            throw new InvalidOperationException("The frozen House Hold'em wallet terms do not match its saved hand.");
        if (completion?.Result is { } final &&
            (final.RoundId != prepared.RoundId || Serialize(final.Round) != Serialize(prepared.After.Round) ||
             final.BalanceCents < 0 || completion.Wallet is { } wallet && final.BalanceCents != wallet.BalanceAfterCents))
            throw new InvalidOperationException("The saved House Hold'em response does not match its frozen command.");
        return new(prepared with { FrozenSha256 = Text(snapshot, "payloadSha256") }, completion);
    }

    internal static void ValidateWallet(HouseHoldemPreparedCommand prepared, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, HouseHoldemBackend.GameId, prepared.AccountId, prepared.WalletRequest()))
            throw new GameWalletUnavailableException();
    }
}
