using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Craps;

/// <summary>Account-backed Craps. Each accepted action commits together with its wallet effects.</summary>
public sealed class FirestorePaidCrapsStore
{
    private readonly FirestoreDb database;
    private readonly Func<DicePair> draw;
    private readonly Action<Transaction, GameSecurityEventDocument>? writeActivity;
    private const string Currency = "slotsCredits";
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private static readonly JsonSerializerOptions RoundJson = new()
    {
        Converters = { new DieValueJsonConverter() },
    };

    public FirestorePaidCrapsStore(FirestoreDb database, Func<DicePair>? draw = null,
        Action<Transaction, GameSecurityEventDocument>? writeActivity = null)
    {
        this.database = database;
        this.writeActivity = writeActivity;
        this.draw = draw ?? (() => new DicePair(
            new DieValue(RandomNumberGenerator.GetInt32(1, 7)),
            new DieValue(RandomNumberGenerator.GetInt32(1, 7))));
    }

    public async Task<CrapsStatusResponse> StatusAsync(string userId, CancellationToken cancellationToken = default)
    {
        var balance = await Balance(userId).GetSnapshotAsync(cancellationToken);
        return new(true, MinimumStake, MaximumStake, MinimumStake, "paid-pass-line", ReadBalance(balance));
    }

    public Task<CrapsRoundResponse?> ActiveAsync(string userId, CancellationToken cancellationToken = default) =>
        database.RunTransactionAsync(async transaction =>
        {
            var player = await transaction.GetSnapshotAsync(Player(userId), cancellationToken);
            var activeId = ActiveId(player, userId);
            if (activeId is null) return null;
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(activeId.Value), Balance(userId)], cancellationToken);
            var stored = ReadRound(snapshots[0], userId);
            if (stored.State.Phase == CrapsPassLinePhase.Resolved)
                throw new InvalidOperationException("A completed Craps round is still marked active.");
            return ToResponse(stored, ReadBalance(snapshots[1]));
        }, cancellationToken: cancellationToken);

    public Task<CrapsRoundResponse> GetAsync(string userId, Guid roundId, CancellationToken cancellationToken = default) =>
        database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Round(roundId), Balance(userId)], cancellationToken);
            return ToResponse(ReadRound(snapshots[0], userId), ReadBalance(snapshots[1]));
        }, cancellationToken: cancellationToken);

    public Task<CrapsRoundResponse> StartAsync(string userId, StartCrapsRoundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = OperationId(request.OperationId);
        ValidateStake(request.Stake);
        var extras = InitialExtras(request.ExtraBets);
        // Keep the historical opening fingerprint stable for operation receipts written before continuation support.
        var fingerprint = Fingerprint("start", request.Stake, extras.Select(extra => new
            { extra.Kind, extra.Stake, extra.Resolved, extra.Won, extra.TotalReturn }).ToArray());
        extras = extras.Select(extra => extra with { WagerId = operationId + ":" + extra.Kind, PlacedForRoll = 1 }).ToArray();
        var id = RoundId(userId, operationId);
        var operation = Operation(userId, operationId);
        var player = Player(userId);
        var round = Round(id);
        var balance = Balance(userId);
        var ledger = Ledger($"craps-{id:N}-entry");
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, player, round, balance, ledger], cancellationToken);
            if (snapshots[0].Exists) return Replay(snapshots[0], userId, fingerprint);
            if (ActiveId(snapshots[1], userId) is not null)
                throw new CrapsPaidException("craps-round-active", "Finish the current Craps round before starting another.", 409);
            if (snapshots[2].Exists || snapshots[4].Exists)
                throw new InvalidOperationException("A Craps round or ledger exists without its operation record.");
            var wagerCents = Cents(request.Stake + extras.Sum(extra => extra.Stake));
            var before = ReadBalance(snapshots[3]);
            if (before < wagerCents) throw new CrapsPaidException("craps-insufficient-credits", "The wallet has insufficient credits for this wager.", 409);
            var after = checked(before - wagerCents);
            var now = DateTime.UtcNow;
            var saved = new PaidCrapsRound(id, userId,
                CrapsEngine.StartPassLine(new CrapsPassLineBet(userId, request.Stake)), extras,
                wagerCents, 0, now, null, 0,
                [new(operationId, "start", null, request.Stake, 0, null, null, before, after)], 2);
            var response = ToResponse(saved, after);
            transaction.Create(round, RoundData(saved));
            transaction.Set(player, PlayerData(userId, id, now), SetOptions.MergeAll);
            WriteBalance(transaction, balance, after, now);
            transaction.Create(ledger, LedgerData(ledger.Id, userId, -wagerCents, after, "craps-wager", operationId, now));
            transaction.Create(operation, OperationData(userId, operationId, fingerprint, id, "start", response, now));
            WriteObservation(transaction, saved, "started", now);
            return response;
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: cancellationToken);
    }

    public Task<CrapsRoundResponse> PlaceOddsAsync(string userId, Guid roundId, PlaceCrapsOddsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeWagerAsync(userId, roundId,
            new("odds", request.Stake, request.OperationId, request.ExpectedRevision, request.ExpectedRollNumber), "odds", cancellationToken);
    }

    public Task<CrapsRoundResponse> SetWagerAsync(string userId, Guid roundId, SetCrapsWagerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeWagerAsync(userId, roundId, request, "wager", cancellationToken);
    }

    private Task<CrapsRoundResponse> ChangeWagerAsync(string userId, Guid roundId, SetCrapsWagerRequest request,
        string actionKind, CancellationToken cancellationToken)
    {
        var operationId = OperationId(request.OperationId);
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("odds" or "field" or "any-seven" or "any-craps"))
            throw new CrapsPaidException("craps-invalid-bets", "Choose a supported adjustable wager; the Pass Line principal stays locked.", 400);
        if (request.Stake != 0m) ValidateStake(request.Stake);
        var fingerprint = actionKind == "odds" && request.ExpectedRevision is null && request.ExpectedRollNumber is null
            ? Fingerprint("odds", roundId, request.Stake)
            : Fingerprint(actionKind, roundId, kind, request.Stake, request.ExpectedRevision, request.ExpectedRollNumber);
        var operation = Operation(userId, operationId);
        var round = Round(roundId);
        var player = Player(userId);
        var balance = Balance(userId);
        var ledger = Ledger($"craps-{roundId:N}-{operationId}-{actionKind}");
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, round, player, balance, ledger], cancellationToken);
            if (snapshots[0].Exists) return Replay(snapshots[0], userId, fingerprint);
            var saved = ReadRound(snapshots[1], userId);
            ValidateRevision(saved, request.ExpectedRevision, request.ExpectedRollNumber);
            var now = DateTime.UtcNow;
            if (ActiveId(snapshots[2], userId) != roundId || saved.State.Phase != CrapsPassLinePhase.Point || saved.State.Point is null)
                throw new CrapsPaidException("craps-wager-unavailable", "Adjust odds and next-roll wagers only between rolls while a point is established.", 409);
            var previous = saved.Extras.SingleOrDefault(extra => extra.Kind == kind && !extra.Resolved);
            if ((previous?.Stake ?? 0m) == request.Stake)
                throw new CrapsPaidException("craps-wager-unchanged", "This wager already has the requested amount.", 409);
            if (snapshots[4].Exists) throw new InvalidOperationException("A Craps wager ledger exists without its operation record.");
            var before = ReadBalance(snapshots[3]);
            var debitCents = Cents(request.Stake - (previous?.Stake ?? 0m));
            if (before < debitCents) throw new CrapsPaidException("craps-insufficient-credits", "The wallet has insufficient credits for this wager.", 409);
            var after = checked(before - debitCents);
            var extras = saved.Extras.Select(extra => ReferenceEquals(extra, previous)
                ? extra with { Resolved = true, Won = false, TotalReturn = 0m, Cancelled = true, ResolvedOnRoll = null }
                : extra).ToList();
            if (request.Stake > 0m)
                extras.Add(new(kind, request.Stake, false, false, null, operationId + ":" + kind, saved.State.Rolls.Length + 1));
            var next = saved with { Extras = [..extras], TotalWagerCents = checked(saved.TotalWagerCents + debitCents),
                Revision = saved.Revision + 1, SchemaVersion = 2,
                Actions = [..saved.Actions ?? [], new(operationId, actionKind, kind, request.Stake, saved.Revision + 1,
                    request.ExpectedRevision, request.ExpectedRollNumber, before, after)] };
            var response = ToResponse(next, after);
            transaction.Set(round, RoundData(next));
            if (debitCents != 0)
            {
                WriteBalance(transaction, balance, after, now);
                transaction.Create(ledger, LedgerData(ledger.Id, userId, -debitCents, after,
                    actionKind == "odds" ? "craps-odds" : "craps-wager-adjustment", operationId, now));
            }
            transaction.Create(operation, OperationData(userId, operationId, fingerprint, roundId, actionKind, response, now));
            WriteObservation(transaction, next, "wager-adjusted", now);
            return response;
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: cancellationToken);
    }

    public Task<CrapsRoundResponse> RollAsync(string userId, Guid roundId, CrapsOperationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = OperationId(request.OperationId);
        var fingerprint = request.ExpectedRevision is null && request.ExpectedRollNumber is null
            ? Fingerprint("roll", roundId) : Fingerprint("roll", roundId, request.ExpectedRevision, request.ExpectedRollNumber);
        var operation = Operation(userId, operationId);
        var round = Round(roundId);
        var player = Player(userId);
        var balance = Balance(userId);
        var payoutLedger = Ledger($"craps-{roundId:N}-{operationId}-payout");
        // Draw only after a new operation is accepted, and reuse the result if Firestore retries.
        DicePair? dice = null;
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, round, player, balance, payoutLedger], cancellationToken);
            if (snapshots[0].Exists) return Replay(snapshots[0], userId, fingerprint);
            var saved = ReadRound(snapshots[1], userId);
            ValidateRevision(saved, request.ExpectedRevision, request.ExpectedRollNumber);
            var now = DateTime.UtcNow;
            if (ActiveId(snapshots[2], userId) != roundId || saved.State.Phase == CrapsPassLinePhase.Resolved)
                throw new CrapsPaidException("craps-round-complete", "This Craps round has finished.", 409);
            dice ??= draw();
            dice.Value.Validate();
            var state = CrapsEngine.Roll(saved.State, dice.Value).State;
            var extras = SettleExtras(saved.Extras, state, dice.Value.Total);
            var terminal = state.Phase == CrapsPassLinePhase.Resolved;
            var newlyResolvedExtras = extras.Where((extra, index) => !saved.Extras[index].Resolved && extra.Resolved)
                .Sum(extra => extra.TotalReturn ?? 0m);
            var payoutCents = Cents(newlyResolvedExtras + (terminal ? state.LastOutcome?.TotalReturn ?? 0m : 0m));
            var next = saved with { State = state, Extras = extras,
                TotalReturnCents = checked(saved.TotalReturnCents + payoutCents),
                CompletedAtUtc = terminal ? now : null, Revision = saved.Revision + 1, SchemaVersion = 2 };
            var before = ReadBalance(snapshots[3]);
            var after = checked(before + payoutCents);
            next = next with { Actions = [..saved.Actions ?? [], new(operationId, "roll", null, null, next.Revision,
                request.ExpectedRevision, request.ExpectedRollNumber, before, after)] };
            if (snapshots[4].Exists) throw new InvalidOperationException("A Craps payout ledger exists without its operation record.");
            var response = ToResponse(next, after);
            transaction.Set(round, RoundData(next));
            if (terminal) transaction.Set(player, PlayerData(userId, null, now), SetOptions.MergeAll);
            if (payoutCents > 0) {
                WriteBalance(transaction, balance, after, now);
                transaction.Create(payoutLedger, LedgerData(payoutLedger.Id, userId, payoutCents, after, "craps-payout", operationId, now));
            }
            transaction.Create(operation, OperationData(userId, operationId, fingerprint, roundId, "roll", response, now));
            WriteObservation(transaction, next, "rolled", now);
            if (terminal) WriteSettlement(transaction, next, now);
            return response;
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: cancellationToken);
    }

    private void WriteSettlement(Transaction transaction, PaidCrapsRound round, DateTime now)
    {
        var record = SinglePlayerRoundRecord.Create("craps", round.Id.ToString("N"), round.UserId,
            round.TotalWagerCents, round.TotalReturnCents, round.StartedAtUtc, now,
            "crapsPaidRounds", round.Id.ToString("N"), round, "craps-private-dice-and-bets-v1");
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        var documents = RoundAuditDocuments.Create(record.Audit);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(record.Audit.Summary.Id), documents.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(record.Audit.Summary.Id), documents.Evidence);
    }

    private void WriteObservation(Transaction transaction, PaidCrapsRound round, string kind, DateTime now)
    {
        var observation = GameSecurityEventDocuments.Capture("craps", round.Id.ToString("N"), round.Revision,
            now, new { round.State, round.Extras, round.TotalWagerCents, round.TotalReturnCents, round.Actions, round.SchemaVersion }, [round.UserId], kind);
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId("craps", round.Id.ToString("N"))),
            new Dictionary<string, object> { ["gameId"] = "craps", ["instanceId"] = round.Id.ToString("N"),
                ["eventIds"] = FieldValue.ArrayUnion(observation.Id), ["schemaVersion"] = 1L }, SetOptions.MergeAll);
        writeActivity?.Invoke(transaction, observation);
    }

    private static PaidCrapsExtra[] InitialExtras(IReadOnlyList<CrapsExtraBetRequest>? requests)
    {
        if (requests is null || requests.Count == 0) return [];
        if (requests.Count > 3) throw new CrapsPaidException("craps-invalid-bets", "Choose at most one of each side bet.", 400);
        var extras = new List<PaidCrapsExtra>(requests.Count);
        foreach (var request in requests)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Kind)) throw new CrapsPaidException("craps-invalid-bets", "Choose a valid side bet.", 400);
            var kind = request.Kind.Trim().ToLowerInvariant();
            if (kind is not ("field" or "any-seven" or "any-craps") || extras.Any(extra => extra.Kind == kind))
                throw new CrapsPaidException("craps-invalid-bets", "Choose at most one of each supported side bet.", 400);
            ValidateStake(request.Stake);
            extras.Add(new(kind, request.Stake, false, false, null));
        }
        return [..extras];
    }

    private static PaidCrapsExtra[] SettleExtras(IReadOnlyList<PaidCrapsExtra> extras, CrapsPassLineState state, int total) =>
        extras.Select(extra => extra.Resolved || extra.Kind == "odds" && state.Phase != CrapsPassLinePhase.Resolved
            ? extra : Settle(extra, state, total)).ToArray();

    private static PaidCrapsExtra Settle(PaidCrapsExtra bet, CrapsPassLineState state, int total)
    {
        var payout = bet.Kind switch
        {
            "field" when total is 2 or 12 => bet.Stake * 3m,
            "field" when total is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2m,
            "any-seven" when total == 7 => bet.Stake * 5m,
            "any-craps" when total is 2 or 3 or 12 => bet.Stake * 8m,
            "odds" when state.LastOutcome?.Result is CrapsRollResult.PointHit => state.Point switch
            {
                4 or 10 => bet.Stake * 3m,
                5 or 9 => bet.Stake * 2.5m,
                6 or 8 => bet.Stake * 2.2m,
                _ => throw new CrapsRuleException("The point does not support odds."),
            },
            _ => 0m,
        };
        return bet with { Resolved = true, Won = payout > 0, TotalReturn = payout,
            ResolvedOnRoll = bet.WagerId is null ? null : state.Rolls.Length };
    }

    private static CrapsRoundResponse ToResponse(PaidCrapsRound round, long balance) => new(
        round.Id, round.State.Bet.Stake, round.State.Phase switch
        {
            CrapsPassLinePhase.ComeOut => "come-out", CrapsPassLinePhase.Point => "point", _ => "resolved"
        }, round.State.Point,
        round.State.Rolls.Select((dice, index) => new CrapsRollResponse(index + 1, dice.First.Value, dice.Second.Value, dice.Total,
            index == round.State.Rolls.Length - 1 && round.State.LastOutcome is { } outcome ? ResultName(outcome.Result) : null)).ToArray(),
        round.State.LastOutcome is { } last ? new CrapsOutcomeResponse(last.Dice.First.Value, last.Dice.Second.Value, last.Dice.Total,
            ResultName(last.Result), last.IsTerminal, last.TotalReturn) : null,
        round.Extras.Select(extra => new CrapsExtraBetResponse(extra.Kind, extra.Stake, extra.Resolved, extra.Won, extra.TotalReturn,
            extra.WagerId, extra.PlacedForRoll, extra.ResolvedOnRoll, extra.Cancelled)).ToArray(), balance,
        round.Revision, round.TotalWagerCents, round.TotalReturnCents);

    private static void ValidateRevision(PaidCrapsRound round, long? revision, int? rollNumber)
    {
        if (revision is null or < 0 || rollNumber is null or < 1)
            throw new CrapsPaidException("craps-revision-required", "Refresh the round before submitting an action.", 400);
        if (revision != round.Revision || rollNumber != round.State.Rolls.Length + 1)
            throw new CrapsPaidException("craps-round-stale", "The round changed in another tab. Refresh it before submitting a new action.", 409);
    }

    private static string ResultName(CrapsRollResult result) => result switch
    {
        CrapsRollResult.NaturalWin => "natural-win", CrapsRollResult.CrapsLoss => "craps-loss",
        CrapsRollResult.PointEstablished => "point-established", CrapsRollResult.PointHit => "point-hit",
        CrapsRollResult.SevenOut => "seven-out", CrapsRollResult.NoDecision => "no-decision",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    private static void ValidateStake(decimal stake)
    {
        if (stake is < MinimumStake or > MaximumStake || stake % MinimumStake != 0m)
            throw new CrapsPaidException("craps-invalid-wager", "Choose a wager from R1 through R100 in whole rand.", 400);
    }
    private static long Cents(decimal value) => checked((long)(value * 100m));
    private static string OperationId(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id.ToString("N") : throw new CrapsPaidException("craps-invalid-operation", "A unique operation ID is required.", 400);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Fingerprint(params object?[] parts) => Hash(JsonSerializer.Serialize(parts));
    private static Guid RoundId(string userId, string operationId) => new(SHA256.HashData(Encoding.UTF8.GetBytes(userId + "\n" + operationId))[..16]);

    private DocumentReference Player(string userId) => database.Collection("crapsPaidPlayers").Document(Hash(userId));
    private DocumentReference Round(Guid id) => database.Collection("crapsPaidRounds").Document(id.ToString("N"));
    private DocumentReference Operation(string userId, string operationId) => database.Collection("crapsPaidOperations").Document(Hash(userId + "\n" + operationId));
    private DocumentReference Balance(string userId) => database.Collection("userBalances").Document($"{userId}_{Currency}");
    private DocumentReference Ledger(string id) => database.Collection("balanceTransactions").Document(id);

    private static Guid? ActiveId(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists) return null;
        if (!snapshot.TryGetValue<string>("userId", out var owner) || owner != userId)
            throw new InvalidOperationException("The Craps player pointer is corrupt.");
        if (!snapshot.TryGetValue<string>("activeRoundId", out var raw) || string.IsNullOrEmpty(raw)) return null;
        return Guid.TryParseExact(raw, "N", out var id) ? id : throw new InvalidOperationException("The Craps active-round pointer is corrupt.");
    }

    private static PaidCrapsRound ReadRound(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists) throw new CrapsAccessException();
        if (!snapshot.TryGetValue<string>("userId", out var owner) || owner != userId) throw new CrapsAccessException();
        if (!snapshot.TryGetValue<string>("roundJson", out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The Craps round is corrupt.");
        var round = JsonSerializer.Deserialize<PaidCrapsRound>(json, RoundJson) ?? throw new InvalidOperationException("The Craps round is corrupt.");
        if (round.UserId != userId || round.Id.ToString("N") != snapshot.Id || round.State.Bet.PlayerId != userId ||
            round.Revision < 0 || round.SchemaVersion is not (1 or 2))
            throw new InvalidOperationException("The Craps round is corrupt.");
        ValidateStoredRound(round);
        return round;
    }

    private static void ValidateStoredRound(PaidCrapsRound round)
    {
        static void Require([DoesNotReturnIf(false)] bool valid)
        {
            if (!valid) throw new InvalidOperationException("The Craps round is corrupt.");
        }
        Require(round.State.Bet.Stake is >= MinimumStake and <= MaximumStake && round.State.Bet.Stake % 1m == 0m);
        Require(round.Extras is not null && round.TotalReturnCents >= 0);
        // Version one admitted a single opening batch and one odds record. It remains readable unchanged.
        Require(round.SchemaVersion != 1 || round.Extras.Length <= 4);
        Require(round.SchemaVersion != 1 || round.Extras.Select(extra => extra.Kind).Distinct(StringComparer.Ordinal).Count() == round.Extras.Length);
        var nextRoll = round.State.Rolls.Length + 1;
        foreach (var dice in round.State.Rolls) dice.Validate();
        if (round.State.LastOutcome is { } outcome)
        {
            outcome.Dice.Validate();
            Require(round.State.Rolls.Length > 0 && outcome.Dice == round.State.Rolls[^1]);
        }
        else Require(round.State.Rolls.Length == 0);
        var identified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extra in round.Extras)
        {
            Require(extra.Kind is "field" or "any-seven" or "any-craps" or "odds");
            Require(extra.Stake is >= MinimumStake and <= MaximumStake && extra.Stake % 1m == 0m);
            Require(extra.Resolved ? extra.TotalReturn is >= 0m && extra.Won == (extra.TotalReturn > 0m)
                : extra.TotalReturn is null && !extra.Won && !extra.Cancelled);
            Require(!extra.Cancelled || extra.Resolved && !extra.Won && extra.TotalReturn == 0m && extra.ResolvedOnRoll is null);
            if (extra.WagerId is null)
            {
                // Legacy outcomes do not gain fabricated wager or roll identities during reads or continuation.
                Require(extra.PlacedForRoll is null && extra.ResolvedOnRoll is null);
                Require(extra.Kind == "odds" || extra.Resolved || round.State.Rolls.Length == 0);
            }
            else
            {
                Require(identified.Add(extra.WagerId) && extra.PlacedForRoll is >= 1 && extra.PlacedForRoll <= nextRoll);
                Require(extra.WagerId.EndsWith(":" + extra.Kind, StringComparison.Ordinal) &&
                    Guid.TryParseExact(extra.WagerId[..^(extra.Kind.Length + 1)], "N", out var wagerOperation) && wagerOperation != Guid.Empty);
                Require(extra.Resolved && !extra.Cancelled
                    ? extra.ResolvedOnRoll >= extra.PlacedForRoll && extra.ResolvedOnRoll <= round.State.Rolls.Length &&
                        (extra.Kind == "odds" || extra.ResolvedOnRoll == extra.PlacedForRoll)
                    : extra.ResolvedOnRoll is null);
                Require(extra.Resolved || extra.Kind == "odds" || extra.PlacedForRoll == nextRoll);
                Require(extra.Kind != "odds" || extra.PlacedForRoll >= 2);
            }
            Require(extra.Kind != "odds" || !extra.Resolved || extra.Cancelled || round.State.Phase == CrapsPassLinePhase.Resolved);
            Require(round.State.Phase != CrapsPassLinePhase.Resolved || extra.Resolved);
        }
        Require(round.Extras.Where(extra => !extra.Resolved).GroupBy(extra => extra.Kind).All(group => group.Count() == 1));
        Require(round.State.Phase == CrapsPassLinePhase.Point || !round.Extras.Any(extra => extra.Kind == "odds" && !extra.Resolved));
        Require(round.TotalWagerCents == Cents(round.State.Bet.Stake + round.Extras.Where(extra => !extra.Cancelled).Sum(extra => extra.Stake)));
        Require(round.TotalReturnCents == Cents(round.Extras.Sum(extra => extra.TotalReturn ?? 0m) +
            (round.State.Phase == CrapsPassLinePhase.Resolved ? round.State.LastOutcome?.TotalReturn ?? 0m : 0m)));
        Require((round.State.Phase == CrapsPassLinePhase.Resolved) == (round.CompletedAtUtc is not null));
        if (round.Actions is not null)
        {
            Require(round.SchemaVersion == 2 && round.Actions.Length > 0 && round.Actions[^1].Revision == round.Revision);
            var operations = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < round.Actions.Length; index++)
            {
                var action = round.Actions[index];
                Require(Guid.TryParseExact(action.OperationId, "N", out var id) && id != Guid.Empty && operations.Add(action.OperationId));
                Require(action.Revision >= 0 && (index == 0 || action.Revision == round.Actions[index - 1].Revision + 1));
                Require(action.WalletBeforeCents >= 0 && action.WalletAfterCents >= 0);
                Require(action.Kind is "start" or "roll" or "wager" or "odds");
                Require(action.Kind == "start" ? action.Revision == 0 && action.ExpectedRevision is null && action.ExpectedRollNumber is null
                    : action.ExpectedRevision == action.Revision - 1 && action.ExpectedRollNumber is >= 1);
            }
        }
        else Require(round.SchemaVersion == 1);
    }

    private static long ReadBalance(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        if (!snapshot.TryGetValue<long>("available", out var whole))
            throw new InvalidOperationException("The account wallet balance is missing.");
        var fractional = snapshot.TryGetValue<long>("availableFractionalCents", out var cents) ? cents : 0;
        if (whole < 0 || fractional is < 0 or > 99) throw new InvalidOperationException("The account wallet balance is corrupt.");
        return checked(whole * 100 + fractional);
    }

    private static void WriteBalance(Transaction transaction, DocumentReference reference, long cents, DateTime now) =>
        transaction.Set(reference, new Dictionary<string, object>
        {
            ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100,
            ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(now),
        }, SetOptions.MergeAll);

    private static Dictionary<string, object> PlayerData(string userId, Guid? activeId, DateTime now) => new()
    {
        ["userId"] = userId, ["activeRoundId"] = activeId?.ToString("N") ?? "",
        ["updatedAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L,
    };
    private static Dictionary<string, object> RoundData(PaidCrapsRound round) => new()
    {
        ["roundId"] = round.Id.ToString("N"), ["userId"] = round.UserId,
        ["roundJson"] = JsonSerializer.Serialize(round), ["phase"] = round.State.Phase.ToString(),
        ["revision"] = round.Revision, ["schemaVersion"] = (long)round.SchemaVersion,
    };
    private static Dictionary<string, object> OperationData(string userId, string operationId, string fingerprint,
        Guid roundId, string kind, CrapsRoundResponse response, DateTime now) => new()
    {
        ["userId"] = userId, ["operationId"] = operationId, ["fingerprint"] = fingerprint,
        ["roundId"] = roundId.ToString("N"), ["kind"] = kind,
        ["responseJson"] = JsonSerializer.Serialize(response), ["createdAt"] = Timestamp.FromDateTime(now),
        ["schemaVersion"] = 1L,
    };
    private static Dictionary<string, object> LedgerData(string id, string userId, long amount, long after,
        string type, string operationId, DateTime now) => new()
    {
        ["transactionId"] = id, ["userId"] = userId, ["currencyId"] = Currency,
        ["amount"] = (double)amount / 100d, ["balanceAfter"] = (double)after / 100d,
        ["type"] = type, ["idempotencyKey"] = operationId, ["createdAt"] = Timestamp.FromDateTime(now),
    };
    private static CrapsRoundResponse Replay(DocumentSnapshot snapshot, string userId, string fingerprint)
    {
        if (!snapshot.TryGetValue<string>("userId", out var owner) || owner != userId ||
            !snapshot.TryGetValue<string>("fingerprint", out var saved) || saved != fingerprint)
            throw new CrapsPaidException("craps-operation-conflict", "This operation ID was already used for another action.", 409);
        var json = snapshot.GetValue<string>("responseJson");
        var response = JsonSerializer.Deserialize<CrapsRoundResponse>(json) ?? throw new InvalidOperationException("The Craps operation response is corrupt.");
        using var document = JsonDocument.Parse(json);
        // Receipts from the original paid contract have no revision or cumulative totals. Return the
        // corresponding historical metadata without rewriting the immutable receipt or any outcome.
        var hasRevision = document.RootElement.TryGetProperty(nameof(CrapsRoundResponse.Revision), out _);
        var hasWager = document.RootElement.TryGetProperty(nameof(CrapsRoundResponse.TotalWagerCents), out _);
        var hasReturn = document.RootElement.TryGetProperty(nameof(CrapsRoundResponse.TotalReturnCents), out _);
        if (!hasRevision || !hasWager || !hasReturn)
        {
            if (hasRevision || hasWager || hasReturn || response.ExtraBets.Any(extra =>
                extra.WagerId is not null || extra.PlacedForRoll is not null || extra.ResolvedOnRoll is not null || extra.Cancelled))
                throw new InvalidOperationException("The Craps operation response is corrupt.");
            response = response with
            {
                Revision = response.Rolls.Count + response.ExtraBets.Count(extra => extra.Kind == "odds"),
                TotalWagerCents = Cents(response.Stake + response.ExtraBets.Sum(extra => extra.Stake)),
                TotalReturnCents = Cents((response.LastOutcome?.TotalReturn ?? 0m) + response.ExtraBets.Sum(extra => extra.TotalReturn ?? 0m)),
            };
        }
        return response;
    }

    // DieValue has a validating constructor and a getter-only Value. The default serializer otherwise
    // creates its zero-valued struct on read, losing accepted dice on the next persisted action.
    private sealed class DieValueJsonConverter : JsonConverter<DieValue>
    {
        public override DieValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty(nameof(DieValue.Value), out var element) ||
                !element.TryGetInt32(out var value) || value is < 1 or > 6)
                throw new InvalidOperationException("The Craps round contains corrupt persisted dice.");
            return new DieValue(value);
        }

        public override void Write(Utf8JsonWriter writer, DieValue value, JsonSerializerOptions options)
        {
            value.Validate();
            writer.WriteStartObject();
            writer.WriteNumber(nameof(DieValue.Value), value.Value);
            writer.WriteEndObject();
        }
    }
}

internal sealed record PaidCrapsRound(Guid Id, string UserId, CrapsPassLineState State, PaidCrapsExtra[] Extras,
    long TotalWagerCents, long TotalReturnCents, DateTime StartedAtUtc, DateTime? CompletedAtUtc, long Revision,
    PaidCrapsAction[]? Actions = null, int SchemaVersion = 1);
internal sealed record PaidCrapsExtra(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn,
    string? WagerId = null, int? PlacedForRoll = null, int? ResolvedOnRoll = null, bool Cancelled = false);
internal sealed record PaidCrapsAction(string OperationId, string Kind, string? WagerKind, decimal? Stake, long Revision,
    long? ExpectedRevision, int? ExpectedRollNumber, long WalletBeforeCents, long WalletAfterCents);
public sealed class CrapsPaidException(string code, string message, int statusCode) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
