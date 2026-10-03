using System.Security.Cryptography;
using FortuneForge.Games.Flappy;
using FortuneForge.Server.Arcade.Competition;

namespace FortuneForge.Server.Arcade.Flappy;

/// <summary>Creates and verifies paid Flappy runs while the shared coordinator owns admission and balance mutation.</summary>
public sealed class ArcadeCompetitionFlappyPaidEntryService
{
    private readonly IArcadeCompetitionFlappyPaidEntryCoordinator coordinator;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;
    private readonly Func<uint> createSeed;

    internal ArcadeCompetitionFlappyPaidEntryService(
        IArcadeCompetitionFlappyPaidEntryCoordinator coordinator,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions rulesOptions,
        Func<uint>? createSeed = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? throw new ArgumentNullException(nameof(rulesOptions));
        this.createSeed = createSeed ?? CreateCryptographicNonZeroSeed;
    }

    internal Task<ArcadeCompetitionFlappyPaidEntryResult> StartAttemptAsync(
        string attemptId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity("flappy", windowKind, window.StartsAtUtc, window.EndsAtUtc);
        var request = new ArcadeCompetitionPaidEntryRequest(
            attemptId,
            competition,
            authenticatedPlayerId,
            rulesOptions.EntryFeeCents,
            nowUtc);
        var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            request.AttemptId, competition, authenticatedPlayerId, request.EntryFeeCents, nowUtc));
        var seed = createSeed();
        if (seed == 0) throw new InvalidOperationException("The Flappy seed source returned zero.");
        return coordinator.StartFlappyPaidAttemptAsync(
            request,
            new FlappyPaidRunIdentity(FirestoreArcadeCompetitionPaidEntryCoordinator.FlappyRunId(attempt), seed),
            cancellationToken);
    }

    internal Task<FlappyPaidReplayCompletionResult> CompleteAttemptAsync(
        string runId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        FlappyReplay replay,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity("flappy", windowKind, window.StartsAtUtc, window.EndsAtUtc);
        return coordinator.CompleteFlappyReplayAsync(new FlappyPaidReplayCompletionRequest(
            runId, competition, authenticatedPlayerId, replay, nowUtc), cancellationToken);
    }

    private static uint CreateCryptographicNonZeroSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        uint seed;
        do { RandomNumberGenerator.Fill(bytes); seed = BitConverter.ToUInt32(bytes); } while (seed == 0);
        return seed;
    }
}

internal sealed record FlappyPaidRunIdentity(string RunId, uint Seed);
