using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Arcade;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Arcade.HorseFlight;

public sealed record HorseFlightStatusResponse(bool Available, int TickMilliseconds, decimal Balance, string Mode);
public sealed record HorseFlightStartResponse(string RunId, uint Seed, int TickMilliseconds)
{
    public bool WasReplay { get; init; }
}
public sealed record CompleteHorseFlightRunRequest(int TotalTicks, IReadOnlyList<int>? JumpTicks, IReadOnlyList<int>? RightClickTicks = null);
public sealed record HorseFlightRunResponse(string RunId, decimal Balance, int Score, string Phase, int TotalTicks, IReadOnlyList<int> JumpTicks)
{
    public IReadOnlyList<int> RightClickTicks { get; init; } = [];
    public bool WasReplay { get; init; }
}
public sealed record HorseFlightErrorResponse(string Code, string Message);

[ApiController]
[Route("api/games/horse-flight")]
public sealed class HorseFlightController(FirestoreDb database, AccountService accountService, IConfiguration configuration, ILogger<HorseFlightController> logger) : ControllerBase
{
    [HttpGet("status")][EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null ? Unauthorized(new HorseFlightErrorResponse("horse-flight-authentication-required", "Sign in to play Horse Flight.")) : Ok(new HorseFlightStatusResponse(true, HorseFlightEngine.TickMilliseconds, account.Balances.SlotsCredits, "recorded-free-play"));
    }

    [HttpPost("runs")][EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start([FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new HorseFlightErrorResponse("horse-flight-authentication-required", "Sign in to play Horse Flight."));
        try { return Ok(await Store().StartAsync(account.UserId, idempotencyKey ?? string.Empty, cancellationToken)); }
        catch (Exception exception) { return HorseFlightHttp.FromException(this, exception, logger); }
    }

    [HttpPost("runs/{runId}/complete")][EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Complete(string runId, CompleteHorseFlightRunRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new HorseFlightErrorResponse("horse-flight-authentication-required", "Sign in to play Horse Flight."));
        try { return Ok(await Store().CompleteAsync(account.UserId, runId, request, idempotencyKey ?? string.Empty, cancellationToken)); }
        catch (Exception exception) { return HorseFlightHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:HorseFlightEnabled", false);
    private HorseFlightFirestoreStore Store() => new(database);
    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(StatusCodes.Status503ServiceUnavailable, new HorseFlightErrorResponse("horse-flight-disabled", "Horse Flight is still being verified and cannot start a run yet."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) => (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal sealed class HorseFlightRunNotFoundException() : Exception("Horse Flight run not found.");
internal sealed class HorseFlightRunConflictException(string message) : Exception(message);

public sealed class HorseFlightFirestoreStore(FirestoreDb database)
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";
    public Task<HorseFlightStartResponse> StartAsync(string userId, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateKey(idempotencyKey);
        var runId = $"horse_flight_free_{Hash($"{userId}\n{idempotencyKey}")}";
        var runRef = RunDocument(runId); var eventRef = EventDocument(runId, "started");
        var startedAtUtc = DateTime.UtcNow;
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(runRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            if (reads[0].Exists)
            {
                var run = ReadRun(reads[0]);
                if (run.UserId != userId || !reads[1].Exists) throw new HorseFlightRunConflictException("This Horse Flight run cannot be replayed safely.");
                return new HorseFlightStartResponse(run.RunId, run.Seed, HorseFlightEngine.TickMilliseconds) { WasReplay = true };
            }
            if (reads[1].Exists) throw new HorseFlightRunConflictException("A Horse Flight event exists without its run.");
            var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            transaction.Create(runRef, new Dictionary<string, object> { ["runId"] = runId, ["userId"] = userId, ["seed"] = (long)seed, ["phase"] = "running", ["rulesVersion"] = (long)HorseFlightEngine.CurrentRulesVersion, ["startIdempotencyKey"] = idempotencyKey, ["createdAt"] = Timestamp.FromDateTime(startedAtUtc), ["schemaVersion"] = 1L });
            transaction.Create(eventRef, Event(runId, userId, "started", idempotencyKey));
            ArcadeRunSecurityAudit.Started(transaction, database, "horse-flight", runId, userId,
                startedAtUtc, new { runId, seed, rulesVersion = HorseFlightEngine.CurrentRulesVersion,
                    tickMilliseconds = HorseFlightEngine.TickMilliseconds });
            return new HorseFlightStartResponse(runId, seed, HorseFlightEngine.TickMilliseconds);
        }, cancellationToken: cancellationToken);
    }

    public Task<HorseFlightRunResponse> CompleteAsync(string userId, string runId, CompleteHorseFlightRunRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateId(runId); ValidateKey(idempotencyKey); ArgumentNullException.ThrowIfNull(request);
        var runRef = RunDocument(runId); var balanceRef = BalanceDocument(userId); var eventRef = EventDocument(runId, "completed");
        var completedAtUtc = DateTime.UtcNow;
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(runRef, cancellationToken), transaction.GetSnapshotAsync(balanceRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            if (!reads[0].Exists) throw new HorseFlightRunNotFoundException();
            var run = ReadRun(reads[0]); if (run.UserId != userId) throw new HorseFlightRunNotFoundException();
            var balance = ReadBalance(reads[1]); var jumps = request.JumpTicks?.ToArray() ?? throw new ArgumentException("A Horse Flight replay requires jump ticks.", nameof(request)); var rightClicks = request.RightClickTicks?.ToArray() ?? [];
            if (run.Completed)
            {
                if (run.TotalTicks != request.TotalTicks || !run.JumpTicks.SequenceEqual(jumps) || !run.RightClickTicks.SequenceEqual(rightClicks) || !reads[2].Exists) throw new HorseFlightRunConflictException("This Horse Flight run was already completed differently.");
                return ToResponse(run, balance, wasReplay: true);
            }
            if (reads[2].Exists) throw new HorseFlightRunConflictException("A Horse Flight completion event exists without a completed run.");
            var replay = HorseFlightReplayEvaluator.Evaluate(run.Seed, new HorseFlightReplay(request.TotalTicks, [.. jumps])
            {
                RightClickTicks = [.. rightClicks],
                RulesVersion = run.RulesVersion,
            });
            var completed = run with { Completed = true, TotalTicks = request.TotalTicks, JumpTicks = jumps, RightClickTicks = rightClicks, Score = replay.Snapshot.Score, Phase = replay.Snapshot.Phase };
            transaction.Update(runRef, new Dictionary<string, object> { ["phase"] = PhaseName(completed.Phase), ["rulesVersion"] = (long)completed.RulesVersion, ["totalTicks"] = completed.TotalTicks, ["jumpTicks"] = completed.JumpTicks, ["rightClickTicks"] = completed.RightClickTicks, ["score"] = completed.Score, ["completionIdempotencyKey"] = idempotencyKey, ["completedAt"] = Timestamp.FromDateTime(completedAtUtc) });
            transaction.Create(eventRef, Event(runId, userId, "completed", idempotencyKey));
            ArcadeRunSecurityAudit.Completed(transaction, database, "horse-flight", runId, userId,
                reads[0].GetValue<Timestamp>("createdAt").ToDateTime(), completedAtUtc, 0,
                "horseFlightRuns", runRef.Id,
                new { runId, run.Seed, run.RulesVersion, request.TotalTicks, jumpTicks = jumps,
                    rightClickTicks = rightClicks, replay.Snapshot.Score, phase = PhaseName(replay.Snapshot.Phase) },
                "horse-flight-replay-v1");
            return ToResponse(completed, balance, wasReplay: false);
        }, cancellationToken: cancellationToken);
    }

    private sealed record Run(string RunId, string UserId, uint Seed, int RulesVersion, bool Completed, int TotalTicks, int[] JumpTicks, int[] RightClickTicks, int Score, HorseFlightPhase Phase);
    private static HorseFlightRunResponse ToResponse(Run run, long balance, bool wasReplay) => new(run.RunId, RandMoney.CentsToRand(balance), run.Score, PhaseName(run.Phase), run.TotalTicks, run.JumpTicks)
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
        return new Run(id, user, (uint)seed, rulesVersion, true, checked((int)ticks), jumps, rightClicks, checked((int)score), terminal);
    }
    private DocumentReference RunDocument(string id) => database.Collection("horseFlightRuns").Document(id);
    private DocumentReference EventDocument(string id, string name) => database.Collection("horseFlightRunEvents").Document($"{id}-{name}");
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private static Dictionary<string, object> Event(string runId, string userId, string name, string key) => new() { ["runId"] = runId, ["userId"] = userId, ["event"] = name, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.GetCurrentTimestamp(), ["schemaVersion"] = 1L };
    private static string PhaseName(HorseFlightPhase phase) => phase == HorseFlightPhase.ObstacleCollision ? "obstacle-collision" : phase == HorseFlightPhase.Fell ? "fell" : "running";
    private static long ReadBalance(DocumentSnapshot snapshot) => checked(ReadLong(snapshot, "available") * RandMoney.CentsPerRand + Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
    private static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static int ReadRulesVersion(DocumentSnapshot snapshot)
    {
        if (!snapshot.TryGetValue<long>("rulesVersion", out var stored))
            return HorseFlightEngine.LegacyRulesVersion;
        return stored is HorseFlightEngine.LegacyRulesVersion or HorseFlightEngine.DraftMultiBiomeRulesVersion or HorseFlightEngine.PreviousRulesVersion or HorseFlightEngine.CurrentRulesVersion
            ? (int)stored
            : throw new InvalidOperationException("The Horse Flight rules version is corrupt.");
    }
    private static int[] ReadInts(DocumentSnapshot snapshot, string field) => snapshot.ToDictionary().TryGetValue(field, out var raw) && raw is IEnumerable values ? values.Cast<object?>().Select(item => item switch { long value => checked((int)value), int value => value, _ => int.MinValue }).ToArray() : [];
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateKey(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value)); }
    private static void ValidateId(string value) { if (!IsFreeRunId(value)) throw new ArgumentException("The Horse Flight run identifier is invalid.", nameof(value)); }
    private static bool IsFreeRunId(string value) => value.StartsWith("horse_flight_free_", StringComparison.Ordinal) && value.Length == "horse_flight_free_".Length + 64 && value["horse_flight_free_".Length..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class HorseFlightHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        HorseFlightRunNotFoundException => controller.NotFound(new HorseFlightErrorResponse("horse-flight-run-not-found", exception.Message)),
        HorseFlightRunConflictException => controller.Conflict(new HorseFlightErrorResponse("horse-flight-run-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new HorseFlightErrorResponse("horse-flight-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };
    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger) { logger.LogError(exception, "Horse Flight request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier); return controller.Problem("The Horse Flight run could not be completed.", statusCode: StatusCodes.Status500InternalServerError); }
}
