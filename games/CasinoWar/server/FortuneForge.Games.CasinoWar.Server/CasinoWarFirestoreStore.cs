using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Cards.CasinoWar.CasinoWarRoundCodec;

namespace FortuneForge.Server.Cards.CasinoWar;

/// <summary>Freezes each paid action before a signed wallet call and publishes it only after a durable receipt.</summary>
internal sealed class CasinoWarFirestoreStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts, bool allowNewPaidRounds = false) : ICasinoWarStore
{
    internal const string Commands = "casinoWarPreparedActions";
    private DocumentReference Command(string id) => database.Collection(Commands).Document(id);
    private DocumentReference Pending(string account) => database.Collection("casinoWarPendingActions").Document(Hash(account));
    private DocumentReference Round(string id) => database.Collection(Rounds).Document(id);
    private DocumentReference RoundEvent(string id, string kind) => database.Collection(Events).Document($"{id}-{kind}");
    internal static string ActionId(string account, string round, string kind, string key) => Hash($"{account}\n{round}\n{kind}\n{key}");

    public async Task<CasinoWarStoreResult> StartAsync(string userId, string idempotencyKey,
        long primaryStakeCents, long tieStakeCents, IReadOnlyList<PlayingCard> shuffledShoe,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var command = await ExistingAsync(userId, roundId, "opening", idempotencyKey, primaryStakeCents,
            tieStakeCents, null, cancellationToken);
        command ??= await PrepareAsync(userId, roundId, "opening", idempotencyKey, primaryStakeCents,
            tieStakeCents, null, shuffledShoe, nowUtc, await BalanceAsync(userId, cancellationToken), cancellationToken);
        return Result(await ResumeAsync(command, cancellationToken));
    }

    public async Task<CasinoWarStoreResult> DecideAsync(string userId, string roundId, string idempotencyKey,
        CasinoWarTieDecision decision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var choice = CasinoWarService.DecisionName(decision);
        var command = await ExistingAsync(userId, roundId, "decision", idempotencyKey, 0, 0, choice, cancellationToken);
        command ??= await PrepareAsync(userId, roundId, "decision", idempotencyKey, 0, 0, choice,
            null, nowUtc, await BalanceAsync(userId, cancellationToken), cancellationToken);
        return Result(await ResumeAsync(command, cancellationToken));
    }

    public async Task<CasinoWarStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        if (await PendingAsync(userId, cancellationToken) is { } pending && pending.Prepared.RoundId == roundId)
            await ResumeAsync(pending, cancellationToken);
        var saved = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!saved.Exists) return null;
        var round = Read(saved);
        return round.UserId == userId ? new(round.Restore(), await BalanceAsync(userId, cancellationToken)) : null;
    }

    internal async Task<CasinoWarRecoveryResponse> RecoverAsync(string userId, CancellationToken token)
    {
        CasinoWarRoundResponse? recovered = null;
        if (await PendingAsync(userId, token) is { } pending)
        {
            var resumed = await ResumeAsync(pending, token);
            if (resumed.Completion?.Wallet?.InsufficientFunds != true)
                recovered = CasinoWarService.ToResponse(Result(resumed));
        }
        var snapshot = await database.Collection(Rounds).WhereEqualTo("userId", userId)
            .WhereEqualTo("phase", "awaiting-tie-decision").Limit(100).GetSnapshotAsync(token);
        var balance = await BalanceAsync(userId, token);
        return new(recovered, snapshot.Documents.Select(document => Read(document)).OrderByDescending(saved => saved.CreatedAt)
            .Select(saved => CasinoWarService.ToResponse(new(saved.Restore(), balance))).ToArray());
    }

    internal async Task<IReadOnlyList<CasinoWarRoundResponse>> HistoryAsync(string userId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit), "Choose 1 to 100 Casino War history entries.");
        // This reads the original collection, including rounds saved before the package cutover.
        var snapshot = await database.Collection(Rounds).WhereEqualTo("userId", userId)
            .OrderByDescending("createdAt").Limit(limit).GetSnapshotAsync(token);
        var balance = await BalanceAsync(userId, token);
        return snapshot.Documents.Select(document => Read(document))
            .Select(saved => CasinoWarService.ToResponse(new(saved.Restore(), balance))).ToArray();
    }

    internal async Task<long> BalanceAsync(string account, CancellationToken token)
    {
        var state = await accounts.GetStateAsync(account, token);
        return state.SlotsCreditsBalanceCents is { } balance && balance >= 0
            ? balance : throw new GameWalletUnavailableException();
    }

    private async Task<CasinoWarActionRecord?> ExistingAsync(string account, string round, string kind,
        string key, long primary, long tie, string? decision, CancellationToken token)
    {
        var snapshot = await Command(ActionId(account, round, kind, key)).GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var saved = ReadCommand(snapshot);
        Verify(saved.Prepared, account, round, kind, key, primary, tie, decision);
        return saved;
    }

    private Task<CasinoWarActionRecord> PrepareAsync(string account, string roundId, string kind, string key,
        long primary, long tie, string? decision, IReadOnlyList<PlayingCard>? shoe, DateTimeOffset now,
        long balance, CancellationToken token)
    {
        var id = ActionId(account, roundId, kind, key);
        var observation = GameSecurityContext.Current;
        if (observation is not null && (observation.UserId != account || observation.GameId != CasinoWarModule.Descriptor.Id))
            throw new CasinoWarRoundConflictException("The authenticated request belongs to another account or game.");
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [Command(id), Pending(account), Round(roundId), RoundEvent(roundId, kind)], token);
            if (snapshots[0].Exists)
            {
                var existing = ReadCommand(snapshots[0]);
                Verify(existing.Prepared, account, roundId, kind, key, primary, tie, decision);
                return existing;
            }
            if (Text(snapshots[1], "operationId") is { Length: > 0 })
                throw new CasinoWarPendingActionException(Text(snapshots[1], "roundId") ?? roundId);
            var before = snapshots[2].Exists ? Read(snapshots[2]) : null;
            if (before is not null && before.UserId != account) throw new CasinoWarRoundNotFoundException();
            if (kind == "opening" && before is not null &&
                (before.StartKey != key || before.PrimaryStakeCents != primary || before.TieStakeCents != tie))
                throw new CasinoWarRoundConflictException("This Idempotency-Key was already used with different Casino War stakes.");
            if (before is not null && (kind == "opening" || before.Restore().Round.Phase == CasinoWarRoundPhase.Completed))
            {
                if (kind == "decision" && before.Decision != decision)
                    throw new CasinoWarRoundConflictException("This Casino War round was already completed with a different decision.");
                if (!snapshots[3].Exists || Text(snapshots[3], "userId") != account)
                    throw new InvalidOperationException("The recorded Casino War action is missing its original event.");
                // Legacy game/event writes committed their money together. Import a receipt without another wallet operation.
                var imported = new CasinoWarPreparedAction(id, account, roundId, key, kind, before, before, now, observation);
                var completion = new CasinoWarActionCompletion(balance, null, true);
                WriteCommand(transaction, imported, completion);
                return new(imported, completion);
            }
            if (snapshots[3].Exists)
                throw new InvalidOperationException("A Casino War action event exists without its resulting round.");
            CasinoWarSavedRound after;
            if (kind == "opening")
            {
                if (!allowNewPaidRounds) throw new CasinoWarAdmissionPausedException();
                CasinoWarMoney.PrimaryCents(CasinoWarMoney.ToRand(primary));
                CasinoWarMoney.TieCents(CasinoWarMoney.ToRand(tie));
                if (shoe is null || shoe.Count < CasinoWarRoundEngine.WarCardsRequired)
                    throw new ArgumentException("A Casino War shoe requires at least ten cards.", nameof(shoe));
                after = new(roundId, account, primary, tie, shoe.Take(CasinoWarRoundEngine.WarCardsRequired)
                    .Select(CardCode.Format).ToArray(), null, key, null, now);
            }
            else
            {
                if (before is null) throw new CasinoWarRoundNotFoundException();
                after = before with { Decision = decision, DecisionKey = key };
            }
            _ = after.Restore();
            var prior = CasinoWarFinancialSnapshot.Empty;
            if (kind == "decision")
            {
                var opening = await transaction.GetSnapshotAsync(Command(ActionId(account, roundId, "opening", before!.StartKey)), token);
                if (opening.Exists && ReadCommand(opening) is { Completion: { ImportedLegacy: false, Wallet: { } paid } } original)
                    prior = CasinoWarFinancialProof.Confirmed(original.Prepared, paid);
                else if (Text(snapshots[2], "backendOwner") == BackendOwner)
                    throw new GameWalletUnavailableException();
                else
                    prior = await CasinoWarFinancialProof.ReadLegacyOpeningAsync(transaction, database, before!, token);
            }
            var prepared = new CasinoWarPreparedAction(id, account, roundId, key, kind, before, after, now, observation, prior);
            WriteCommand(transaction, prepared, null);
            transaction.Set(Pending(account), new Dictionary<string, object>
            {
                ["accountId"] = account, ["operationId"] = id, ["roundId"] = roundId,
                ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime),
            });
            return new(prepared, null);
        }, cancellationToken: token);
    }

    private async Task<CasinoWarActionRecord?> PendingAsync(string account, CancellationToken token)
    {
        var pointer = await Pending(account).GetSnapshotAsync(token);
        if (Text(pointer, "operationId") is not { Length: > 0 } operation) return null;
        var saved = ReadCommand(await Command(operation).GetSnapshotAsync(token));
        if (saved.Prepared.AccountId != account || saved.Prepared.RoundId != Text(pointer, "roundId"))
            throw new InvalidOperationException("The Casino War pending action pointer is corrupt.");
        return saved;
    }

    private async Task<CasinoWarActionRecord> ResumeAsync(CasinoWarActionRecord command, CancellationToken token)
    {
        if (command.Completion is not null) return command;
        var prepared = command.Prepared;
        var receipt = await wallet.SettleAsync(prepared.AccountId, prepared.WalletRequest(), token);
        ValidateWallet(prepared, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        return await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [Command(prepared.Id), Pending(prepared.AccountId), Round(prepared.RoundId), RoundEvent(prepared.RoundId, prepared.Kind)], token);
            var existing = ReadCommand(snapshots[0]);
            if (existing.Prepared.Digest != prepared.Digest) throw new GameWalletUnavailableException();
            if (existing.Completion is not null)
                return existing.Completion.Wallet == receipt ? existing : throw new GameWalletUnavailableException();
            if (Text(snapshots[1], "operationId") != prepared.Id || snapshots[3].Exists ||
                (prepared.Before is null ? snapshots[2].Exists
                    : !snapshots[2].Exists || Serialize(Read(snapshots[2])) != Serialize(prepared.Before)))
                throw new InvalidOperationException("The frozen Casino War action no longer owns its saved round.");
            var completion = new CasinoWarActionCompletion(receipt.BalanceAfterCents, receipt);
            var now = receipt.SettledAtUtc < prepared.PreparedAt ? prepared.PreparedAt : receipt.SettledAtUtc;
            if (!receipt.InsufficientFunds)
            {
                transaction.Set(Round(prepared.RoundId), Write(prepared.After, now));
                transaction.Create(RoundEvent(prepared.RoundId, prepared.Kind), Event(prepared, now));
                CasinoWarRoundReporting.Write(transaction, database, prepared, receipt, now);
            }
            transaction.Delete(Pending(prepared.AccountId));
            WriteCompletion(transaction, prepared.Id, completion);
            return new CasinoWarActionRecord(prepared, completion);
        }, cancellationToken: token);
    }

    private static CasinoWarStoreResult Result(CasinoWarActionRecord action)
    {
        var completion = action.Completion ?? throw new GameWalletUnavailableException();
        if (completion.Wallet?.InsufficientFunds == true)
            throw new CasinoWarInsufficientCreditsException(completion.Wallet.BalanceBeforeCents, action.Prepared.WagerCents);
        return new(action.Prepared.After.Restore(), completion.BalanceCents);
    }

    private void WriteCommand(Transaction transaction, CasinoWarPreparedAction prepared, CasinoWarActionCompletion? completion)
    {
        var json = Serialize(prepared);
        var fields = new Dictionary<string, object>
        {
            ["payloadJson"] = json, ["payloadSha256"] = Hash(json), ["accountId"] = prepared.AccountId,
            ["roundId"] = prepared.RoundId, ["status"] = completion is null ? "pending" : "applied", ["schemaVersion"] = 1L,
        };
        if (completion is not null)
        {
            var result = Serialize(completion);
            fields["completionJson"] = result;
            fields["completionSha256"] = Hash(result);
        }
        transaction.Create(Command(prepared.Id), fields);
    }

    private void WriteCompletion(Transaction transaction, string id, CasinoWarActionCompletion completion)
    {
        var json = Serialize(completion);
        transaction.Update(Command(id), new Dictionary<string, object>
        {
            ["completionJson"] = json, ["completionSha256"] = Hash(json),
            ["status"] = completion.Wallet?.InsufficientFunds == true ? "declined" : "applied",
        });
    }

    private static CasinoWarActionRecord ReadCommand(DocumentSnapshot snapshot)
    {
        var payload = Text(snapshot, "payloadJson") ?? throw new InvalidOperationException("The frozen Casino War action is missing.");
        if (Hash(payload) != Text(snapshot, "payloadSha256"))
            throw new InvalidOperationException("The frozen Casino War action digest is corrupt.");
        var prepared = Deserialize<CasinoWarPreparedAction>(payload);
        if (prepared.Id != snapshot.Id || prepared.Id != ActionId(prepared.AccountId, prepared.RoundId, prepared.Kind, prepared.Key) ||
            prepared.AccountId != Text(snapshot, "accountId") || prepared.RoundId != Text(snapshot, "roundId") ||
            prepared.Kind is not ("opening" or "decision") || prepared.After.UserId != prepared.AccountId ||
            prepared.After.RoundId != prepared.RoundId || prepared.PreparedAt == default || prepared.PreparedAt.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The frozen Casino War action identity is corrupt.");
        _ = prepared.After.Restore();
        CasinoWarActionCompletion? completion = null;
        if (Text(snapshot, "completionJson") is { } json)
        {
            if (Hash(json) != Text(snapshot, "completionSha256"))
                throw new InvalidOperationException("The Casino War action response digest is corrupt.");
            completion = Deserialize<CasinoWarActionCompletion>(json);
            if (completion.BalanceCents < 0 || completion.Wallet is { } receipt && completion.BalanceCents != receipt.BalanceAfterCents ||
                completion.ImportedLegacy == (completion.Wallet is not null))
                throw new InvalidOperationException("The Casino War action response is corrupt.");
            if (completion.Wallet is { } paid) ValidateWallet(prepared, paid);
        }
        return new(prepared, completion);
    }

    private static void Verify(CasinoWarPreparedAction prepared, string account, string round, string kind,
        string key, long primary, long tie, string? decision)
    {
        if (prepared.AccountId != account || prepared.RoundId != round || prepared.Kind != kind || prepared.Key != key ||
            kind == "opening" && (prepared.After.PrimaryStakeCents != primary || prepared.After.TieStakeCents != tie) ||
            kind == "decision" && prepared.After.Decision != decision)
            throw new CasinoWarRoundConflictException("This Idempotency-Key was already used with a different Casino War request.");
    }

    private static void ValidateWallet(CasinoWarPreparedAction prepared, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, CasinoWarModule.GameId, prepared.AccountId, prepared.WalletRequest()) ||
            receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch)
            throw new GameWalletUnavailableException();
    }
}
