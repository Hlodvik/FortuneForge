using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

public sealed partial class NeonPrimaryRoundStore
{
    private const string AdminCollection = "neonPrimaryAdminOperations";

    internal Task<NeonPrimaryStoredRound> PublishReservedNitroAsync(NeonPrimaryPreparedRound round,
        GameWalletSettlementReceipt wallet, CancellationToken token)
    {
        ValidateReservation(round, wallet, expectDecline: false);
        wallet = wallet with { WasAlreadySettled = false };
        var raw = RoundDocument(round.AdmissionId);
        var guard = GuardDocument(round.AccountId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([raw, guard], token);
            var saved = ReadRound(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (saved.Round.ReservationSha256 != round.ReservationSha256 || saved.Round.Request != round.Request)
                throw new GameWalletUnavailableException();
            if (saved.Round.Reservation is { } prior)
            {
                if (prior != wallet) throw new GameWalletUnavailableException();
                if (saved.Final is null) ValidateGuard(snapshots[1], saved.Round);
                return saved;
            }
            if (saved.Final is not null || saved.Round.OutcomeSha256 != round.OutcomeSha256 ||
                saved.Round.Outcome.NeonNitroWild?.RequiresChoice != true)
                throw new GameWalletUnavailableException();

            ValidateGuard(snapshots[1], saved.Round);
            var held = saved.Round with { Reservation = wallet };
            var changes = new Dictionary<string, object>();
            WriteFrozen(changes, held);
            transaction.Set(raw, changes, SetOptions.MergeAll);
            transaction.Set(guard, new Dictionary<string, object>
            {
                [NitroAdmissionField] = round.AdmissionId.ToString("N"),
                [PendingNitroField] = JsonSerializer.Serialize(CreateHeldOffer(held)),
                ["updatedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            }, SetOptions.MergeAll);
            return new NeonPrimaryStoredRound(held, null);
        }, cancellationToken: token);
    }

    internal static NeonNitroChoiceOffer CreateHeldOffer(NeonPrimaryPreparedRound round)
    {
        if (round.Reservation is not { } held || round.Outcome.NeonNitroWild is not { RequiresChoice: true } nitro)
            throw new GameWalletUnavailableException();
        return NeonNitroChoice.CreateSpinOffer(round.Outcome.SpinId, round.Outcome.WagerPoints, round.OfferedBalance, nitro)
            with { WalletBalanceAfterWagerCents = held.BalanceAfterWagerCents };
    }

    internal Task<NeonRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(GuardDocument(account), token);
            var pending = await ReadPendingAsync(transaction, guard, account, token);
            var features = pending?.Before ?? NeonPrimaryFeatures.Read(guard, wallet.LegacyFreeGamesRemaining);
            var offer = NeonNitroChoice.ReadOffer(Text(guard, PendingNitroField));
            if (offer?.SourceKind == NeonNitroChoice.SpinSource && Text(guard, OwnerField) == Owner && pending?.Reservation is null)
                throw new GameWalletUnavailableException();
            var awarded = pending?.Reservation is null ? features.Awarded :
                pending.OfferedBalance == 0 ? new(0, null, null) : features.Awarded with { Remaining = pending.OfferedBalance };
            var car = features.Car with { HasActiveAdmission = Text(guard, SlotSpinGuard.ActiveAdmissionField) is not null };
            return NeonRoundStateReply.Create(awarded, features.Disco, car, offer, pending?.Request) with
            {
                SlotsCreditsBalance = wallet.SlotsCreditsBalanceCents / 100m
            };
        }, cancellationToken: token);

    internal Task<NeonPrimaryStoredRound> SelectNitroAsync(string account, Guid spin, int reel, CancellationToken token)
    {
        if (spin == Guid.Empty || !NeonNitroChoice.IsChoiceReel(reel))
            throw new NeonPrimaryNitroConflictException("Choose one of the five reels for Nitro Wild.");
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([SpinIndexDocument(spin), GuardDocument(account)], token);
            var index = snapshots[0];
            var guard = snapshots[1];
            if (!index.Exists)
            {
                if (await IsExactLegacyNitroAsync(transaction, guard, account, spin, reel, token))
                    throw new NeonLegacyNitroChoiceException();
                throw new NeonPrimaryNitroConflictException("The pending Nitro choice is no longer available.");
            }
            if (Text(index, "userId") != account)
                throw new NeonPrimaryNitroConflictException("The pending Nitro choice is no longer available.");
            var admission = ValidateIdentity(index, account, null);
            var raw = RoundDocument(admission);
            var saved = ReadRound(await transaction.GetSnapshotAsync(raw, token), admission, account, Text(index, "operationId"));
            if (saved.Round.Outcome.SpinId != spin)
                throw new GameWalletUnavailableException();
            var round = saved.Round;
            if (round.ChosenReel is { } already)
            {
                if (already != reel)
                    throw new NeonPrimaryNitroConflictException("This Nitro choice has already been recorded for a different reel.");
                if (saved.Final is null)
                {
                    ValidateGuard(guard, round);
                    ValidateRound(round);
                }
                return saved;
            }
            if (saved.Final is not null || round.Reservation is null || round.Outcome.NeonNitroWild?.RequiresChoice != true)
                throw new NeonPrimaryNitroConflictException("The pending Nitro choice is no longer available.");

            ValidateGuard(guard, round);
            var offer = NeonNitroChoice.ReadOffer(Text(guard, PendingNitroField));
            if (Text(guard, NitroAdmissionField) != admission.ToString("N") ||
                offer?.Allows(spin, NeonNitroChoice.SpinSource, reel) != true)
                throw new GameWalletUnavailableException();
            var chosen = NeonNitroChoice.ResolveSavedSpin(JsonSerializer.Serialize(round.Outcome), spin,
                round.Outcome.WagerPoints, reel, offer);
            var resolved = round with
            {
                Outcome = chosen, OutcomeSha256 = OutcomeDigest(chosen), ChosenReel = reel, ReservedOutcome = round.Outcome
            };
            ValidateRound(resolved);
            var changes = new Dictionary<string, object>();
            WriteFrozen(changes, resolved);
            transaction.Set(raw, changes, SetOptions.MergeAll);
            transaction.Set(GuardDocument(account), new Dictionary<string, object>
            {
                [PendingNitroField] = JsonSerializer.Serialize(offer with { SelectedReel = reel }),
                ["updatedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            }, SetOptions.MergeAll);
            return new NeonPrimaryStoredRound(resolved, null);
        }, cancellationToken: token);
    }

    private async Task<bool> IsExactLegacyNitroAsync(Transaction transaction, DocumentSnapshot guard,
        string account, Guid spin, int reel, CancellationToken token)
    {
        var spinKey = spin.ToString("N");
        var result = await transaction.GetSnapshotAsync(database.Collection(SpinResultCollection).Document(spinKey), token);
        string? admissionId;
        if (result.Exists)
        {
            if (Text(result, "userId") != account || Text(result, "gameId") != GameId || Text(result, OwnerField) is not null ||
                Number(result, "neonNitroChosenReel") != reel || Text(result, "neonNitroFinalOutcomeJson") is null)
                return false;
            admissionId = Text(result, "admissionId");
        }
        else
        {
            if (Text(guard, OwnerField) is not null || Text(guard, CarAdjustmentField) is not null ||
                NeonNitroChoice.ReadOffer(Text(guard, PendingNitroField))?.Allows(spin, NeonNitroChoice.SpinSource, reel) != true)
                return false;
            admissionId = Text(guard, NitroAdmissionField);
            if (admissionId != Text(guard, SlotSpinGuard.ActiveAdmissionField))
                return false;
        }
        if (!Guid.TryParseExact(admissionId, "N", out var admission))
            return false;
        var record = await transaction.GetSnapshotAsync(database.Collection(LegacyAdmissionCollection).Document(admission.ToString("N")), token);
        return Text(record, "userId") == account && Text(record, "gameId") == GameId &&
            Text(record, "pendingNeonNitroSpinId") == spinKey && Text(record, "pendingNeonNitroOutcomeJson") is not null &&
            Text(record, "pendingNeonNitroAdmissionJson") is not null &&
            Text(record, "status") == (result.Exists ? Settled : "admitted");
    }

    internal async Task<bool?> FindGrantAsync(string account, string operation, long wager, bool car, CancellationToken token)
    {
        var recorded = await GrantDocument(account, operation).GetSnapshotAsync(token);
        return ReadGrant(recorded, account, operation, wager, car);
    }

    internal Task<bool> GrantAsync(string account, string operation, long wager, bool car, long legacyCount, CancellationToken token)
    {
        new NeonNightsEngine(random).ValidateRequest(wager);
        NeonArcadeSlotDefinitions.ValidateCarryWager(wager);
        var guard = GuardDocument(account);
        var grant = GrantDocument(account, operation);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([guard, grant], token);
            if (ReadGrant(snapshots[1], account, operation, wager, car) is { } recorded)
                return recorded;
            var now = clock.GetUtcNow().UtcDateTime;
            var before = NeonPrimaryFeatures.Read(snapshots[0], legacyCount);
            // A legacy admin may have granted while the signed wallet state request was in flight.
            // Its bank and carried wager are written together under this same guard.
            var pendingLegacyGrant = legacyCount == 0 && Number(snapshots[0], "freeSpinWagerCents") > 0;
            var granted = !IsBusy(snapshots[0]) && !pendingLegacyGrant && before.Awarded.Remaining == 0 && before.Disco.SpinsRemaining == 0;
            if (granted)
            {
                var fields = new Dictionary<string, object>
                {
                    ["userId"] = account, [OwnerField] = Owner, ["updatedAt"] = Timestamp.FromDateTime(now)
                };
                if (car)
                {
                    var mode = NeonArcadeBonus.PaidCarFeatureMode;
                    NeonPrimaryFeatures.WriteNativeAwarded(fields, new(NeonArcadeSlotDefinitions.SpecialPlaySpins, wager, mode));
                    NeonCarRoundState.Start(grant.Id, mode).WriteGuard(fields);
                }
                else
                    NeonDiscoRoundState.Grant(wager).WriteGuard(fields);
                transaction.Set(guard, fields, SetOptions.MergeAll);
            }
            transaction.Create(grant, new Dictionary<string, object>
            {
                ["gameId"] = GameId, ["userId"] = account, ["operationId"] = operation, ["wagerPoints"] = wager,
                ["feature"] = car ? "car" : "disco-ball", ["granted"] = granted, ["createdAt"] = Timestamp.FromDateTime(now)
            });
            return granted;
        }, cancellationToken: token);
    }

    private static bool? ReadGrant(DocumentSnapshot record, string account, string operation, long wager, bool car)
    {
        if (!record.Exists) return null;
        if (Text(record, "gameId") != GameId || Text(record, "userId") != account || Text(record, "operationId") != operation ||
            Number(record, "wagerPoints") != wager || Text(record, "feature") != (car ? "car" : "disco-ball"))
            throw new NeonPrimaryRoundConflictException("This preview operation ID was already used for different options.");
        return record.GetValue<bool>("granted");
    }

    private DocumentReference GrantDocument(string account, string operation) =>
        database.Collection(AdminCollection).Document(GameSecurityEventDocuments.Hash($"{GameId}\n{account}\n{operation}"));
}
