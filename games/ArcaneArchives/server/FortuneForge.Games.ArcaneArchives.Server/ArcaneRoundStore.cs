using System.Globalization;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.ArcaneArchives.Server;

/// <summary>Owns the durable Arcane round and the game's fields in the existing spin guard.</summary>
public sealed class ArcaneRoundStore(FirestoreDb database, IGameRandomSource? random = null, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly GameDefinition Game = ArcaneArchivesEngine.CreateGame();
    private static readonly TimeSpan SpinCooldown = TimeSpan.FromMilliseconds(500);
    private const string GameId = ArcaneArchivesEngine.GameId;
    private const string Owner = "arcane-game-v1";
    private const string GuardOwner = "arcanePaidRoundOwner";
    private const string Operations = "slotPreparedOperations";
    public const string RawCollectionName = "arcanePreparedRounds";
    private const string Pending = "pending";
    private const string Settled = "settled";
    private const string Declined = "declined";
    private const long CurrentIndexSchema = 2;
    private const long LegacyIndexSchema = 1;

    internal Task<ArcaneStoredRound> PrepareAsync(string accountId, ArcaneRoundRequest request, bool allowStart,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(request);
        if (!ArcaneRoundRequest.IsValidOperationId(request.OperationId))
            throw new ArgumentException("Spin operation IDs must contain 16 through 128 ASCII letters, digits, hyphens, or underscores.", nameof(request));
        if (request.UseSpecialBoost)
            throw new ArgumentException("Arcane Archives does not support the legacy special-point boost.", nameof(request));

        var operation = OperationDocument(accountId, request.OperationId);
        var guard = GuardDocument(accountId);
        var requestedTerms = RequestDigest(request);
        var observation = GameSecurityContext.Current;
        if (observation is not null && observation.UserId != accountId)
            throw new ArgumentException("The Arcane request belongs to a different account.", nameof(accountId));

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], cancellationToken);
            var index = snapshots[0];
            var guardSnapshot = snapshots[1];
            if (index.Exists)
            {
                if (Text(index, "requestSha256") != requestedTerms)
                    throw new ArcaneRoundConflictException("This spin operation ID was already used for different spin options.");
                if (Number(index, "schemaVersion") == LegacyIndexSchema && Text(index, "owner") is null && AdmissionId(index) != Guid.Empty)
                    throw new ArcaneObsoleteRoundException();
                ValidateIndex(index, accountId, request.OperationId);
                var existingId = AdmissionId(index);
                var existingRecord = await transaction.GetSnapshotAsync(RoundDocument(existingId), cancellationToken);
                var existing = ReadRound(existingRecord, existingId, accountId, request.OperationId);
                if (existing.Prepared.Round.Request != request)
                    throw Unavailable();
                if (existing.Final is null)
                {
                    var pendingRound = existing.Prepared.Round;
                    if (!OwnsGuard(guardSnapshot, pendingRound.AdmissionId)) throw Unavailable();
                    ValidateAdmittedFeatures(pendingRound, guardSnapshot, existingRecord);
                    ValidateEngine(pendingRound.Outcome, pendingRound.AccountId, pendingRound.Outcome.WagerPoints,
                        pendingRound.Request.UseFreeSpin, pendingRound.Features, pendingRound.PityMissesBefore);
                }
                return existing;
            }

            var now = UtcNow();
            if (await ReadPendingAsync(transaction, guardSnapshot, accountId, cancellationToken) is { } pending)
                throw new ArcaneRoundConflictException("Finish the prepared Arcane Archives spin before starting a new spin.", pending.Request);
            if (!allowStart)
                throw new ArcaneRoundStartPausedException();
            if (guardSnapshot.Exists && guardSnapshot.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var lastStarted) &&
                lastStarted.ToDateTime().Add(SpinCooldown) > now)
                throw new ArcaneRoundCooldownException(lastStarted.ToDateTime().Add(SpinCooldown) - now);

            var prepared = NewPreparation(accountId, request, guardSnapshot, now, observation);
            var serialized = JsonSerializer.Serialize(prepared, Json);
            // Complete preparation, unique operation index and guard ownership are committed together.
            var fields = new Dictionary<string, object>
            {
                ["owner"] = Owner, ["schemaVersion"] = CurrentIndexSchema,
                ["admissionId"] = prepared.Round.AdmissionId.ToString("N"), ["userId"] = accountId,
                ["gameId"] = GameId, ["operationId"] = request.OperationId, ["status"] = Pending,
                ["preparedJson"] = serialized, ["preparedSha256"] = GameSecurityEventDocuments.Hash(serialized),
                ["startedAtUtc"] = Timestamp.FromDateTime(now), ["spinId"] = prepared.Round.Outcome.SpinId.ToString("N"),
                ["requestId"] = observation?.RequestId ?? string.Empty,
            };
            ArcaneFeatureStore.WriteAdmission(fields, prepared.Round.Features);
            transaction.Create(RoundDocument(prepared.Round.AdmissionId), fields);
            transaction.Create(operation, new Dictionary<string, object>
            {
                ["owner"] = Owner, ["schemaVersion"] = CurrentIndexSchema,
                ["admissionId"] = prepared.Round.AdmissionId.ToString("N"), ["requestSha256"] = requestedTerms,
                ["userId"] = accountId, ["gameId"] = GameId, ["operationId"] = request.OperationId,
            });
            transaction.Set(guard, new Dictionary<string, object>
            {
                ["userId"] = accountId, [GuardOwner] = Owner,
                [SlotSpinGuard.ActiveAdmissionField] = prepared.Round.AdmissionId.ToString("N"),
                [SlotSpinGuard.PreparedAdmissionField] = prepared.Round.AdmissionId.ToString("N"),
                [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(now),
                ["updatedAt"] = Timestamp.FromDateTime(now),
            }, SetOptions.MergeAll);
            return new ArcaneStoredRound(prepared, null);
        }, cancellationToken: cancellationToken);
    }

    internal Task<ArcaneRoundResult> FinalizeAsync(ArcanePreparedRound round, GameWalletSettlementReceipt receipt,
        CancellationToken cancellationToken)
    {
        ValidateWallet(round, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var guard = GuardDocument(round.AccountId);
        var record = RoundDocument(round.AdmissionId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([record, guard], cancellationToken);
            var saved = ReadRound(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (!SameRound(saved.Prepared.Round, round))
                throw Unavailable();
            if (saved.Final is { } already)
            {
                if (already.Wallet != receipt) throw Unavailable();
                return already;
            }

            if (!OwnsGuard(snapshots[1], round.AdmissionId))
                throw Unavailable();
            var walletTime = receipt.SettledAtUtc.UtcDateTime;
            var completedAt = walletTime < round.StartedAtUtc ? round.StartedAtUtc : walletTime;
            var fields = new Dictionary<string, object>
            {
                [GuardOwner] = FieldValue.Delete,
                [SlotSpinGuard.ActiveAdmissionField] = string.Empty,
                [SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete,
                ["updatedAt"] = Timestamp.FromDateTime(completedAt),
            };

            var approved = !receipt.InsufficientFunds && !receipt.InsufficientLegacyFreeGames;
            ArcaneRoundResult result;
            if (approved)
            {
                ValidateAdmittedFeatures(saved.Prepared.Round, snapshots[1], snapshots[0]);
                var finalized = FinalRound(saved.Prepared.Round, receipt);
                var features = saved.Prepared.Settlement;
                ArcaneFeatureStore.WriteSettlement(fields, features);
                fields["consecutiveFiveMisses"] = finalized.Outcome.ConsecutiveFiveMisses;
                ApplyCarryFields(fields, finalized, receipt);
                result = new(finalized, features, receipt);
                ArcaneSpinReport.Write(transaction, database, finalized, features, result.SlotsCreditsBalance,
                    receipt.SettlementId, completedAt);
            }
            else
            {
                // A decline is durable, but prepared reels and feature progress are never returned.
                result = new(null, null, receipt);
            }

            var finalJson = JsonSerializer.Serialize(new ArcaneFinalReceipt(approved, receipt), Json);
            var finalFields = new Dictionary<string, object>
            {
                ["status"] = approved ? Settled : Declined,
                ["finalJson"] = finalJson, ["finalSha256"] = GameSecurityEventDocuments.Hash(finalJson),
                ["walletSettlementId"] = receipt.SettlementId, ["completedAtUtc"] = Timestamp.FromDateTime(completedAt),
            };
            if (!approved)
            {
                var redacted = saved.Prepared with { Round = saved.Prepared.Round with { Observation = null } };
                var json = JsonSerializer.Serialize(redacted, Json);
                finalFields["preparedJson"] = json;
                finalFields["preparedSha256"] = GameSecurityEventDocuments.Hash(json);
                finalFields["requestId"] = FieldValue.Delete;
            }
            transaction.Update(record, finalFields);
            transaction.Set(guard, fields, SetOptions.MergeAll);
            return result;
        }, cancellationToken: cancellationToken);
    }

    public Task<ArcaneRoundState> ReadAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(GuardDocument(accountId), cancellationToken);
            var pending = await ReadPendingAsync(transaction, guard, accountId, cancellationToken);
            return new ArcaneRoundState(ArcaneFeatureStore.ReadTome(guard), ArcaneFeatureStore.ReadPotions(guard),
                pending?.Request, false,
                ReadCarryWager(guard), Text(guard, "freeSpinFeatureMode"));
        }, cancellationToken: cancellationToken);
    }

    private ArcaneFrozenPreparation NewPreparation(string accountId, ArcaneRoundRequest request, DocumentSnapshot guard,
        DateTime now, GameRequestObservation? observation)
    {
        var carry = ReadCarryWager(guard);
        var effectiveWager = request.UseFreeSpin ? carry ?? request.WagerPoints : request.WagerPoints;
        if (request.UseFreeSpin && (request.WagerPoints < Game.Wagering.MinimumWagerPoints ||
            Game.Wagering.MaximumWagerPoints is { } maximum && request.WagerPoints > maximum))
            throw new ArgumentOutOfRangeException(nameof(request), "The bonus wager is outside Arcane Archives' stake bounds.");
        var features = ArcaneFeatureStore.Admit(guard, request.WagerPoints, request.UseFreeSpin);
        var potion = ArcanePotions.ForSpin(features.Potions, request.UseFreeSpin);
        var pity = checked((int)Number(guard, "consecutiveFiveMisses"));
        if (pity < 0 || features.Tome is null) throw Unavailable();

        var engine = new ArcaneArchivesEngine(random ?? new CryptographicSlotRandom());
        var outcome = engine.SpinTome(effectiveWager, accountId, 0, pity,
            request.UseFreeSpin, features.Tome.ActivePage is not null, potion);
        ValidateEngine(outcome, accountId, effectiveWager, request.UseFreeSpin, features, pity);
        var digest = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, Json));
        var carryMode = Text(guard, "freeSpinFeatureMode");
        var prep = new ArcanePreparedRound(Guid.NewGuid(), accountId, request, features, outcome, digest,
            request.UseFreeSpin ? 0 : ToCents(effectiveWager), ToCents(outcome.Payout.TotalPoints), now)
        {
            Observation = observation, PityMissesBefore = pity,
            FreeSpinWagerPoints = carry, FreeSpinFeatureMode = carryMode, AdmittedFreeSpinFeatureMode = request.UseFreeSpin ? carryMode : null,
        };
        var progress = ArcaneFeatureStore.PreviewSpin(features, FeatureOutcome(outcome, potion), guard,
            effectiveWager, request.UseFreeSpin);
        return new(prep, progress);
    }

    private async Task<ArcanePendingRound?> ReadPendingAsync(Transaction transaction, DocumentSnapshot guard,
        string userId, CancellationToken cancellationToken)
    {
        if (Text(guard, SlotSpinGuard.ActiveAdmissionField) is not { } active)
        {
            if (Text(guard, GuardOwner) is not null) throw Unavailable();
            return null;
        }

        if (Text(guard, GuardOwner) == Owner)
        {
            if (!Guid.TryParseExact(active, "N", out var id) || !OwnsGuard(guard, id))
                throw Unavailable();
            var own = await ReadRoundAsync(transaction, id, userId, null, cancellationToken);
            if (own.Final is not null) throw Unavailable();
            return new(own.Prepared.Round.Request);
        }
        if (Text(guard, GuardOwner) is not null) throw Unavailable();
        throw new ArcaneObsoleteRoundException();
    }

    private async Task<ArcaneStoredRound> ReadRoundAsync(Transaction transaction, Guid id, string userId, string? operationId,
        CancellationToken cancellationToken) =>
        ReadRound(await transaction.GetSnapshotAsync(RoundDocument(id), cancellationToken), id, userId, operationId);

    private static ArcaneStoredRound ReadRound(DocumentSnapshot record, Guid id, string accountId, string? operationId)
    {
        var status = Text(record, "status");
        if (Text(record, "owner") != Owner || Number(record, "schemaVersion") != CurrentIndexSchema ||
            Text(record, "admissionId") != id.ToString("N") || Text(record, "userId") != accountId ||
            Text(record, "gameId") != GameId || status is not (Pending or Settled or Declined))
            throw Unavailable();
        try
        {
            var preparedJson = ReadSealed(record, "preparedJson", "preparedSha256");
            using var document = JsonDocument.Parse(preparedJson);
            var rawOutcome = document.RootElement.GetProperty("round").GetProperty("outcome").GetRawText();
            var prepared = JsonSerializer.Deserialize<ArcaneFrozenPreparation>(preparedJson, Json) ?? throw Unavailable();
            var round = prepared.Round;
            if (round.AdmissionId != id || round.AccountId != accountId ||
                Text(record, "operationId") != round.Request.OperationId ||
                operationId is not null && round.Request.OperationId != operationId ||
                Text(record, "spinId") != round.Outcome.SpinId.ToString("N") ||
                GameSecurityEventDocuments.Hash(rawOutcome) != round.OutcomeSha256 || round.Outcome.GameId != GameId)
                throw Unavailable();

            if (status == Pending) return new(prepared, null);

            var final = JsonSerializer.Deserialize<ArcaneFinalReceipt>(ReadSealed(record, "finalJson", "finalSha256"), Json)
                ?? throw Unavailable();
            if (final.Approved != (status == Settled) || final.Approved == (final.Wallet.InsufficientFunds || final.Wallet.InsufficientLegacyFreeGames) ||
                final.Wallet.SettlementId != Text(record, "walletSettlementId"))
                throw Unavailable();
            ValidateWallet(round, final.Wallet);
            return new(prepared, final.Approved
                ? new(FinalRound(round, final.Wallet), prepared.Settlement, final.Wallet)
                : new(null, null, final.Wallet));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            throw Unavailable();
        }
    }

    private static ArcaneFeatureOutcome FeatureOutcome(ClassicSlotSpinOutcome outcome, ArcanePotionBottle? potion) =>
        new(outcome.Audit.EngineVersion, outcome.ReelSetId, outcome.Reels, potion);

    private static void ValidateAdmittedFeatures(ArcanePreparedRound round, DocumentSnapshot guard, DocumentSnapshot record)
    {
        var tome = round.Features.Tome ?? throw Unavailable();
        var potions = round.Features.Potions ?? throw Unavailable();
        if (!ArcaneTome.SameState(tome, ArcaneFeatureStore.ReadTome(guard)) ||
            !ArcanePotions.SameState(potions, ArcaneFeatureStore.ReadPotions(guard)) ||
            Number(guard, "consecutiveFiveMisses") != round.PityMissesBefore || !round.Features.TracksIngredientWagers)
            throw Unavailable();
        var admission = new Dictionary<string, object>();
        ArcaneFeatureStore.WriteAdmission(admission, round.Features);
        if (admission.Any(pair => !record.TryGetValue<object>(pair.Key, out var actual) || !Equals(pair.Value, actual)))
            throw Unavailable();
    }

    private static void ValidateEngine(ClassicSlotSpinOutcome outcome, string accountId, long wager, bool isFreeSpin,
        ArcaneFeatureAdmission features, int pity) =>
        ArcaneNativeReplay.Validate(outcome, accountId, wager, isFreeSpin, outcome.Audit.EngineVersion,
            features.Tome ?? throw Unavailable(), ArcanePotions.ForSpin(features.Potions, isFreeSpin), pity);

    private static void ValidateWallet(ArcanePreparedRound round, GameWalletSettlementReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var spin = round.Outcome.SpinId.ToString("N");
        var expectedId = GameWalletSettlementIdentity.CreateId(GameId, round.AccountId, spin);
        var decline = receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames;
        if (receipt.SettlementId != expectedId || receipt.BalanceBeforeCents < 0 ||
            receipt.SettledAtUtc.UtcDateTime < round.StartedAtUtc.AddSeconds(-1) ||
            receipt.LegacyFreeGamesRemaining is < 0 or > int.MaxValue ||
            receipt.InsufficientLegacyFreeGames && (!round.Request.UseFreeSpin || receipt.LegacyFreeGamesRemaining != 0) ||
            receipt.InsufficientFunds && (round.WagerCents == 0 || receipt.BalanceBeforeCents >= round.WagerCents))
            throw Unavailable();

        var afterWager = decline ? receipt.BalanceBeforeCents : checked(receipt.BalanceBeforeCents - round.WagerCents);
        if (afterWager < 0 || receipt.BalanceAfterWagerCents != afterWager ||
            receipt.BalanceAfterCents != (decline ? afterWager : checked(afterWager + round.PayoutCents)) ||
            receipt.WagerTransactionId != (!decline && round.WagerCents > 0 ? spin + "-wager" : null) ||
            receipt.PayoutTransactionId != (!decline && round.PayoutCents > 0 ? spin + "-payout" : null) ||
            receipt.LegacyFreeGameTransactionId != (!decline && round.Request.UseFreeSpin ? spin + "-free-game-use" : null) ||
            !decline && round.Request.UseFreeSpin && receipt.LegacyFreeGamesRemaining is null)
            throw Unavailable();
    }

    private static ArcanePreparedRound FinalRound(ArcanePreparedRound round, GameWalletSettlementReceipt receipt)
    {
        var count = receipt.LegacyFreeGamesRemaining;
        return round with
        {
            FreeSpinsRemaining = count is { } remaining ? checked((int)remaining) : round.FreeSpinsRemaining,
            FreeSpinWagerPoints = count == 0 ? null :
                round.Request.UseFreeSpin ? round.Outcome.WagerPoints : round.FreeSpinWagerPoints,
            FreeSpinFeatureMode = count == 0 ? null : round.FreeSpinFeatureMode,
        };
    }

    private static void ApplyCarryFields(Dictionary<string, object> fields, ArcanePreparedRound result,
        GameWalletSettlementReceipt receipt)
    {
        if (receipt.LegacyFreeGamesRemaining is null) return;
        fields["freeSpinWagerPoints"] = result.FreeSpinWagerPoints ?? 0;
        fields["freeSpinWagerCents"] = result.FreeSpinWagerPoints is { } wager ? ToCents(wager) : 0;
        fields["freeSpinFeatureMode"] = result.FreeSpinFeatureMode ?? string.Empty;
        if (receipt.LegacyFreeGamesRemaining == 0) fields["freeSpinMathModelId"] = string.Empty;
    }

    private static long? ReadCarryWager(DocumentSnapshot guard)
    {
        var cents = Number(guard, "freeSpinWagerCents");
        if (cents <= 0) cents = checked(Number(guard, "freeSpinWagerPoints") * 100);
        if (cents <= 0) return null;
        var points = cents / Game.Wagering.PointValueInCents;
        if (points != decimal.Truncate(points)) throw Unavailable();
        return checked((long)points);
    }

    private static long ToCents(long points)
    {
        var cents = checked(points * Game.Wagering.PointValueInCents);
        if (cents < 0 || cents != decimal.Truncate(cents)) throw Unavailable();
        return checked((long)cents);
    }

    private static string RequestDigest(ArcaneRoundRequest request) => GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
    {
        wagerPoints = request.WagerPoints,
        pointValueInCents = Game.Wagering.PointValueInCents.ToString("G29", CultureInfo.InvariantCulture),
        useFreeSpin = request.UseFreeSpin,
        useSpecialBoost = request.UseSpecialBoost,
        specialBoostCost = 0,
    }));

    private static void ValidateIndex(DocumentSnapshot index, string accountId, string operationId)
    {
        if (Text(index, "owner") != Owner || Number(index, "schemaVersion") != CurrentIndexSchema ||
            Text(index, "userId") != accountId || Text(index, "gameId") != GameId ||
            Text(index, "operationId") != operationId || AdmissionId(index) == Guid.Empty)
            throw Unavailable();
    }

    private static bool OwnsGuard(DocumentSnapshot guard, Guid id) =>
        Text(guard, GuardOwner) == Owner && Text(guard, SlotSpinGuard.ActiveAdmissionField) == id.ToString("N") &&
        Text(guard, SlotSpinGuard.PreparedAdmissionField) == id.ToString("N");

    private static bool SameRound(ArcanePreparedRound left, ArcanePreparedRound right) =>
        left.AdmissionId == right.AdmissionId && left.AccountId == right.AccountId &&
        left.Request == right.Request && left.Outcome.SpinId == right.Outcome.SpinId &&
        left.OutcomeSha256 == right.OutcomeSha256 && left.WagerCents == right.WagerCents && left.PayoutCents == right.PayoutCents;

    private static Guid AdmissionId(DocumentSnapshot index) =>
        Guid.TryParseExact(Text(index, "admissionId"), "N", out var id) ? id : Guid.Empty;

    private static string ReadSealed(DocumentSnapshot record, string jsonField, string hashField) =>
        Text(record, jsonField) is { } json && Text(record, hashField) == GameSecurityEventDocuments.Hash(json)
            ? json : throw Unavailable();

    private DocumentReference GuardDocument(string accountId) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(accountId, GameId));

    private DocumentReference OperationDocument(string accountId, string operationId) =>
        database.Collection(Operations).Document(GameSecurityEventDocuments.Hash($"{GameId}\n{accountId}\n{operationId}"));

    private DocumentReference RoundDocument(Guid id) => database.Collection(RawCollectionName).Document(id.ToString("N"));

    public static bool TryGetSettledRequestId(DocumentSnapshot record, string spinId, out string? requestId)
    {
        requestId = null;
        if (!record.Exists || record.Reference.Parent.Id != RawCollectionName ||
            Text(record, "owner") != Owner || Number(record, "schemaVersion") != CurrentIndexSchema ||
            Text(record, "gameId") != GameId || Text(record, "status") != Settled ||
            !Guid.TryParseExact(spinId, "N", out _) || Text(record, "spinId") != spinId ||
            !Guid.TryParseExact(record.Id, "N", out _) || Text(record, "admissionId") != record.Id ||
            Text(record, "userId") is null)
            return false;
        requestId = Text(record, "requestId");
        return true;
    }

    private DateTime UtcNow() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static long Number(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static ArcaneRoundConflictException Unavailable() =>
        new("The saved Arcane Archives spin is unavailable. Its operation ID cannot be reused for a new spin.");
}

internal sealed record ArcaneFrozenPreparation(ArcanePreparedRound Round, ArcaneFeatureSettlement Settlement);
internal sealed record ArcaneFinalReceipt(bool Approved, GameWalletSettlementReceipt Wallet);
internal sealed record ArcaneStoredRound(ArcaneFrozenPreparation Prepared, ArcaneRoundResult? Final);
internal sealed record ArcanePendingRound(ArcaneRoundRequest Request);
