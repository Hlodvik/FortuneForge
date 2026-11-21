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

/// <summary>Account-backed Craps. Each accepted action commits together with its wallet effects.</summary>
public sealed partial class FirestorePaidCrapsStore
{
    private readonly FirestoreDb database;
    private readonly Func<DicePair> draw;
    private readonly IGameWalletClient wallet;
    private readonly IGameWalletAccountReader accounts;
    private readonly Func<bool>? acceptsNewRounds;
    private const decimal MinimumStake = 1m;
    private const decimal MaximumStake = 100m;
    private static readonly JsonSerializerOptions RoundJson = new()
    {
        Converters = { new DieValueJsonConverter() },
    };

    public FirestorePaidCrapsStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts,
        Func<DicePair>? draw = null, Func<bool>? acceptsNewRounds = null)
    {
        this.database = database; this.wallet = wallet; this.accounts = accounts; this.acceptsNewRounds = acceptsNewRounds;
        this.draw = draw ?? (() => new DicePair(new DieValue(RandomNumberGenerator.GetInt32(1, 7)), new DieValue(RandomNumberGenerator.GetInt32(1, 7))));
    }

    public async Task<CrapsStatusResponse> StatusAsync(string userId, CancellationToken token = default) =>
        new(true, MinimumStake, MaximumStake, MinimumStake, "paid-pass-line", await ReadAccountBalance(userId, token));

    public async Task<CrapsRoundResponse?> ActiveAsync(string userId, CancellationToken token = default)
    {
        await RecoverPendingAsync(userId, token);
        var player = await Player(userId).GetSnapshotAsync(token);
        var id = ActiveId(player, userId);
        if (id is null) return null;
        return await GetAsync(userId, id.Value, token);
    }
    public async Task<CrapsRoundResponse> GetAsync(string userId, Guid roundId, CancellationToken token = default)
    {
        await RecoverPendingAsync(userId, token);
        var round = ReadRound(await Round(roundId).GetSnapshotAsync(token), userId);
        return ToResponse(round, await ReadAccountBalance(userId, token));
    }

    public Task<CrapsRoundResponse> StartAsync(string userId, StartCrapsRoundRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = OperationId(request.OperationId); ValidateStake(request.Stake);
        var extras = InitialExtras(request.ExtraBets);
        var fingerprint = Fingerprint("start", request.Stake, extras.Select(extra => new { extra.Kind, extra.Stake, extra.Resolved, extra.Won, extra.TotalReturn }).ToArray());
        extras = extras.Select(extra => extra with { WagerId = operationId + ":" + extra.Kind, PlacedForRoll = 1 }).ToArray();
        var id = RoundId(userId, operationId);
        return ExecuteAsync(userId, operationId, fingerprint, id, "start", (saved, active, now) =>
        {
            if (saved is not null) throw new InvalidOperationException("A Craps round exists without its opening operation.");
            if (active is not null) throw new CrapsPaidException("craps-round-active", "Finish the current Craps round before starting another.", 409);
            if (acceptsNewRounds?.Invoke() == false) throw new CrapsPaidException("craps-disabled", "Craps is not accepting new rounds.", 503);
            return new PaidCrapsRound(id, userId, CrapsEngine.StartPassLine(new CrapsPassLineBet(userId, request.Stake)), extras,
                Cents(request.Stake + extras.Sum(extra => extra.Stake)), 0, now, null, 0,
                [new(operationId, "start", null, request.Stake, 0, null, null, 0, 0)], 2);
        }, token);
    }
    public Task<CrapsRoundResponse> PlaceOddsAsync(string userId, Guid roundId, PlaceCrapsOddsRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeWagerAsync(userId, roundId, new("odds", request.Stake, request.OperationId, request.ExpectedRevision, request.ExpectedRollNumber), "odds", token);
    }
    public Task<CrapsRoundResponse> SetWagerAsync(string userId, Guid roundId, SetCrapsWagerRequest request, CancellationToken token = default) =>
        ChangeWagerAsync(userId, roundId, request, "wager", token);
    private Task<CrapsRoundResponse> ChangeWagerAsync(string userId, Guid roundId, SetCrapsWagerRequest request, string actionKind, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = OperationId(request.OperationId); var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("odds" or "field" or "any-seven" or "any-craps"))
            throw new CrapsPaidException("craps-invalid-bets", "Choose a supported adjustable wager; the Pass Line principal stays locked.", 400);
        if (request.Stake != 0m) ValidateStake(request.Stake);
        var fingerprint = actionKind == "odds" && request.ExpectedRevision is null && request.ExpectedRollNumber is null
            ? Fingerprint("odds", roundId, request.Stake)
            : Fingerprint(actionKind, roundId, kind, request.Stake, request.ExpectedRevision, request.ExpectedRollNumber);
        return ExecuteAsync(userId, operationId, fingerprint, roundId, actionKind, (saved, active, now) =>
        {
            var round = saved ?? throw new CrapsAccessException(); ValidateRevision(round, request.ExpectedRevision, request.ExpectedRollNumber);
            if (active != roundId || round.State.Phase != CrapsPassLinePhase.Point || round.State.Point is null)
                throw new CrapsPaidException("craps-wager-unavailable", "Adjust odds and next-roll wagers only between rolls while a point is established.", 409);
            var previous = round.Extras.SingleOrDefault(extra => extra.Kind == kind && !extra.Resolved);
            if ((previous?.Stake ?? 0m) == request.Stake) throw new CrapsPaidException("craps-wager-unchanged", "This wager already has the requested amount.", 409);
            var delta = Cents(request.Stake - (previous?.Stake ?? 0m));
            var extras = round.Extras.Select(extra => ReferenceEquals(extra, previous)
                ? extra with { Resolved = true, Won = false, TotalReturn = 0m, Cancelled = true, ResolvedOnRoll = null } : extra).ToList();
            if (request.Stake > 0m) extras.Add(new(kind, request.Stake, false, false, null, operationId + ":" + kind, round.State.Rolls.Length + 1));
            return round with { Extras = [..extras], TotalWagerCents = checked(round.TotalWagerCents + delta), Revision = round.Revision + 1, SchemaVersion = 2,
                Actions = [..round.Actions ?? [], new(operationId, actionKind, kind, request.Stake, round.Revision + 1, request.ExpectedRevision, request.ExpectedRollNumber, 0, 0)] };
        }, token);
    }
    public Task<CrapsRoundResponse> RollAsync(string userId, Guid roundId, CrapsOperationRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = OperationId(request.OperationId);
        var fingerprint = request.ExpectedRevision is null && request.ExpectedRollNumber is null ? Fingerprint("roll", roundId)
            : Fingerprint("roll", roundId, request.ExpectedRevision, request.ExpectedRollNumber);
        DicePair? dice = null;
        return ExecuteAsync(userId, operationId, fingerprint, roundId, "roll", (saved, active, now) =>
        {
            var round = saved ?? throw new CrapsAccessException(); ValidateRevision(round, request.ExpectedRevision, request.ExpectedRollNumber);
            if (active != roundId || round.State.Phase == CrapsPassLinePhase.Resolved)
                throw new CrapsPaidException("craps-round-complete", "This Craps round has finished.", 409);
            dice ??= draw(); dice.Value.Validate();
            var state = CrapsEngine.Roll(round.State, dice.Value).State;
            var extras = SettleExtras(round.Extras, state, dice.Value.Total);
            var terminal = state.Phase == CrapsPassLinePhase.Resolved;
            var payout = Cents(extras.Where((extra, index) => !round.Extras[index].Resolved && extra.Resolved).Sum(extra => extra.TotalReturn ?? 0m)
                + (terminal ? state.LastOutcome?.TotalReturn ?? 0m : 0m));
            return round with { State = state, Extras = extras, TotalReturnCents = checked(round.TotalReturnCents + payout),
                CompletedAtUtc = terminal ? now : null, Revision = round.Revision + 1, SchemaVersion = 2,
                Actions = [..round.Actions ?? [], new(operationId, "roll", null, null, round.Revision + 1, request.ExpectedRevision, request.ExpectedRollNumber, 0, 0)] };
        }, token);
    }

    private void WriteSettlement(Transaction transaction, PaidCrapsRound round, DateTime now)
    {
        var record = SinglePlayerRoundRecord.Create("craps", round.Id.ToString("N"), round.UserId,
            round.TotalWagerCents, round.TotalReturnCents, round.StartedAtUtc, now,
            "crapsPaidRounds", round.Id.ToString("N"), round, "craps-private-dice-and-bets-v1");
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
    }
    private void WriteObservation(Transaction transaction, PaidCrapsRound round, string kind, DateTime now) =>
        FirestoreGameAudit.WriteObservation(transaction, database, "craps", round.Id.ToString("N"), round.Revision,
            now, new { round.State, round.Extras, round.TotalWagerCents, round.TotalReturnCents, round.Actions, round.SchemaVersion }, [round.UserId], kind);

    private static string OperationId(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id.ToString("N") : throw new CrapsPaidException("craps-invalid-operation", "A unique operation ID is required.", 400);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Fingerprint(params object?[] parts) => Hash(JsonSerializer.Serialize(parts));
    private static Guid RoundId(string userId, string operationId) => new(SHA256.HashData(Encoding.UTF8.GetBytes(userId + "\n" + operationId))[..16]);

    private DocumentReference Player(string userId) => database.Collection("crapsPaidPlayers").Document(Hash(userId));
    private DocumentReference Round(Guid id) => database.Collection("crapsPaidRounds").Document(id.ToString("N"));
    private DocumentReference Operation(string userId, string operationId) => database.Collection("crapsPaidOperations").Document(Hash(userId + "\n" + operationId));

    private static Guid? ActiveId(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists) return null;
        if (!snapshot.TryGetValue<string>("userId", out var owner) || owner != userId)
            throw new InvalidOperationException("The Craps player pointer is corrupt.");
        if (!snapshot.TryGetValue<string>("activeRoundId", out var raw) || string.IsNullOrEmpty(raw)) return null;
        return Guid.TryParseExact(raw, "N", out var id) ? id : throw new InvalidOperationException("The Craps active-round pointer is corrupt.");
    }

    internal static PaidCrapsRound ReadRound(DocumentSnapshot snapshot, string userId)
    {
        try { return DecodeRound(snapshot, userId); }
        catch (Exception error) when (error is JsonException or ArgumentException or CrapsRuleException or OverflowException)
        { throw new InvalidOperationException("The saved Craps round could not be verified.", error); }
    }
    private static PaidCrapsRound DecodeRound(DocumentSnapshot snapshot, string userId)
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
        var replayed = CrapsEngine.StartPassLine(round.State.Bet);
        foreach (var dice in round.State.Rolls) replayed = CrapsEngine.Roll(replayed, dice).State;
        Require(replayed.Phase == round.State.Phase && replayed.Point == round.State.Point &&
            replayed.Rolls.SequenceEqual(round.State.Rolls) && replayed.LastOutcome == round.State.LastOutcome);
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
