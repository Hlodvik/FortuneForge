using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

public sealed class NeonCarRespinStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader balances, IGameRandomSource draw, TimeProvider clock)
{
    public const string PendingAdjustmentField = "neonCarRespinPendingAdjustmentId";
    public const string WorkerOwnerField = "neonWorkerOwner";
    public const string WorkerOwner = "neon-nights-worker-v1";

    private const string GameId = NeonArcadeSlotDefinitions.GameId;
    private const string Adjustments = "neonCarRespinAdjustments";
    private const string NitroOfferField = "pendingNeonNitroChoiceJson";
    private const string NitroOutcomeField = "pendingNeonNitroCarRespinJson";

    public Task<NeonCarRespinResponse> RespinAsync(string accountId, Guid spinId, int reelIndex, CancellationToken token) =>
        ResolveAsync(accountId, spinId, reelIndex, null, token);

    public Task<NeonCarRespinResponse> ChooseNitroAsync(string accountId, Guid spinId, int reelIndex, CancellationToken token) =>
        ResolveAsync(accountId, spinId, null, reelIndex, token);

    public Task DismissAsync(string accountId, Guid spinId, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Guard(accountId);
            var guard = await transaction.GetSnapshotAsync(reference, token);
            var round = ReadGuard(guard);
            if (HasForeignOwner(guard) || Text(guard, PendingAdjustmentField) is not null ||
                Text(guard, SlotSpinGuard.PreparedAdmissionField) is not null ||
                !round.CanDismiss(spinId, () => ReadNitroOffer(guard) is not null)) return;

            var fields = new Dictionary<string, object>
            {
                [WorkerOwnerField] = WorkerOwner,
                ["updatedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            };
            round.ClearOffer().WriteOffer(fields);
            transaction.Update(reference, fields);
        }, cancellationToken: token);

    private async Task<NeonCarRespinResponse> ResolveAsync(string accountId, Guid spinId, int? initialReel,
        int? nitroReel, CancellationToken token)
    {
        if (spinId == Guid.Empty || (initialReel ?? nitroReel) is not { } reel || !NeonNitroChoice.IsChoiceReel(reel))
            throw Unavailable();
        var account = await balances.GetStateAsync(accountId, token);
        var currentBalance = Balance(account);
        var prepared = await PrepareAsync(accountId, spinId, initialReel, nitroReel, account, token);
        if (prepared.Replay is { } alreadyApplied)
            return NeonCarRespinResponse.From(alreadyApplied, currentBalance, true, prepared.RoundWinPoints);

        var frozen = prepared.Frozen ?? throw InvalidState();
        if (frozen.PendingOffer is { } offer)
            return NeonCarRespinResponse.From(frozen.Outcome, currentBalance, false, prepared.RoundWinPoints, offer);

        GameWalletSettlementReceipt adjustment;
        try
        {
            adjustment = await wallet.SettleAsync(accountId, new(frozen.Id, Hash(frozen), 0, frozen.PayoutCents),
                CancellationToken.None);
        }
        catch (GameWalletConflictException error)
        {
            throw new GameWalletUnavailableException(error);
        }
        ValidateWallet(frozen, adjustment);
        var completed = await CompleteAsync(accountId, spinId, frozen, adjustment);
        return completed.AlreadyApplied ? completed with
        {
            SlotsCreditsBalance = Balance(await balances.GetStateAsync(accountId, token)) / 100m
        } : completed;
    }

    private Task<PreparedRespin> PrepareAsync(string accountId, Guid spinId, int? initialReel, int? nitroReel,
        GameWalletAccountState account, CancellationToken token) =>
        database.RunTransactionAsync<PreparedRespin>(async transaction =>
        {
            var source = ReadSource(await transaction.GetSnapshotAsync(Source(spinId), token), accountId, spinId);
            var id = AdjustmentId(accountId, spinId, source.ReceiptId);
            var guardRef = Guard(accountId);
            var stageRef = database.Collection(Adjustments).Document(id);
            var snapshots = await transaction.GetAllSnapshotsAsync([Receipt(source), guardRef, stageRef], token);
            var receipt = snapshots[0];
            var guard = snapshots[1];
            var stage = snapshots[2];
            var round = ReadGuard(guard);
            if (receipt.Exists)
            {
                var replay = ReadReceipt(receipt, source, accountId, initialReel, nitroReel);
                if (stage.Exists || Text(receipt, "operationId") is not null)
                    ValidateNativeReceipt(receipt, ReadFrozen(stage, source, accountId, id), replay);
                return new(null, replay, round.WinPointsForRound(source.RoundId));
            }

            var guardOffer = ReadNitroOffer(guard);
            if (stage.Exists)
            {
                var frozen = ReadFrozen(stage, source, accountId, id);
                ValidateOwnedGuard(guard, round, source, frozen);
                if (initialReel is { } requested && requested != frozen.Outcome.ReelIndex ||
                    nitroReel is not null && frozen.PendingOffer is null && frozen.ChosenNitroReel != nitroReel)
                    throw AlreadyUsed();
                if (nitroReel is not { } choice || frozen.PendingOffer is null)
                    return new(frozen, null, round.WinPoints);

                var final = ResolveNitro(source, frozen, choice);
                transaction.Update(stageRef, StageFields(final));
                return new(final, null, round.WinPoints);
            }

            if (HasForeignOwner(guard) || round.HasActiveAdmission || Text(guard, SlotSpinGuard.PreparedAdmissionField) is not null ||
                Text(guard, PendingAdjustmentField) is not null)
                throw Unavailable();

            NeonCarRespinOutcome outcome;
            NeonNitroChoiceOffer? pending = null;
            if (guardOffer is not null)
            {
                if (guardOffer is not { SourceKind: NeonNitroChoice.CarRespinSource, ReelIndex: { } oldReel } ||
                    guardOffer.SpinId != spinId || initialReel is { } requested && requested != oldReel ||
                    Text(guard, NitroOutcomeField) is not { } pendingJson || !round.CanRespin(source.RoundId, spinId, oldReel))
                    throw new NeonCarActionConflictException("Finish the pending Nitro choice first.");
                outcome = NeonNitroChoice.ReadSavedCarRespin(pendingJson, source.Original, guardOffer);
                pending = guardOffer;
            }
            else
            {
                if (initialReel is not { } reel || !round.CanRespin(source.RoundId, spinId, reel)) throw Unavailable();
                if (!NeonCarRespin.MatchesOfferedReel(source.Original, spinId, reel, source.UsesLegacyRoundReceipt))
                    throw InvalidState();
                outcome = new NeonCarRespin(draw).Resolve(source.Original, reel);
                if (outcome.NeonNitroWild?.RequiresChoice == true)
                {
                    var remaining = NeonPrimaryFeatures.ReadCombinedAwarded(guard, account.LegacyFreeGamesRemaining).Remaining;
                    pending = NeonNitroChoice.CreateCarRespinOffer(source.Original, outcome,
                        checked((int)remaining), round.WinPoints);
                }
            }

            var prepared = new FrozenRespin(id, accountId, spinId, source.RoundId, source.ReceiptId, source.Hash,
                outcome, pending, null, PointsToCents(source, outcome.ImprovementPoints), clock.GetUtcNow().UtcDateTime)
            { Observation = ReadObservation(accountId) };
            if (nitroReel is { } selected) prepared = ResolveNitro(source, prepared, selected);
            ValidateFrozen(source, prepared);

            var guardData = new Dictionary<string, object>
            {
                [WorkerOwnerField] = WorkerOwner,
                [SlotSpinGuard.ActiveAdmissionField] = id,
                [SlotSpinGuard.PreparedAdmissionField] = id,
                [PendingAdjustmentField] = id,
                ["updatedAt"] = Timestamp.FromDateTime(prepared.StartedAtUtc)
            };
            // A formerly issued offer remains visible until this frozen adjustment settles.
            if (pending is not null)
            {
                guardData[NitroOfferField] = JsonSerializer.Serialize(pending);
                guardData[NitroOutcomeField] = JsonSerializer.Serialize(outcome);
            }
            transaction.Create(stageRef, StageFields(prepared));
            transaction.Update(guardRef, guardData);
            return new(prepared, null, round.WinPoints);
        }, cancellationToken: token);

    private Task<NeonCarRespinResponse> CompleteAsync(string accountId, Guid spinId, FrozenRespin requested,
        GameWalletSettlementReceipt walletReceipt) => database.RunTransactionAsync(async transaction =>
    {
        var source = ReadSource(await transaction.GetSnapshotAsync(Source(spinId), CancellationToken.None), accountId, spinId);
        var guardRef = Guard(accountId);
        var stageRef = database.Collection(Adjustments).Document(requested.Id);
        var receiptRef = Receipt(source);
        var snapshots = await transaction.GetAllSnapshotsAsync([receiptRef, guardRef, stageRef], CancellationToken.None);
        var frozen = ReadFrozen(snapshots[2], source, accountId, requested.Id);
        if (Hash(frozen) != Hash(requested)) throw InvalidState();
        var guard = snapshots[1];
        var round = ReadGuard(guard);
        if (snapshots[0].Exists)
        {
            if (Text(snapshots[0], "walletRoundSettlementId") != walletReceipt.SettlementId) throw InvalidState();
            var replay = ReadReceipt(snapshots[0], source, accountId, frozen.Outcome.ReelIndex, frozen.ChosenNitroReel);
            ValidateNativeReceipt(snapshots[0], frozen, replay);
            return NeonCarRespinResponse.From(replay, walletReceipt.BalanceAfterCents, true, round.WinPointsForRound(source.RoundId));
        }

        ValidateOwnedGuard(guard, round, source, frozen);
        var completed = round.CompleteRespin(frozen.Outcome.ImprovementPoints);
        var completedAt = walletReceipt.SettledAtUtc.UtcDateTime;
        var fields = new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = string.Empty,
            [SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete,
            [PendingAdjustmentField] = FieldValue.Delete,
            [NitroOfferField] = string.Empty,
            [NitroOutcomeField] = string.Empty,
            ["updatedAt"] = Timestamp.FromDateTime(completedAt)
        };
        completed.WriteRespinGuard(fields);
        transaction.Update(guardRef, fields);
        var finalStage = StageFields(WithoutObservation(frozen));
        finalStage["settledAtUtc"] = Timestamp.FromDateTime(completedAt);
        finalStage["walletRoundSettlementId"] = walletReceipt.SettlementId;
        transaction.Update(stageRef, finalStage);
        NeonCarRespinReport.Write(transaction, database, receiptRef, source, frozen, walletReceipt);
        return NeonCarRespinResponse.From(frozen.Outcome, walletReceipt.BalanceAfterCents, false, completed.WinPoints);
    }, cancellationToken: CancellationToken.None);

    private static FrozenRespin ResolveNitro(CarSource source, FrozenRespin frozen, int selectedReel)
    {
        if (frozen.PendingOffer?.Allows(frozen.SpinId, NeonNitroChoice.CarRespinSource, selectedReel) != true)
            throw Unavailable();
        var outcome = NeonCarRespin.ResolveChoice(source.Original, frozen.Outcome, selectedReel);
        return frozen with
        {
            Outcome = outcome, PendingOffer = null, ChosenNitroReel = selectedReel,
            PayoutCents = PointsToCents(source, outcome.ImprovementPoints), ChoiceObservation = ReadObservation(frozen.AccountId)
        };
    }

    private static void ValidateOwnedGuard(DocumentSnapshot guard, NeonCarRoundState round, CarSource source, FrozenRespin frozen)
    {
        if (Text(guard, WorkerOwnerField) != WorkerOwner || Text(guard, PendingAdjustmentField) != frozen.Id ||
            Text(guard, SlotSpinGuard.ActiveAdmissionField) != frozen.Id ||
            Text(guard, SlotSpinGuard.PreparedAdmissionField) != frozen.Id ||
            !(round with { HasActiveAdmission = false }).CanRespin(source.RoundId, frozen.SpinId, frozen.Outcome.ReelIndex))
            throw InvalidState();

        if (frozen.PendingOffer is { } offer && JsonSerializer.Serialize(ReadNitroOffer(guard)) != JsonSerializer.Serialize(offer))
            throw InvalidState();
    }

    private static FrozenRespin ReadFrozen(DocumentSnapshot document, CarSource source, string accountId, string id)
    {
        if (Text(document, "frozenJson") is not { } json || Text(document, "frozenSha256") != GameSecurityEventDocuments.Hash(json) ||
            JsonSerializer.Deserialize<FrozenRespin>(json) is not { } frozen || frozen.Id != id || frozen.AccountId != accountId)
            throw InvalidState();
        ValidateFrozen(source, frozen);
        return frozen;
    }

    private static void ValidateFrozen(CarSource source, FrozenRespin frozen)
    {
        if (frozen.SpinId != source.Original.SpinId || frozen.RoundId != source.RoundId || frozen.ReceiptId != source.ReceiptId ||
            frozen.SourceSha256 != source.Hash || frozen.PayoutCents != PointsToCents(source, frozen.Outcome.ImprovementPoints) ||
            !NeonCarRespin.MatchesOfferedReel(source.Original, frozen.SpinId, frozen.Outcome.ReelIndex, source.UsesLegacyRoundReceipt) ||
            (frozen.Outcome.NeonNitroWild?.RequiresChoice == true) != (frozen.PendingOffer is not null) ||
            frozen.ChosenNitroReel is { } reel && frozen.Outcome.NeonNitroWild?.ExpandedReel != reel ||
            NeonCarRespin.Check(source.Original, frozen.Outcome).Count != 0)
            throw InvalidState();
    }

    private static NeonCarRespinOutcome ReadReceipt(DocumentSnapshot receipt, CarSource source, string accountId,
        int? initialReel, int? nitroReel)
    {
        if (Text(receipt, "userId") != accountId || Text(receipt, "sourceSpinId") != source.Original.SpinId.ToString("N") ||
            initialReel is { } initial && Number(receipt, "reelIndex") != initial ||
            nitroReel is { } selected && (!receipt.ContainsField("neonNitroChosenReel") || Number(receipt, "neonNitroChosenReel") != selected))
            throw AlreadyUsed();
        if (Text(receipt, "roundId") != source.RoundId || Text(receipt, "sourceSha256") != source.Hash ||
            Text(receipt, "outcomeJson") is not { } json || JsonSerializer.Deserialize<NeonCarRespinOutcome>(json) is not { } outcome ||
            outcome.ReelIndex != Number(receipt, "reelIndex") || outcome.NeonNitroWild?.RequiresChoice == true ||
            receipt.ContainsField("neonNitroChosenReel") && Number(receipt, "neonNitroChosenReel") is >= 0 and var chosen &&
                outcome.NeonNitroWild?.ExpandedReel != chosen ||
            Number(receipt, "improvementPoints") != outcome.ImprovementPoints ||
            Number(receipt, "improvementCents") != PointsToCents(source, outcome.ImprovementPoints) ||
            NeonCarRespin.Check(source.Original, outcome).Count != 0)
            throw InvalidState();
        return outcome;
    }

    private static void ValidateNativeReceipt(DocumentSnapshot receipt, FrozenRespin frozen, NeonCarRespinOutcome replay)
    {
        if (frozen.PendingOffer is not null ||
            Text(receipt, "operationId") != frozen.Id || Text(receipt, "outcomeSha256") != Hash(frozen) ||
            Text(receipt, "walletRoundSettlementId") != GameWalletSettlementIdentity.CreateId(GameId, frozen.AccountId, frozen.Id) ||
            Number(receipt, "neonNitroChosenReel") != ((long?)frozen.ChosenNitroReel ?? -1) ||
            JsonSerializer.Serialize(replay) != JsonSerializer.Serialize(frozen.Outcome))
            throw InvalidState();
    }

    private static CarSource ReadSource(DocumentSnapshot snapshot, string accountId, Guid spinId)
    {
        if (Text(snapshot, "userId") != accountId || Text(snapshot, "gameId") != GameId ||
            NeonCarRoundState.ReadFrozenSource(field => Text(snapshot, field)) is not { } source)
            throw new NeonCarActionConflictException("This Car Play is no longer available for a reel respin.");
        if (source.ReceiptId.Contains('/') || source.ReceiptId.Contains('\\') ||
            JsonSerializer.Deserialize<NeonSpinOutcome>(source.OutcomeJson) is not { } original || original.GameId != GameId ||
            original.SpinId != spinId || !snapshot.TryGetValue<Timestamp>("createdAt", out var createdAt))
            throw InvalidState();
        return new(source.RoundId, source.ReceiptId, source.UsesLegacyRoundReceipt, original,
            GameSecurityEventDocuments.Hash(source.OutcomeJson), createdAt.ToDateTime());
    }

    private static void ValidateWallet(FrozenRespin frozen, GameWalletSettlementReceipt receipt)
    {
        if (receipt.SettlementId != GameWalletSettlementIdentity.CreateId(GameId, frozen.AccountId, frozen.Id) ||
            receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch ||
            receipt.BalanceBeforeCents < 0 ||
            receipt.BalanceAfterWagerCents != receipt.BalanceBeforeCents || receipt.WagerTransactionId is not null ||
            receipt.LegacyFreeGameTransactionId is not null ||
            receipt.BalanceAfterCents != checked(receipt.BalanceBeforeCents + frozen.PayoutCents) ||
            (frozen.PayoutCents != 0) != !string.IsNullOrWhiteSpace(receipt.PayoutTransactionId) ||
            receipt.SettledAtUtc.UtcDateTime < frozen.StartedAtUtc.AddSeconds(-1))
            throw new GameWalletUnavailableException();
    }

    private DocumentReference Guard(string accountId) => database.Collection(SlotSpinGuard.Collection)
        .Document(SlotSpinGuard.DocumentId(accountId, GameId));
    private DocumentReference Source(Guid spinId) => database.Collection("slotSpinResults").Document(spinId.ToString("N"));
    private DocumentReference Receipt(CarSource source) => database.Collection(NeonCarRespinReport.ReceiptCollection).Document(source.ReceiptId);

    private static string AdjustmentId(string accountId, Guid spinId, string receiptId) =>
        GameSecurityEventDocuments.Hash($"neon-car-respin\n{accountId}\n{spinId:N}\n{receiptId}")[..32];

    private static Dictionary<string, object> StageFields(FrozenRespin frozen)
    {
        var json = JsonSerializer.Serialize(frozen);
        return new()
        {
            ["userId"] = frozen.AccountId, ["sourceSpinId"] = frozen.SpinId.ToString("N"),
            ["frozenJson"] = json, ["frozenSha256"] = GameSecurityEventDocuments.Hash(json)
        };
    }

    // Wallet and retry identity remain stable after request metadata moves into retained audit evidence.
    internal static string Hash(FrozenRespin frozen) => GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(WithoutObservation(frozen)));

    private static FrozenRespin WithoutObservation(FrozenRespin frozen) => frozen with { Observation = null, ChoiceObservation = null };
    private static long PointsToCents(CarSource source, long points)
    {
        var cents = checked(points * source.Original.PointValueInCents);
        return cents >= 0 && cents == decimal.Truncate(cents) ? checked((long)cents) : throw InvalidState();
    }

    private static long Balance(GameWalletAccountState account) => account.SlotsCreditsBalanceCents is { } cents && cents >= 0
        ? cents : throw new GameWalletUnavailableException();
    private static NeonCarRoundState ReadGuard(DocumentSnapshot guard) =>
        NeonCarRoundState.ReadGuard(field => Text(guard, field), field => Number(guard, field));
    private static NeonNitroChoiceOffer? ReadNitroOffer(DocumentSnapshot guard) => NeonNitroChoice.ReadOffer(Text(guard, NitroOfferField));
    private static bool HasForeignOwner(DocumentSnapshot guard) => Text(guard, WorkerOwnerField) is not null and not WorkerOwner;

    private static GameRequestObservation? ReadObservation(string accountId)
    {
        var observation = GameSecurityContext.Current;
        if (observation is not null && (observation.UserId != accountId || observation.GameId != NeonCarRespinEndpoints.AuditGameId))
            throw InvalidState();
        return observation;
    }

    private static string? Text(DocumentSnapshot document, string field) =>
        document.Exists && document.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    private static long Number(DocumentSnapshot document, string field) =>
        document.Exists && document.TryGetValue<long>(field, out var value) ? value : 0;

    private static NeonCarActionConflictException Unavailable() => new("Choose an available reel from the latest Car Play.");
    private static NeonCarActionConflictException AlreadyUsed() => new("This Car Play has already used its reel respin.");
    private static InvalidOperationException InvalidState() => new("The saved Neon Car respin does not replay from its frozen source.");

    private sealed record PreparedRespin(FrozenRespin? Frozen, NeonCarRespinOutcome? Replay, long? RoundWinPoints);
    internal sealed record CarSource(string RoundId, string ReceiptId, bool UsesLegacyRoundReceipt, NeonSpinOutcome Original,
        string Hash, DateTime CreatedAtUtc);
    internal sealed record FrozenRespin(string Id, string AccountId, Guid SpinId, string RoundId, string ReceiptId,
        string SourceSha256, NeonCarRespinOutcome Outcome, NeonNitroChoiceOffer? PendingOffer, int? ChosenNitroReel,
        long PayoutCents, DateTime StartedAtUtc)
    {
        public GameRequestObservation? Observation { get; init; }
        public GameRequestObservation? ChoiceObservation { get; init; }
    }
}
