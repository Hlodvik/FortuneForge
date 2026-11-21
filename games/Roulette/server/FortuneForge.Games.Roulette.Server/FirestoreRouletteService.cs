using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;

namespace FortuneForge.Server.Games.Roulette;

/// <summary>Transactions own the table roster, checkpoints, private history and exact command receipts.</summary>
public sealed class FirestoreRouletteService(FirestoreDb database, IRoulettePopulation population,
    Func<bool>? acceptsNewActions = null, Func<int>? draw = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DocumentReference Directory => database.Collection("rouletteRuntime").Document("tables");
    private DocumentReference Table(Guid id) => database.Collection("rouletteTables").Document(id.ToString("N"));
    private DocumentReference View(string userId, Guid id) => database.Collection("rouletteAccountRounds").Document(Hash(userId + "\n" + id.ToString("N")));
    private DocumentReference Operation(string userId, string operationId) => database.Collection("rouletteOperations").Document(Hash(userId + "\n" + operationId));
    public RouletteStatusResponse Status() => new RouletteGameService().Status() with { Available = acceptsNewActions?.Invoke() != false };

    public Task<RouletteRoundResponse> StartAsync(string userId, string? name, string operationId, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "joined", null, null, null, service => service.StartAuditedAsync(userId, name, token), token);
    public Task<RouletteRoundResponse> StartAsync(string userId, Func<CancellationToken, Task<string>> trustedName, string operationId, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "joined", null, null, null, async service => await service.StartAuditedAsync(userId, await trustedName(token), token), token);
    public Task<RouletteRoundResponse> PlaceBetAsync(string userId, Guid id, PlaceRouletteBetRequest request, string operationId, long? revision, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "bet-placed", id, request, revision, service => service.PlaceBetAuditedAsync(userId, id, request, token), token);
    public Task<RouletteRoundResponse> RemoveBetAsync(string userId, Guid id, int index, string operationId, long? revision, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "bet-removed", id, index, revision, service => service.RemoveBetAuditedAsync(userId, id, index, token), token);
    public Task<RouletteRoundResponse> ClearBetsAsync(string userId, Guid id, string operationId, long? revision, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "bets-cleared", id, null, revision, service => service.ClearBetsAuditedAsync(userId, id, token), token);
    public Task<RouletteRoundResponse> SpinAsync(string userId, Guid id, string operationId, long? revision, CancellationToken token = default) =>
        ExecuteAsync(userId, operationId, "spun", id, null, revision, service => service.SpinAuditedAsync(userId, id, token), token);
    public Task<RouletteRoundResponse> GetAsync(string userId, Guid id, CancellationToken token = default) =>
        ExecuteAsync(userId, null, "observed", id, null, null, service => service.GetAuditedAsync(userId, id, token), token);

    public async Task<RouletteRoundResponse> HistoryAsync(string userId, Guid tableId, int roundNumber, CancellationToken token = default)
    {
        var document = await History(userId, tableId, roundNumber).GetSnapshotAsync(token);
        return ReadResponse(document, userId);
    }

    private Task<RouletteRoundResponse> ExecuteAsync(string userId, string? operationId, string kind, Guid? tableId,
        object? request, long? revision, Func<RouletteGameService, Task<RouletteRoundResponse>> action, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (operationId is not null) ValidateOperation(operationId);
        var fingerprint = Hash(JsonSerializer.Serialize(new { kind, tableId, request, revision }, Json));
        int? pocket = null;
        int Draw() { pocket ??= draw?.Invoke() ?? RandomNumberGenerator.GetInt32(0, 37); return pocket.Value; }
        return database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot? operation = null;
            if (operationId is not null)
            {
                operation = await transaction.GetSnapshotAsync(Operation(userId, operationId), token);
                if (operation.Exists)
                {
                    if (operation.GetValue<string>("userId") != userId || operation.GetValue<string>("operationId") != operationId || operation.GetValue<string>("fingerprint") != fingerprint)
                        throw new RouletteConflictException("This operation was already used for a different Roulette action.");
                    return ReadResponse(operation, userId);
                }
                if (acceptsNewActions?.Invoke() == false)
                    throw new RouletteUnavailableException();
            }
            var directory = await transaction.GetSnapshotAsync(Directory, token);
            var ids = directory.Exists ? directory.GetValue<string[]>("tableIds") : [];
            if (ids.Length > 256) throw new InvalidOperationException("The Roulette table roster exceeds its transaction budget.");
            var references = ids.Select(id => Table(Guid.ParseExact(id, "N"))).ToArray();
            var checkpoints = references.Length == 0 ? [] : await transaction.GetAllSnapshotsAsync(references, token);
            if (checkpoints.Any(checkpoint => !checkpoint.Exists)) throw new InvalidOperationException("The Roulette table directory is inconsistent.");
            var recorder = new CaptureRecorder();
            var service = new RouletteGameService(population, events: recorder,
                draw: Draw);
            service.ImportTables(checkpoints.Select(checkpoint => checkpoint.GetValue<string>("stateJson")));
            if (tableId is { } expectedId && operationId is not null) service.RequireRevision(userId, expectedId, revision);
            RouletteRoundResponse result;
            try { result = await action(service); }
            catch (RouletteAccessException) when (operationId is null && tableId is { })
            {
                var history = ReadResponse(await transaction.GetSnapshotAsync(View(userId, tableId.Value), token), userId);
                return history.Phase == "settled" ? history : throw new RouletteAccessException();
            }
            var observation = recorder.Snapshots.Last();
            result = result with { Revision = observation.Revision };
            var exported = service.ExportTables();
            if (exported.Count > 256) throw new RouletteConflictException("The Roulette tables are busy. Try again shortly.");
            var auditExists = observation.Completion is null ? null :
                await transaction.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Summaries).Document(observation.Completion.Summary.Id), token);
            var before = checkpoints.ToDictionary(checkpoint => checkpoint.Id, checkpoint => checkpoint.GetValue<string>("stateJson"));
            foreach (var pair in exported)
            {
                if (!before.TryGetValue(pair.Key.ToString("N"), out var prior) || prior != pair.Value)
                {
                    if (Encoding.UTF8.GetByteCount(pair.Value) > 750_000) throw new InvalidOperationException("The Roulette checkpoint exceeds its document budget.");
                    transaction.Set(Table(pair.Key), new Dictionary<string, object> { ["stateJson"] = pair.Value, ["schemaVersion"] = 1L });
                }
            }
            var newIds = exported.Keys.Select(id => id.ToString("N")).Order(StringComparer.Ordinal).ToArray();
            if (!ids.SequenceEqual(newIds)) transaction.Set(Directory, new Dictionary<string, object> { ["tableIds"] = newIds, ["schemaVersion"] = 1L });
            WriteObservation(transaction, observation, auditExists?.Exists == false);
            if (kind is "bet-placed" or "spun") FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "roulette", userId, observation.OccurredAtUtc);
            var responseFields = ResponseFields(userId, result);
            transaction.Set(View(userId, result.RoundId), responseFields);
            if (result.Phase == "settled" && auditExists?.Exists == false)
            {
                var participants = observation.Completion?.Summary.Participants.Select(participant => participant.ActorId) ?? [userId];
                foreach (var participant in participants.Distinct(StringComparer.Ordinal))
                {
                    var own = service.ProjectRound(result.RoundId, participant) with { Revision = observation.Revision };
                    var ownFields = ResponseFields(participant, own);
                    transaction.Set(History(participant, result.RoundId, result.RoundNumber), ownFields);
                    transaction.Set(View(participant, result.RoundId), ownFields);
                }
            }
            if (operationId is not null)
            {
                responseFields["operationId"] = operationId; responseFields["fingerprint"] = fingerprint;
                transaction.Create(Operation(userId, operationId), responseFields);
            }
            return result;
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: token);
    }

    public Task SweepAsync(DateTime nowUtc, CancellationToken token = default) => database.RunTransactionAsync(async transaction =>
    {
        var directory = await transaction.GetSnapshotAsync(Directory, token);
        if (!directory.Exists) return false;
        var ids = directory.GetValue<string[]>("tableIds");
        if (ids.Length == 0) return false;
        var checkpoints = await transaction.GetAllSnapshotsAsync(ids.Select(id => Table(Guid.ParseExact(id, "N"))), token);
        var recorder = new CaptureRecorder();
        var service = new RouletteGameService(population, events: recorder, draw: draw);
        service.ImportTables(checkpoints.Select(checkpoint => checkpoint.GetValue<string>("stateJson")));
        await service.SweepAuditedAsync(nowUtc, token);
        var existing = new Dictionary<string, bool>();
        foreach (var snapshot in recorder.Snapshots.Where(snapshot => snapshot.Completion is not null))
            existing[snapshot.Completion!.Summary.Id] = (await transaction.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Summaries).Document(snapshot.Completion.Summary.Id), token)).Exists;
        foreach (var pair in service.ExportTables())
            transaction.Set(Table(pair.Key), new Dictionary<string, object> { ["stateJson"] = pair.Value, ["schemaVersion"] = 1L });
        transaction.Set(Directory, new Dictionary<string, object> { ["tableIds"] = service.ExportTables().Keys.Select(id => id.ToString("N")).Order(StringComparer.Ordinal).ToArray(), ["schemaVersion"] = 1L });
        foreach (var snapshot in recorder.Snapshots)
            WriteObservation(transaction, snapshot, snapshot.Completion is not null && !existing[snapshot.Completion.Summary.Id]);
        return true;
    }, cancellationToken: token);

    private void WriteObservation(Transaction transaction, RouletteGameService.RouletteAuditSnapshot snapshot, bool newCompletion)
    {
        var observation = GameSecurityEventDocuments.Capture("roulette", snapshot.InstanceId, snapshot.Version,
            snapshot.OccurredAtUtc, snapshot.State, snapshot.ParticipantIds, snapshot.EventKind, snapshot.TableId);
        FirestoreGameAudit.WriteObservation(transaction, database, observation);
        if (!newCompletion || snapshot.Completion is not { } completion) return;
        FirestoreGameAudit.WriteCompletion(transaction, database, completion);
        foreach (var userId in completion.Summary.Participants.Where(participant => participant.IsAccountBacked).Select(participant => participant.ActorId))
        {
            var settlement = GameAnalyticsOutbox.Create(new("roulette", snapshot.InstanceId, userId, "practice", true,
                snapshot.OccurredAtUtc, null, 0, 0, "rouletteTables", snapshot.TableId));
            if (settlement is not null) transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(settlement.Id), settlement.Fields);
        }
    }
    private DocumentReference History(string userId, Guid id, int round) => database.Collection("rouletteHistory").Document(Hash(userId + "\n" + id.ToString("N") + "\n" + round));
    private static Dictionary<string, object> ResponseFields(string userId, RouletteRoundResponse response) => new()
    {
        ["userId"] = userId, ["responseJson"] = JsonSerializer.Serialize(response, Json), ["tableId"] = response.RoundId.ToString("N"),
        ["roundNumber"] = (long)response.RoundNumber, ["revision"] = response.Revision, ["schemaVersion"] = 1L
    };
    private static RouletteRoundResponse ReadResponse(DocumentSnapshot document, string userId)
    {
        if (!document.Exists || document.GetValue<string>("userId") != userId) throw new RouletteAccessException();
        try
        {
            return JsonSerializer.Deserialize<RouletteRoundResponse>(document.GetValue<string>("responseJson"), Json)
                ?? throw new InvalidOperationException("The Roulette command receipt is corrupt.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or OverflowException)
        { throw new InvalidOperationException("The saved Roulette command could not be verified.", error); }
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateOperation(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.");
    }
    private sealed class CaptureRecorder : IRouletteEventRecorder
    {
        public List<RouletteGameService.RouletteAuditSnapshot> Snapshots { get; } = [];
        public Task WriteAsync(RouletteGameService.RouletteAuditSnapshot snapshot, CancellationToken token) { Snapshots.Add(snapshot); return Task.CompletedTask; }
    }
}
public sealed class RouletteUnavailableException() : Exception("Roulette is not accepting new actions.");
