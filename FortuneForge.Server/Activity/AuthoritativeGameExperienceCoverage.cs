using System.Text.Json;
using System.Text.RegularExpressions;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Roulette;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Activity;

internal sealed record GameExperienceCoverageProof(string GameId, string ActivationId, DateTime AsOfUtc,
    string ApiRevision, string SourceRevision, DateTime ValidUntilUtc, string? SourceVersion = null,
    string? SourceProofSha256 = null, string ReceiptKey = "slotGuards", string? OriginId = null);
internal sealed record AuthoritativeGameExperienceRead(IReadOnlyList<PlayerGameExperienceEvidence> Evidence,
    IReadOnlyList<GameExperienceCoverageProof> Complete, IReadOnlyList<string> AbsentSlots, DateTime? AsOfUtc);

// Activation is operator-owned. The API never establishes historical coverage
// from account age, a missing recent list, or its first observation of a player.
internal sealed class AuthoritativeGameExperienceCoverage(
    FirestoreDb database, TimeProvider clock, ILogger logger, IConfiguration? configuration)
{
    internal const string ActivationCollection = "catalogGameExperienceCoverage";
    internal const string ActivationDocument = "slot-guards-v1";
    internal const string SourceVersion = "slot-spin-guards-lifetime-v1";
    internal const string SourceOrigin = "0e4efe78821a0fde1c36671d8eff7f9f448a7923";
    internal static readonly string SourceProofSha256 = AcceptedGameActivityReader.Hash(
        SourceVersion + "\n" + SourceOrigin + "\natomic-accepted-spin:lastSpinStartedAt;permanent-guard;" +
        "key=userId_SHA256(runtimeGameId);wukong-alias=classic-demo-v1\n" +
        string.Join("\n", AcceptedGameActivityReader.SlotIds.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value)));

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GuardKeys(string userId) =>
        AcceptedGameActivityReader.SlotIds.ToDictionary(pair => pair.Value,
            pair => (IReadOnlyList<string>)(pair.Key == "wukong-journey-v1"
                ? new[] { Key(userId, pair.Key), Key(userId, "classic-demo-v1") }
                : new[] { Key(userId, pair.Key) }), StringComparer.Ordinal);
    private static string Key(string userId, string runtime) => userId + "_" + AcceptedGameActivityReader.Hash(runtime);

    internal async Task<AuthoritativeGameExperienceRead> ReadAsync(string userId, CancellationToken ct)
    {
        var keys = GuardKeys(userId);
        var references = keys.Values.SelectMany(value => value).Distinct(StringComparer.Ordinal)
            .Select(key => database.Collection("slotSpinGuards").Document(key)).ToArray();
        var snapshots = await database.GetAllSnapshotsAsync(references, ct);
        if (snapshots.Count != references.Length) return new([], [], [], null);
        var documents = snapshots.ToDictionary(snapshot => snapshot.Id, StringComparer.Ordinal);
        var positive = new List<PlayerGameExperienceEvidence>();
        var absent = new List<string>();
        var asOf = snapshots.Min(snapshot => snapshot.ReadTime.ToDateTime());
        foreach (var (game, aliases) in keys)
        {
            if (aliases.Any(key => !documents.ContainsKey(key))) continue;
            var owned = aliases.Select(key => documents[key]).ToArray();
            if (owned.All(snapshot => !snapshot.Exists)) absent.Add(game);
            // Pending, failed, legacy and malformed guards cannot prove actual
            // settled play or newness. Independent positive evidence wins.
        }
        var revision = configuration?["K_REVISION"];
        var sourceRevision = configuration?["FF_RELEASE_SHA"];
        if (revision is null || sourceRevision is null ||
            !Regex.IsMatch(sourceRevision, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant) || absent.Count == 0) return new(positive, [], absent, asOf);
        DocumentSnapshot activation;
        try
        {
            activation = await database.Collection(ActivationCollection).Document(ActivationDocument).GetSnapshotAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Slot lifetime coverage activation is unavailable; absence remains unknown");
            return new(positive, [], absent, asOf);
        }
        var id = activation.Exists
            ? ValidateActivation(activation.ToDictionary(), revision, sourceRevision, asOf, clock.GetUtcNow().UtcDateTime)
            : null;
        return new(positive, id is null ? [] : absent.Select(game =>
            new GameExperienceCoverageProof(game, id, asOf, revision, sourceRevision,
                activation.GetValue<Timestamp>("validUntilUtc").ToDateTime())).ToArray(), absent, asOf);
    }

    internal static string? ValidateActivation(IReadOnlyDictionary<string, object> fields, string revision,
        string sourceRevision, DateTime asOf, DateTime now)
    {
        if (!fields.TryGetValue("schemaVersion", out var schema) || schema is not 1L ||
            Text(fields, "status") != "active" || Text(fields, "sourceVersion") != SourceVersion ||
            Text(fields, "sourceOrigin") != SourceOrigin || Text(fields, "sourceProofSha256") != SourceProofSha256 ||
            !fields.TryGetValue("lifetimeCoverage", out var lifetime) || lifetime is not true ||
            Text(fields, "activationId") is not { Length: > 0 and <= 48 } id || id.Any(char.IsControl) ||
            !Time(fields, "activatedAtUtc", out var activated) || !Time(fields, "verifiedAtUtc", out var verified) ||
            !Time(fields, "validUntilUtc", out var until) || verified < activated || verified > asOf ||
            activated > asOf || verified > now || until <= now || until <= asOf ||
            until > verified.AddHours(24) ||
            !TryStrings(fields, "gameIds", out var gameIds) || !gameIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(AcceptedGameActivityReader.SlotIds.Values)) return null;
        if (!TryStrings(fields, "servingApiRevisions", out var serving) || serving.Length == 0 ||
            !serving.Contains(revision, StringComparer.Ordinal) ||
            !fields.TryGetValue("compatibleApiRevisions", out var raw) || raw is not IEnumerable<object> revisions) return null;
        var compatible = revisions.OfType<Dictionary<string, object>>().ToArray();
        if (compatible.Length != revisions.Count() || compatible.Length != serving.Length || compatible.Select(item => Text(item, "apiRevision"))
            .Distinct(StringComparer.Ordinal).Count() != serving.Length) return null;
        foreach (var active in serving)
        {
            var entry = compatible.SingleOrDefault(item => Text(item, "apiRevision") == active);
            if (entry is null || Text(entry, "sourceVersion") != SourceVersion ||
                Text(entry, "sourceRevision") is not { } commit || !Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant) ||
                active == revision && commit != sourceRevision) return null;
        }
        return id;
    }

    private static string? Text(IReadOnlyDictionary<string, object> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value as string : null;
    private static bool TryStrings(IReadOnlyDictionary<string, object> fields, string key, out string[] result)
    {
        result = [];
        if (!fields.TryGetValue(key, out var raw) || raw is not IEnumerable<object> values) return false;
        var all = values.ToArray(); result = all.OfType<string>().ToArray();
        return all.Length == result.Length && result.All(value => value.Length is > 0 and <= 128 && !value.Any(char.IsControl)) &&
            result.Distinct(StringComparer.Ordinal).Count() == result.Length;
    }
    private static bool Time(IReadOnlyDictionary<string, object> fields, string key, out DateTime value)
    {
        value = default;
        if (!fields.TryGetValue(key, out var raw) || raw is not Timestamp timestamp) return false;
        value = timestamp.ToDateTime(); return true;
    }
}

// These engines already await the acknowledged security event commit. Add a
// small monotonic witness to that same commit, without changing audit payloads.
internal static class GameExperienceSecurityWitness
{
    internal static IReadOnlyList<string> Players(GameSecurityEventDocument observation)
    {
        if (!observation.Fields.TryGetValue("gameId", out var rawGame) || rawGame is not string game ||
            game is not ("hearts" or "2048" or "drop-merge" or "liars-dice" or "craps" or "roulette") ||
            !observation.Fields.TryGetValue("payloadJson", out var rawPayload) || rawPayload is not string payload) return [];
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("state", out var state) ||
                !state.TryGetProperty("state", out var engine)) return [];
            var actors = observation.Fields.TryGetValue("participantIds", out var participants) && participants is IEnumerable<string> ids
                ? ids.Where(ValidPlayer).Distinct(StringComparer.Ordinal).ToArray() : [];
            if ((game is "2048" or "drop-merge") && (!engine.TryGetProperty("moves", out var moves) || !moves.TryGetInt64(out var count) || count <= 0)) return [];
            if (game == "hearts" && (!engine.TryGetProperty("phase", out _) ||
                !state.TryGetProperty("id", out var match) || match.ValueKind != JsonValueKind.String)) return [];
            if (game == "liars-dice" && (!state.TryGetProperty("id", out var liarsId) || liarsId.ValueKind != JsonValueKind.String ||
                !engine.TryGetProperty("roundNumber", out var roundNumber) || !roundNumber.TryGetInt32(out var number) || number <= 0 ||
                !engine.TryGetProperty("allPlayerIds", out var players) || players.ValueKind != JsonValueKind.Array ||
                !players.EnumerateArray().Any(player => player.ValueKind == JsonValueKind.String && player.GetString() == "you") ||
                !engine.TryGetProperty("round", out var round) || !round.TryGetProperty("hands", out var hands) ||
                hands.ValueKind != JsonValueKind.Object || !hands.EnumerateObject().Any(hand => hand.Value.ValueKind == JsonValueKind.Array && hand.Value.GetArrayLength() > 0))) return [];
            if (game == "craps")
            {
                if (!engine.TryGetProperty("bet", out var bet) || !PositiveStake(bet) ||
                    !bet.TryGetProperty("playerId", out var player) || player.ValueKind != JsonValueKind.String) return [];
                return actors.Where(actor => actor == player.GetString()).ToArray();
            }
            if (game == "roulette")
            {
                if (!engine.TryGetProperty("phase", out var phase) || !phase.TryGetInt32(out var value) || value != (int)RouletteRoundPhase.Settled ||
                    !engine.TryGetProperty("bets", out var bets) || bets.ValueKind != JsonValueKind.Array ||
                    !state.TryGetProperty("humanParticipants", out var humans) || humans.ValueKind != JsonValueKind.Array) return [];
                var humanIds = humans.EnumerateArray().Where(human => human.ValueKind == JsonValueKind.String)
                    .Select(human => human.GetString()!).ToHashSet(StringComparer.Ordinal);
                return bets.EnumerateArray().Where(PositiveStake)
                    .Where(bet => bet.TryGetProperty("playerId", out var player) && player.ValueKind == JsonValueKind.String)
                    .Select(bet => bet.GetProperty("playerId").GetString()!)
                    .Where(player => humanIds.Contains(player) && actors.Contains(player, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            return actors;
        }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
    }

    private static bool ValidPlayer(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 && !id.Any(char.IsControl);
    private static bool PositiveStake(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("stake", out var stake) && stake.TryGetDecimal(out var amount) && amount > 0;

    internal static void Write(FirestoreDb database, GameSecurityEventDocument observation,
        Action<DocumentReference, Dictionary<string, object>> set)
    {
        if (!observation.Fields.TryGetValue("occurredAtUtc", out var rawTime) || rawTime is not DateTime at || at.Kind != DateTimeKind.Utc) return;
        foreach (var userId in Players(observation))
        {
            var document = AcceptedGameParticipationDocuments.Create(userId, (string)observation.Fields["gameId"], at);
            document.Fields["experienceProvenance"] = FieldValue.ArrayUnion("accepted-security-commit");
            set(database.Collection("catalogPlayerActivity").Document(document.DocOwnerKey)
                .Collection("games").Document(document.GameId), document.Fields);
        }
    }
}
