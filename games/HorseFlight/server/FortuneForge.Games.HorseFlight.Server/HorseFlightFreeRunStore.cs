using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.HorseFlight.Server;

public class HorseFlightFreeRunStore(
    FirestoreDb database,
    IGameWalletAccountReader accountReader,
    TimeProvider? clock = null,
    Func<uint>? createSeed = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly Func<uint> seedSource = createSeed ?? (() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue));
    public Task<HorseFlightStartResponse> StartAsync(string userId, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateKey(idempotencyKey);
        var runId = $"horse_flight_free_{Hash($"{userId}\n{idempotencyKey}")}";
        var runRef = RunDocument(runId); var eventRef = EventDocument(runId, "started");
        var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(runRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            if (reads[0].Exists)
            {
                var run = ReadRun(reads[0]);
                if (run.UserId != userId || !reads[1].Exists) throw new HorseFlightRunConflictException("This Horse Flight run cannot be replayed safely.");
                return new HorseFlightStartResponse(run.RunId, run.Seed, HorseFlightEngine.TickMilliseconds) { WasReplay = true, RulesVersion = run.RulesVersion };
            }
            if (reads[1].Exists) throw new HorseFlightRunConflictException("A Horse Flight event exists without its run.");
            var seed = seedSource();
            if (seed == 0) throw new InvalidOperationException("The Horse Flight seed source returned zero.");
            transaction.Create(runRef, new Dictionary<string, object> { ["runId"] = runId, ["userId"] = userId, ["seed"] = (long)seed, ["phase"] = "running", ["rulesVersion"] = (long)HorseFlightEngine.CurrentRulesVersion, ["startIdempotencyKey"] = idempotencyKey, ["createdAt"] = Timestamp.FromDateTime(startedAtUtc), ["schemaVersion"] = 1L });
            transaction.Create(eventRef, Event(runId, userId, "started", idempotencyKey));
            ArcadeRunSecurityAudit.Started(transaction, database, "horse-flight", runId, userId,
                startedAtUtc, new { runId, seed, rulesVersion = HorseFlightEngine.CurrentRulesVersion,
                    tickMilliseconds = HorseFlightEngine.TickMilliseconds });
            return new HorseFlightStartResponse(runId, seed, HorseFlightEngine.TickMilliseconds);
        }, cancellationToken: cancellationToken);
    }

    public async Task<HorseFlightRunResponse> CompleteAsync(string userId, string runId, CompleteHorseFlightRunRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateId(runId); ValidateKey(idempotencyKey); ArgumentNullException.ThrowIfNull(request);
        var runRef = RunDocument(runId); var eventRef = EventDocument(runId, "completed");
        var previous = await runRef.GetSnapshotAsync(cancellationToken);
        if (!previous.Exists) throw new HorseFlightRunNotFoundException();
        var priorRun = ReadRun(previous);
        if (priorRun.UserId != userId) throw new HorseFlightRunNotFoundException();
        var balance = priorRun.CompletionBalanceCents;
        if (balance is null)
        {
            var account = await accountReader.GetStateAsync(userId, cancellationToken);
            balance = account.SlotsCreditsBalanceCents ?? throw new InvalidOperationException("The account balance is unavailable.");
        }
        if (balance < 0) throw new InvalidOperationException("The account balance is invalid.");
        var completedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return await database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(runRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            if (!reads[0].Exists) throw new HorseFlightRunNotFoundException();
            var run = ReadRun(reads[0]); if (run.UserId != userId) throw new HorseFlightRunNotFoundException();
            var jumps = request.JumpTicks?.ToArray() ?? throw new ArgumentException("A Horse Flight replay requires jump ticks.", nameof(request)); var rightClicks = request.RightClickTicks?.ToArray() ?? [];
            if (run.Completed)
            {
                if (run.TotalTicks != request.TotalTicks || !run.JumpTicks.SequenceEqual(jumps) || !run.RightClickTicks.SequenceEqual(rightClicks) || !reads[1].Exists) throw new HorseFlightRunConflictException("This Horse Flight run was already completed differently.");
                return ToResponse(run, balance.Value, wasReplay: true);
            }
            if (reads[1].Exists) throw new HorseFlightRunConflictException("A Horse Flight completion event exists without a completed run.");
            var replay = HorseFlightReplayEvaluator.Evaluate(run.Seed, new HorseFlightReplay(request.TotalTicks, [.. jumps])
            {
                RightClickTicks = [.. rightClicks],
                RulesVersion = run.RulesVersion,
            });
            var completed = run with { Completed = true, TotalTicks = request.TotalTicks, JumpTicks = jumps, RightClickTicks = rightClicks, Score = replay.Snapshot.Score, Phase = replay.Snapshot.Phase, CompletionBalanceCents = balance };
            transaction.Update(runRef, new Dictionary<string, object> { ["phase"] = PhaseName(completed.Phase), ["rulesVersion"] = (long)completed.RulesVersion, ["totalTicks"] = completed.TotalTicks, ["jumpTicks"] = completed.JumpTicks, ["rightClickTicks"] = completed.RightClickTicks, ["score"] = completed.Score, ["completionBalanceCents"] = balance.Value, ["completionIdempotencyKey"] = idempotencyKey, ["completedAt"] = Timestamp.FromDateTime(completedAtUtc) });
            transaction.Create(eventRef, Event(runId, userId, "completed", idempotencyKey));
            ArcadeRunSecurityAudit.Completed(transaction, database, "horse-flight", runId, userId,
                reads[0].GetValue<Timestamp>("createdAt").ToDateTime(), completedAtUtc, 0,
                "horseFlightRuns", runRef.Id,
                new { runId, run.Seed, run.RulesVersion, request.TotalTicks, jumpTicks = jumps,
                    rightClickTicks = rightClicks, replay.Snapshot.Score, phase = PhaseName(replay.Snapshot.Phase) },
                "horse-flight-replay-v1");
            return ToResponse(completed, balance.Value, wasReplay: false);
        }, cancellationToken: cancellationToken);
    }

    private sealed record Run(string RunId, string UserId, uint Seed, int RulesVersion, bool Completed, int TotalTicks, int[] JumpTicks, int[] RightClickTicks, int Score, HorseFlightPhase Phase, long? CompletionBalanceCents = null);
    private static HorseFlightRunResponse ToResponse(Run run, long balance, bool wasReplay) => new(run.RunId, (run.CompletionBalanceCents ?? balance) / 100m, run.Score, PhaseName(run.Phase), run.TotalTicks, run.JumpTicks)
    {
        RightClickTicks = run.RightClickTicks,
        WasReplay = wasReplay,
    };
    private static Run ReadRun(DocumentSnapshot snapshot)
    {
        if (!snapshot.TryGetValue<string>("runId", out var id) || !IsFreeRunId(id) || !snapshot.TryGetValue<string>("userId", out var user) || string.IsNullOrWhiteSpace(user) || !snapshot.TryGetValue<long>("seed", out var seed) || seed is < 1 or > uint.MaxValue || !snapshot.TryGetValue<string>("phase", out var phase) || !snapshot.TryGetValue<long>("schemaVersion", out var version) || version != 1) throw new InvalidOperationException("The Horse Flight run is corrupt.");
        var rulesVersion = ReadRulesVersion(snapshot);
        if (phase == "running") return new Run(id, user, (uint)seed, rulesVersion, false, 0, [], [], 0, HorseFlightPhase.Running);
        if (!snapshot.TryGetValue<long>("totalTicks", out var ticks) || !snapshot.TryGetValue<long>("score", out var score)) throw new InvalidOperationException("The Horse Flight completion is corrupt.");
        var jumps = ReadInts(snapshot, "jumpTicks"); var rightClicks = ReadInts(snapshot, "rightClickTicks"); var terminal = phase switch { "obstacle-collision" => HorseFlightPhase.ObstacleCollision, "fell" => HorseFlightPhase.Fell, _ => throw new InvalidOperationException("The Horse Flight phase is corrupt.") };
        var replay = HorseFlightReplayEvaluator.Evaluate((uint)seed, new HorseFlightReplay(checked((int)ticks), [.. jumps])
        {
            RightClickTicks = [.. rightClicks],
            RulesVersion = rulesVersion,
        });
        if (replay.Snapshot.Score != score || replay.Snapshot.Phase != terminal) throw new InvalidOperationException("The Horse Flight score is corrupt.");
        long? completedBalance = snapshot.TryGetValue<long>("completionBalanceCents", out var savedBalance) ? savedBalance : null;
        if (completedBalance < 0) throw new InvalidOperationException("The Horse Flight completion balance is corrupt.");
        return new Run(id, user, (uint)seed, rulesVersion, true, checked((int)ticks), jumps, rightClicks, checked((int)score), terminal, completedBalance);
    }
    private DocumentReference RunDocument(string id) => database.Collection("horseFlightRuns").Document(id);
    private DocumentReference EventDocument(string id, string name) => database.Collection("horseFlightRunEvents").Document($"{id}-{name}");
    private Dictionary<string, object> Event(string runId, string userId, string name, string key) => new() { ["runId"] = runId, ["userId"] = userId, ["event"] = name, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), ["schemaVersion"] = 1L };
    private static string PhaseName(HorseFlightPhase phase) => HorseFlightRunReplay.PhaseName(phase);
    private static int ReadRulesVersion(DocumentSnapshot snapshot) => HorseFlightRunReplay.ResolveRulesVersion(
        snapshot.TryGetValue<long>("rulesVersion", out var stored) ? stored : null);
    private static int[] ReadInts(DocumentSnapshot snapshot, string field) => snapshot.ToDictionary().TryGetValue(field, out var raw) && raw is IEnumerable values ? values.Cast<object?>().Select(item => item switch { long value => checked((int)value), int value => value, _ => int.MinValue }).ToArray() : [];
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateKey(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value)); }
    private static void ValidateId(string value) { if (!IsFreeRunId(value)) throw new ArgumentException("The Horse Flight run identifier is invalid.", nameof(value)); }
    private static bool IsFreeRunId(string value) => value.StartsWith("horse_flight_free_", StringComparison.Ordinal) && value.Length == "horse_flight_free_".Length + 64 && value["horse_flight_free_".Length..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
