using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

public sealed partial class NeonPrimaryRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "neonPreparedRounds";
    private const string GameId = NeonArcadeSlotDefinitions.GameId;
    private const string Owner = NeonCarRespinStore.WorkerOwner;
    private const string OwnerField = NeonCarRespinStore.WorkerOwnerField;
    private const string CarAdjustmentField = NeonCarRespinStore.PendingAdjustmentField;
    private const string PendingNitroField = "pendingNeonNitroChoiceJson";
    private const string NitroAdmissionField = "pendingNeonNitroAdmissionId";
    private const string OperationCollection = "slotPreparedOperations";
    private const string SpinIndexCollection = "neonPrimarySpinOperations";
    private const string LegacyAdmissionCollection = "slotSpinAdmissions";
    internal const string SpinResultCollection = "slotSpinResults";
    private const string Pending = "pending";
    private const string Settled = "settled";
    private const string Declined = "declined";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);
    internal static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    internal Task<NeonPrimaryStoredRound?> FindAsync(string account, NeonRoundRequest request, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var index = await transaction.GetSnapshotAsync(OperationDocument(account, request.OperationId), token);
            if (!index.Exists) return null;
            var guard = await transaction.GetSnapshotAsync(GuardDocument(account), token);
            return await ReadIndexedAsync(transaction, index, guard, account, request, token);
        }, cancellationToken: token);

    internal Task<NeonPrimaryStoredRound> PrepareAsync(string account, NeonRoundRequest request, long legacyCount,
        CancellationToken token)
    {
        var operation = OperationDocument(account, request.OperationId);
        var guard = GuardDocument(account);
        var observation = GameSecurityContext.Current;
        if (observation is not null && (observation.UserId != account || observation.GameId != NeonPrimaryRouteContract.AuditGameId))
            throw new ArgumentException("The Neon request belongs to a different account or game.");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], token);
            if (snapshots[0].Exists)
                return await ReadIndexedAsync(transaction, snapshots[0], snapshots[1], account, request, token);

            var state = snapshots[1];
            var now = clock.GetUtcNow().UtcDateTime;
            if (await ReadPendingAsync(transaction, state, account, token) is { } pending)
                throw new NeonPrimaryRoundConflictException("Finish the prepared Neon spin before starting another one.", pending.Request);
            if (IsBusy(state))
                throw new NeonPrimaryRoundConflictException("Finish the current Neon spin before starting another one.");
            if (LastStarted(state) is { } started && started + Cooldown > now)
                throw new NeonPrimaryRoundCooldownException(started + Cooldown - now);

            var round = Prepare(account, request, NeonPrimaryFeatures.Read(state, legacyCount), now, observation);
            var raw = RoundDocument(round.AdmissionId);
            var index = Identity(round);
            index["requestSha256"] = RequestDigest(request);
            var rawFields = Identity(round);
            rawFields["status"] = Pending;
            rawFields["startedAtUtc"] = Timestamp.FromDateTime(now);
            rawFields["requestId"] = observation?.RequestId ?? string.Empty;
            WriteFrozen(rawFields, round);

            var guardFields = new Dictionary<string, object>
            {
                ["userId"] = account, [OwnerField] = Owner,
                [SlotSpinGuard.ActiveAdmissionField] = round.AdmissionId.ToString("N"),
                [SlotSpinGuard.PreparedAdmissionField] = round.AdmissionId.ToString("N"),
                [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(now), ["updatedAt"] = Timestamp.FromDateTime(now)
            };
            transaction.Create(raw, rawFields);
            transaction.Create(operation, index);
            transaction.Create(SpinIndexDocument(round.Outcome.SpinId), Identity(round));
            transaction.Set(guard, guardFields, SetOptions.MergeAll);
            return new NeonPrimaryStoredRound(round, null);
        }, cancellationToken: token);
    }

    private NeonPrimaryPreparedRound Prepare(string account, NeonRoundRequest request, NeonPrimaryFeatures state,
        DateTime now, GameRequestObservation? observation)
    {
        var awarded = state.Awarded;
        if (request.UseFreeSpin && awarded.Remaining == 0)
            throw new NeonPrimaryFreeSpinsException();
        if (!request.UseFreeSpin && awarded.Remaining > 0)
            throw new NeonPrimaryFreeSpinsRequiredException();

        var engine = new NeonNightsEngine(random);
        engine.ValidateRequest(request.WagerPoints, request.UseFreeSpin);
        var wager = request.UseFreeSpin ? awarded.LockedWagerPoints!.Value : request.WagerPoints;
        var (isDisco, disco) = state.Disco.Admit(request.UseFreeSpin, wager);
        var outcome = engine.Spin(wager, account, false, 0, awarded.FeatureMode, state.Pity, request.UseFreeSpin,
            isDisco, isDisco ? disco.Multiplier : NeonDiscoCascade.InitialMultiplier);
        var chargesWager = NeonPlayPolicy.ChargesWager(request.UseFreeSpin, awarded.FeatureMode);
        var outcomeDigest = OutcomeDigest(outcome);
        var round = new NeonPrimaryPreparedRound(Guid.NewGuid(), account, request, state, outcome,
            outcomeDigest, chargesWager ? wager : 0, request.UseFreeSpin && state.Legacy.Remaining > 0,
            isDisco, null, now, observation, outcome.NeonNitroWild?.RequiresChoice == true ? outcomeDigest : null);
        ValidateRound(round);
        return round;
    }

    private static void ValidateRound(NeonPrimaryPreparedRound round)
    {
        var outcome = round.Outcome;
        var awarded = round.Before.Awarded;
        var expectedWager = round.Request.UseFreeSpin ? awarded.LockedWagerPoints : round.Request.WagerPoints;
        var (disco, admittedDisco) = round.Before.Disco.Admit(round.Request.UseFreeSpin, outcome.WagerPoints);
        var expectedCharge = NeonPlayPolicy.ChargesWager(round.Request.UseFreeSpin, awarded.FeatureMode) ? expectedWager : 0;
        if (outcome.SpinId == Guid.Empty || outcome.GameId != GameId || outcome.WagerPoints != expectedWager ||
            outcome.IsFreeSpin != round.Request.UseFreeSpin || outcome.PointValueInCents != 1m ||
            round.Request.UseFreeSpin != (awarded.Remaining > 0) || round.WagerCents != expectedCharge ||
            round.SpendsLegacy != (round.Request.UseFreeSpin && round.ExpectedLegacy > 0) || outcome.SpecialBoostApplied ||
            outcome.EngineAudit.PityMissesBefore != round.Before.Pity || outcome.EngineAudit.FreeSpinFeatureMode != awarded.FeatureMode ||
            round.IsDisco != disco || disco != (outcome.NeonDiscoCascade is not null) ||
            disco && outcome.EngineAudit.NeonDiscoCascade?.StartingMultiplier != admittedDisco.Multiplier ||
            (round.ReservationSha256 is not null) != (outcome.NeonNitroWild?.RequiresChoice == true || round.ChosenReel.HasValue) ||
            (round.ReservedOutcome is not null) != round.ChosenReel.HasValue ||
            outcome.NeonNitroWild?.RequiresChoice == true && round.ReservationSha256 != round.OutcomeSha256 ||
            round.ChosenReel is { } chosen && (outcome.NeonNitroWild?.ExpandedReel != chosen || round.Reservation is null) ||
            outcome.Payout.TotalPoints < 0 || round.OutcomeSha256 != OutcomeDigest(outcome) ||
            NeonOutcomeReplay.Check(outcome, outcome.Payout).Count != 0)
            throw new GameWalletUnavailableException();
        if (round.Reservation is { } hold)
            ValidateReservation(round, hold, expectDecline: false);
        if (round.ReservedOutcome is { } reserved && round.ChosenReel is { } selected &&
            (reserved.SpinId != outcome.SpinId || OutcomeDigest(reserved) != round.ReservationSha256 ||
             OutcomeDigest(NeonNightsEngine.ResolveNitroChoice(reserved, selected)) != round.OutcomeSha256))
            throw new GameWalletUnavailableException();
        var next = round.Before.Settle(round, round.ExpectedLegacy - (round.SpendsLegacy ? 1 : 0));
        _ = checked((int)next.Awarded.Remaining);
    }

    internal Task<NeonRoundResult> FinalizeAsync(NeonPrimaryPreparedRound round, GameWalletSettlementReceipt wallet,
        CancellationToken token)
    {
        ValidateFinalWallet(round, wallet);
        wallet = wallet with { WasAlreadySettled = false };
        var raw = RoundDocument(round.AdmissionId);
        var guard = GuardDocument(round.AccountId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([raw, guard], token);
            var saved = ReadRound(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (saved.Round.OutcomeSha256 != round.OutcomeSha256 || saved.Round.Request != round.Request ||
                saved.Round.ChosenReel != round.ChosenReel || saved.Round.WagerCents != round.WagerCents ||
                saved.Round.ExpectedLegacy != round.ExpectedLegacy || saved.Round.Reservation != round.Reservation)
                throw new GameWalletUnavailableException();
            if (saved.Final is { } prior)
                return prior.Wallet == wallet ? prior : throw new GameWalletUnavailableException();

            ValidateGuard(snapshots[1], saved.Round);
            var walletTime = wallet.SettledAtUtc.UtcDateTime;
            var completedAt = walletTime < round.StartedAtUtc ? round.StartedAtUtc : walletTime;
            var approved = !IsDecline(wallet);
            var guardFields = new Dictionary<string, object>
            {
                [SlotSpinGuard.ActiveAdmissionField] = string.Empty,
                [SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete,
                ["updatedAt"] = Timestamp.FromDateTime(completedAt)
            };
            if (round.ChosenReel.HasValue)
            {
                guardFields[NitroAdmissionField] = FieldValue.Delete;
                guardFields[PendingNitroField] = FieldValue.Delete;
            }
            var rawFields = new Dictionary<string, object>
            {
                ["status"] = approved ? Settled : Declined,
                ["completedAt"] = Timestamp.FromDateTime(completedAt)
            };

            NeonSpinReply? reply = null;
            if (approved)
            {
                var settledLegacyCount = (round.Reservation ?? wallet).LegacyFreeGamesRemaining!.Value;
                var next = round.Before.Settle(round, settledLegacyCount);
                var isCar = NeonCarRoundState.IsCarPlay(round.Request.UseFreeSpin, round.Before.Awarded.FeatureMode);
                next.Write(guardFields);
                reply = NeonSpinReply.Create(round.Outcome, round.WagerCents, wallet.BalanceAfterCents,
                    NeonRoundStateReply.Create(next.Awarded, next.Disco, next.Car, consumedDisco: round.IsDisco, consumedCar: isCar))
                    with { NeonCarRoundWinPoints = isCar ? next.Car.WinPoints : null };
                NeonPrimarySpinReport.Write(transaction, database, round, next, reply, wallet, completedAt);
            }
            if (round.Observation is not null)
            {
                rawFields["requestId"] = FieldValue.Delete;
                WriteFrozen(rawFields, round with { Observation = null });
            }

            var result = new NeonRoundResult(reply, null, wallet) { ChargedWagerCents = round.WagerCents };
            var resultJson = JsonSerializer.Serialize(result, Json);
            rawFields["resultJson"] = resultJson;
            rawFields["resultSha256"] = GameSecurityEventDocuments.Hash(resultJson);
            transaction.Set(raw, rawFields, SetOptions.MergeAll);
            transaction.Set(guard, guardFields, SetOptions.MergeAll);
            return result;
        }, cancellationToken: token);
    }

    private static void ValidateFinalWallet(NeonPrimaryPreparedRound round, GameWalletSettlementReceipt wallet)
    {
        if (round.ReservationSha256 is null)
        {
            ValidateDebitReceipt(round, wallet, round.PayoutCents);
            return;
        }
        if (round.Reservation is null)
        {
            ValidateReservation(round, wallet, expectDecline: true);
            return;
        }
        var spin = round.Outcome.SpinId.ToString("N");
        if (round.ChosenReel is null || IsDecline(wallet) ||
            wallet.SettlementId != GameWalletSettlementIdentity.CreateCompletionId(GameId, round.AccountId, spin) ||
            wallet.WagerTransactionId is not null || wallet.LegacyFreeGameTransactionId is not null ||
            wallet.PayoutTransactionId != (round.PayoutCents > 0 ? $"{spin}-payout" : null) ||
            wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents != wallet.BalanceBeforeCents ||
            wallet.BalanceAfterCents != checked(wallet.BalanceBeforeCents + round.PayoutCents) ||
            wallet.LegacyFreeGamesRemaining < 0)
            throw new GameWalletUnavailableException();
    }

    private static void ValidateReservation(NeonPrimaryPreparedRound round, GameWalletSettlementReceipt wallet, bool expectDecline)
    {
        if (round.ReservationSha256 is null || IsDecline(wallet) != expectDecline)
            throw new GameWalletUnavailableException();
        ValidateDebitReceipt(round, wallet, payoutCents: 0);
    }

    private static void ValidateDebitReceipt(NeonPrimaryPreparedRound round, GameWalletSettlementReceipt wallet, long payoutCents)
    {
        var spin = round.Outcome.SpinId.ToString("N");
        var declined = IsDecline(wallet);
        var expectedRemaining = round.ExpectedLegacy - (round.SpendsLegacy ? 1 : 0);
        var countOkay = wallet.LegacyFreeGamesCountMismatch
            ? wallet.LegacyFreeGamesRemaining >= 0 && wallet.LegacyFreeGamesRemaining != round.ExpectedLegacy
            : wallet.LegacyFreeGamesRemaining == (declined ? round.ExpectedLegacy : expectedRemaining);
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(GameId, round.AccountId, spin) ||
            !countOkay || wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents < 0 || wallet.BalanceAfterCents < 0 ||
            (wallet.InsufficientFunds ? 1 : 0) + (wallet.InsufficientLegacyFreeGames ? 1 : 0) +
                (wallet.LegacyFreeGamesCountMismatch ? 1 : 0) > 1 ||
            wallet.WagerTransactionId != (!declined && round.WagerCents > 0 ? $"{spin}-wager" : null) ||
            wallet.PayoutTransactionId != (!declined && payoutCents > 0 ? $"{spin}-payout" : null) ||
            wallet.LegacyFreeGameTransactionId != (!declined && round.SpendsLegacy ? $"{spin}-free-game-use" : null) ||
            wallet.BalanceAfterWagerCents != wallet.BalanceBeforeCents - (declined ? 0 : round.WagerCents) ||
            wallet.BalanceAfterCents != checked(wallet.BalanceAfterWagerCents + (declined ? 0 : payoutCents)))
            throw new GameWalletUnavailableException();
    }

    private static bool IsDecline(GameWalletSettlementReceipt wallet) =>
        wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch;

    private async Task<NeonPrimaryStoredRound> ReadIndexedAsync(Transaction transaction, DocumentSnapshot index,
        DocumentSnapshot guard, string account, NeonRoundRequest request, CancellationToken token)
    {
        var admission = ValidateIdentity(index, account, request.OperationId);
        if (Text(index, "requestSha256") != RequestDigest(request))
            throw new NeonPrimaryRoundConflictException("This operation ID was already used for different spin options.");
        var raw = await transaction.GetSnapshotAsync(RoundDocument(admission), token);
        var saved = ReadRound(raw, admission, account, request.OperationId);
        if (saved.Round.Request != request)
            throw new GameWalletUnavailableException();
        if (saved.Final is null)
        {
            ValidateGuard(guard, saved.Round);
            ValidateRound(saved.Round);
        }
        return saved;
    }

    private static NeonPrimaryStoredRound ReadRound(DocumentSnapshot snapshot, Guid admission, string account, string? operation = null)
    {
        if (ValidateIdentity(snapshot, account, operation) != admission)
            throw new GameWalletUnavailableException();
        var round = ReadChecked<NeonPrimaryPreparedRound>(snapshot, "prepared");
        if (round.AdmissionId != admission || round.AccountId != account || round.Request.OperationId != Text(snapshot, "operationId") ||
            round.Outcome.SpinId.ToString("N") != Text(snapshot, "spinId"))
            throw new GameWalletUnavailableException();
        var status = Text(snapshot, "status");
        if (status == Pending) return new(round, null);
        if (status is not (Settled or Declined)) throw new GameWalletUnavailableException();
        var result = ReadChecked<NeonRoundResult>(snapshot, "result");
        if (result.Wallet is not { } wallet || result.PendingNeonNitroChoice is not null ||
            (status == Settled) != (result.Spin?.SpinId == round.Outcome.SpinId) ||
            (status == Declined) != IsDecline(wallet))
            throw new GameWalletUnavailableException();
        ValidateFinalWallet(round, wallet);
        return new(round, result);
    }

    private static T ReadChecked<T>(DocumentSnapshot snapshot, string prefix)
    {
        var json = Text(snapshot, prefix + "Json");
        if (json is null || Text(snapshot, prefix + "Sha256") != GameSecurityEventDocuments.Hash(json))
            throw new GameWalletUnavailableException();
        return JsonSerializer.Deserialize<T>(json, Json) ?? throw new GameWalletUnavailableException();
    }

    private static void WriteFrozen(IDictionary<string, object> fields, NeonPrimaryPreparedRound round)
    {
        var json = JsonSerializer.Serialize(round, Json);
        fields["preparedJson"] = json;
        fields["preparedSha256"] = GameSecurityEventDocuments.Hash(json);
    }

    private static void ValidateGuard(DocumentSnapshot guard, NeonPrimaryPreparedRound round)
    {
        var admission = round.AdmissionId.ToString("N");
        if (Text(guard, OwnerField) != Owner || Text(guard, SlotSpinGuard.ActiveAdmissionField) != admission ||
            Text(guard, SlotSpinGuard.PreparedAdmissionField) != admission || Text(guard, CarAdjustmentField) is not null ||
            NeonPrimaryFeatures.Read(guard, round.ExpectedLegacy) != round.Before)
            throw new GameWalletUnavailableException();
        var nitroAdmission = Text(guard, NitroAdmissionField);
        var offer = NeonNitroChoice.ReadOffer(Text(guard, PendingNitroField));
        if (round.Reservation is { } held)
        {
            if (nitroAdmission != admission || offer?.SpinId != round.Outcome.SpinId || offer.SourceKind != NeonNitroChoice.SpinSource ||
                offer.WagerPoints != round.Outcome.WagerPoints || offer.FreeSpinsRemaining != round.OfferedBalance ||
                offer.WalletBalanceAfterWagerCents != held.BalanceAfterWagerCents || offer.SelectedReel != round.ChosenReel)
                throw new GameWalletUnavailableException();
        }
        else if (nitroAdmission is not null || offer is not null)
            throw new GameWalletUnavailableException();
    }

    private static Dictionary<string, object> Identity(NeonPrimaryPreparedRound round) => new()
    {
        ["owner"] = Owner, ["gameId"] = GameId, ["userId"] = round.AccountId,
        ["operationId"] = round.Request.OperationId, ["admissionId"] = round.AdmissionId.ToString("N"),
        ["spinId"] = round.Outcome.SpinId.ToString("N")
    };

    private static Guid ValidateIdentity(DocumentSnapshot index, string account, string? operation)
    {
        if (Text(index, "owner") != Owner || Text(index, "gameId") != GameId || Text(index, "userId") != account ||
            operation is not null && Text(index, "operationId") != operation ||
            !Guid.TryParseExact(Text(index, "admissionId"), "N", out var admission))
            throw new GameWalletUnavailableException();
        return admission;
    }

    private async Task<NeonPrimaryPreparedRound?> ReadPendingAsync(Transaction transaction, DocumentSnapshot guard,
        string account, CancellationToken token)
    {
        if (Text(guard, OwnerField) != Owner || Text(guard, CarAdjustmentField) is not null ||
            !Guid.TryParseExact(Text(guard, SlotSpinGuard.PreparedAdmissionField), "N", out var pending))
            return null;
        var raw = await transaction.GetSnapshotAsync(RoundDocument(pending), token);
        var stored = ReadRound(raw, pending, account);
        if (stored.Final is not null) throw new GameWalletUnavailableException();
        ValidateGuard(guard, stored.Round);
        return stored.Round;
    }

    private static bool IsBusy(DocumentSnapshot guard) =>
        Text(guard, SlotSpinGuard.PreparedAdmissionField) is not null || Text(guard, CarAdjustmentField) is not null ||
        Text(guard, PendingNitroField) is not null ||
        Text(guard, SlotSpinGuard.ActiveAdmissionField) is not null;

    private static DateTime? LastStarted(DocumentSnapshot guard) =>
        guard.Exists && guard.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var started) ? started.ToDateTime() : null;

    private static string RequestDigest(NeonRoundRequest request) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(request, Json));

    private static string OutcomeDigest(NeonSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, Json));

    internal static string? Text(DocumentSnapshot document, string key) =>
        document.Exists && document.TryGetValue<string>(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    internal static long? Number(DocumentSnapshot document, string key) =>
        document.Exists && document.TryGetValue<long>(key, out var value) ? value : null;

    private DocumentReference GuardDocument(string account) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, GameId));
    private DocumentReference RoundDocument(Guid admission) => database.Collection(RawCollectionName).Document(admission.ToString("N"));
    private DocumentReference SpinIndexDocument(Guid spin) => database.Collection(SpinIndexCollection).Document(spin.ToString("N"));
    private DocumentReference OperationDocument(string account, string operation) =>
        database.Collection(OperationCollection).Document(GameSecurityEventDocuments.Hash($"{GameId}\n{account}\n{operation}"));
}
