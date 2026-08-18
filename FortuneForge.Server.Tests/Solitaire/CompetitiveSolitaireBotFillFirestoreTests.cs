using System.Text.Json;
using FortuneForge.Server.Cards.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class CompetitiveSolitaireBotFillFirestoreTests
{
    private static readonly DateTime Start = new(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SingleHuman_NoBotsBeforeExactBoundary_RefundsOnceWithoutRevenue()
    {
        var (database, store, suffix) = CreateStore();
        var user = $"solo-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);

        var joins = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.JoinAsync(
            user,
            "Alice",
            4,
            500,
            3,
            "single-human-join-0001",
            1001,
            Start,
            default)));
        var started = Assert.IsType<SolitaireMatchSessionResponse>(joins[0].Session);
        Assert.All(joins, result => Assert.Equal(started.MatchId,
            Assert.IsType<SolitaireMatchSessionResponse>(result.Session).MatchId));
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));
        Assert.Single(await LedgerAsync(database));
        Assert.Equal(3, started.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.Single(await StoredPlayersAsync(database, started.MatchId));

        var reconnect = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddSeconds(30), default)).Session);
        Assert.Equal(started.Version, reconnect.Version);
        Assert.Equal(JsonSerializer.Serialize(started.Game), JsonSerializer.Serialize(reconnect.Game));
        AssertAutomationNeutral(reconnect);

        var completedAt = Start.AddMinutes(1);
        var finished = Assert.IsType<SolitaireMatchSessionResponse>((await store.ForfeitAsync(
            user,
            started.MatchId,
            started.Version,
            "single-human-forfeit-01",
            completedAt,
            default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Forfeited,
            Assert.Single(finished.Players, player => player.IsCurrentPlayer).Status);
        Assert.Single(await StoredPlayersAsync(database, started.MatchId));

        var beforeFill = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(
                user,
                completedAt.AddMinutes(2).AddSeconds(59),
                default)).Session);
        Assert.Equal(3, beforeFill.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.Single(await StoredPlayersAsync(database, started.MatchId));

        var fillAt = completedAt.AddMinutes(3);
        var fills = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            store.GetSessionAsync(user, fillAt, default)));
        var result = Assert.IsType<SolitaireResultSessionResponse>(fills[0].Session);
        Assert.All(fills, value => Assert.IsType<SolitaireResultSessionResponse>(value.Session));
        Assert.Equal((5m, 5m, 0m),
            (result.PrizePoolCredits, result.WinnerPayoutCredits, result.PlatformFeeCredits));
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));
        AssertAutomationNeutral(result);

        var storedPlayers = await StoredPlayersAsync(database, started.MatchId);
        Assert.Equal(4, storedPlayers.Count);
        var synthetics = storedPlayers.Where(snapshot => Field<bool>(snapshot, "isSynthetic")).ToArray();
        Assert.Equal(3, synthetics.Length);
        Assert.Equal([2L, 3L, 4L], synthetics
            .Select(snapshot => Field<long>(snapshot, "syntheticSkill"))
            .Order()
            .ToArray());
        var ledger = await LedgerAsync(database);
        Assert.Single(ledger);
        Assert.Single(ledger, entry => Field<string>(entry, "type") == "solitaire-buyin");
        Assert.Empty((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("solitaireTestMatchTrace").GetSnapshotAsync()).Documents);

        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ClaimAsync(
            user,
            started.MatchId,
            "single-human-claim-001",
            fillAt.AddSeconds(1),
            default)));
        Assert.All(claims, value => Assert.IsType<SolitaireIdleSessionResponse>(value.Session));
        Assert.Equal(10_000, await ReadBalanceAsync(database, user));
        ledger = await LedgerAsync(database);
        Assert.Equal(2, ledger.Count);
        Assert.Single(ledger, entry => Field<string>(entry, "type") == "solitaire-test-refund-claim");
    }

    [Fact]
    public async Task CompletedHumanCanLeaveWhileSharedMatchKeepsExactLateJoinWindow()
    {
        var (database, store, suffix) = CreateStore();
        var user = $"restart-{suffix}";
        var lateUser = $"restart-late-{suffix}";
        await Task.WhenAll(
            SeedBalanceAsync(database, user, 10_000),
            SeedBalanceAsync(database, lateUser, 10_000));

        var first = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            user, "Alice", 4, 500, 3, "restart-first-join-01", 7117, Start, default)).Session);
        _ = await store.ForfeitAsync(
            user,
            first.MatchId,
            first.Version,
            "restart-first-forfeit",
            Start.AddMinutes(1),
            default);

        var dismissedAt = Start.AddMinutes(1).AddSeconds(1);
        var idle = await store.DismissAsync(
            user,
            first.MatchId,
            "restart-first-dismiss1",
            dismissedAt,
            default);
        Assert.IsType<SolitaireIdleSessionResponse>(idle.Session);
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));
        Assert.Single(await StoredPlayersAsync(database, first.MatchId));
        Assert.Empty((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);

        var late = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            lateUser,
            "Bruno",
            4,
            500,
            3,
            "restart-late-join-001",
            8118,
            Start.AddMinutes(3).AddSeconds(59),
            default)).Session);
        Assert.Equal(first.MatchId, late.MatchId);
        Assert.Equal(2, (await StoredPlayersAsync(database, first.MatchId)).Count);
        Assert.Equal(9_500, await ReadBalanceAsync(database, lateUser));

        var replay = await store.DismissAsync(
            user,
            first.MatchId,
            "restart-first-dismiss1",
            dismissedAt.AddSeconds(2),
            default);
        Assert.IsType<SolitaireIdleSessionResponse>(replay.Session);
    }

    [Fact]
    public async Task CompletedHumanCanStartAnotherMatchBeforePriorFillWindowCloses()
    {
        var (database, store, suffix) = CreateStore();
        var user = $"restart-self-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);

        var first = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            user, "Alice", 4, 500, 3, "restart-self-first-join", 9119, Start, default)).Session);
        _ = await store.ForfeitAsync(
            user,
            first.MatchId,
            first.Version,
            "restart-self-forfeit1",
            Start.AddMinutes(1),
            default);
        _ = await store.DismissAsync(
            user,
            first.MatchId,
            "restart-self-dismiss01",
            Start.AddMinutes(1).AddSeconds(1),
            default);

        var second = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            user,
            "Alice",
            4,
            500,
            3,
            "restart-self-secondjoin",
            9120,
            Start.AddMinutes(1).AddSeconds(2),
            default)).Session);

        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Equal(9_000, await ReadBalanceAsync(database, user));
        Assert.Single(await StoredPlayersAsync(database, first.MatchId));
        Assert.Single(await StoredPlayersAsync(database, second.MatchId));
    }

    [Fact]
    public async Task LateHuman_ClaimsOpenSeatBeforeBoundary_GetsOwnDeadlineAndNormalHumanPool()
    {
        var (database, store, suffix) = CreateStore();
        var firstUser = $"first-{suffix}";
        var lateUser = $"late-{suffix}";
        await Task.WhenAll(
            SeedBalanceAsync(database, firstUser, 10_000),
            SeedBalanceAsync(database, lateUser, 10_000));

        var first = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            firstUser, "Alice", 4, 500, 3, "late-first-join-0001", 2002, Start, default)).Session);
        var firstGame = JsonSerializer.Serialize(first.Game);

        var firstDeadline = Start.AddMinutes(10);
        var expired = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(firstUser, firstDeadline, default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Finished,
            Assert.Single(expired.Players, player => player.IsCurrentPlayer).Status);
        Assert.Single(await StoredPlayersAsync(database, first.MatchId));

        var lateJoinAt = firstDeadline.AddMinutes(2).AddSeconds(59);
        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.JoinAsync(
            lateUser,
            "Bruno",
            4,
            500,
            3,
            "late-second-join-001",
            9999,
            lateJoinAt,
            default)));
        var late = Assert.IsType<SolitaireMatchSessionResponse>(claims[0].Session);
        Assert.All(claims, value => Assert.Equal(first.MatchId,
            Assert.IsType<SolitaireMatchSessionResponse>(value.Session).MatchId));
        Assert.Equal(first.MatchId, late.MatchId);
        Assert.Equal(firstGame, JsonSerializer.Serialize(late.Game));
        Assert.Equal(lateJoinAt, late.StartedAtUtc);
        Assert.Equal(lateJoinAt.AddMinutes(10), late.DeadlineAtUtc);
        Assert.Equal(9_500, await ReadBalanceAsync(database, lateUser));
        Assert.Equal(2, (await LedgerAsync(database)).Count);

        var fillAt = firstDeadline.AddMinutes(3);
        var waitingForLateHuman = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(firstUser, fillAt, default)).Session);
        Assert.DoesNotContain(waitingForLateHuman.Players, player => player.Status == SolitairePlayerStatuses.Open);
        Assert.Equal(4, (await StoredPlayersAsync(database, first.MatchId)).Count);
        AssertAutomationNeutral(waitingForLateHuman);

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(lateUser, late.DeadlineAtUtc, default)).Session);
        Assert.Equal((10m, 9m, 1m),
            (result.PrizePoolCredits, result.WinnerPayoutCredits, result.PlatformFeeCredits));
        Assert.Equal(19_000,
            await ReadBalanceAsync(database, firstUser) + await ReadBalanceAsync(database, lateUser));
        var paid = Assert.Single(result.Standings, standing => standing.PayoutCredits > 0);
        Assert.True(paid.PlayerId == firstUser || paid.PlayerId == lateUser);
        AssertAutomationNeutral(result);

        var ledger = await LedgerAsync(database);
        Assert.Equal(2, ledger.Count);
        Assert.Equal(2, ledger.Count(entry => Field<string>(entry, "type") == "solitaire-buyin"));
        _ = await store.ClaimAsync(
            firstUser, first.MatchId, "late-first-claim-0001", late.DeadlineAtUtc.AddSeconds(1), default);
        _ = await store.ClaimAsync(
            lateUser, first.MatchId, "late-second-claim-001", late.DeadlineAtUtc.AddSeconds(1), default);
        Assert.Equal(19_900,
            await ReadBalanceAsync(database, firstUser) + await ReadBalanceAsync(database, lateUser));
        ledger = await LedgerAsync(database);
        Assert.Equal(3, ledger.Count);
        var payout = Assert.Single(ledger,
            entry => Field<string>(entry, "type") == "solitaire-winner-payout-claim");
        Assert.DoesNotContain("internal", Field<string>(payout, "userId"), StringComparison.OrdinalIgnoreCase);
        var revenue = Assert.Single(
            (await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(1_000, Field<long>(revenue, "grossPoolCents"));
        Assert.Equal(900, Field<long>(revenue, "winnerPayoutCents"));
        Assert.Equal(100, Field<long>(revenue, "platformFeeCents"));
        Assert.Equal("slotsCredits", Field<string>(revenue, "currencyId"));
        Assert.Equal("real-human-pool-v1", Field<string>(revenue, "financialClassification"));
        Assert.Equal(0, Field<long>(revenue, "botFinancialContributionCents"));
        Assert.Equal(2, Field<long>(revenue, "humanPlayerCount"));
        Assert.Equal(
            Field<long>(revenue, "grossPoolCents") - Field<long>(revenue, "winnerPayoutCents"),
            Field<long>(revenue, "platformFeeCents"));
        Assert.Equal(Timestamp.FromDateTime(late.DeadlineAtUtc), Field<Timestamp>(revenue, "recognizedAt"));
    }

    [Fact]
    public async Task PauseBudget_IsCumulativeAndExtendsOnlyThePlayersPlayDeadline()
    {
        var (database, store, suffix) = CreateStore();
        var user = $"pause-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);
        var started = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            user, "Alice", 4, 500, 3, "pause-join-command01", 5050, Start, default)).Session);

        var paused = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            user,
            started.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.Pause, started.Version, null, null, null, null),
            "pause-first-command1",
            Start.AddMinutes(1),
            default)).Session);
        Assert.True(paused.IsPaused);
        Assert.Equal(9 * 60_000, paused.RemainingMilliseconds);
        Assert.Equal(10 * 60_000, paused.PauseRemainingMilliseconds);

        var stillPaused = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(5), default)).Session);
        Assert.True(stillPaused.IsPaused);
        Assert.Equal(9 * 60_000, stillPaused.RemainingMilliseconds);
        Assert.Equal(6 * 60_000, stillPaused.PauseRemainingMilliseconds);

        var resumed = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            user,
            started.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.Resume, stillPaused.Version, null, null, null, null),
            "pause-first-resume01",
            Start.AddMinutes(5),
            default)).Session);
        Assert.False(resumed.IsPaused);
        Assert.Equal(Start.AddMinutes(14), resumed.DeadlineAtUtc);

        var pausedAgain = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            user,
            started.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.Pause, resumed.Version, null, null, null, null),
            "pause-second-command",
            Start.AddMinutes(6),
            default)).Session);
        Assert.True(pausedAgain.IsPaused);

        var exhausted = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(12), default)).Session);
        Assert.False(exhausted.IsPaused);
        Assert.Equal(0, exhausted.PauseRemainingMilliseconds);
        Assert.Equal(Start.AddMinutes(20), exhausted.DeadlineAtUtc);
        Assert.Equal(8 * 60_000, exhausted.RemainingMilliseconds);

        var beforeDeadline = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(19), default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Playing,
            Assert.Single(beforeDeadline.Players, player => player.IsCurrentPlayer).Status);
        Assert.Equal(60_000, beforeDeadline.RemainingMilliseconds);
        var atDeadline = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(20), default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Finished,
            Assert.Single(atDeadline.Players, player => player.IsCurrentPlayer).Status);
    }

    [Fact]
    public async Task SubmitAndIntegrityRollback_WriteResultsAndPersistAcknowledgedWarning()
    {
        var (database, store, suffix) = CreateStore();
        var submittedUser = $"submit-{suffix}";
        await SeedBalanceAsync(database, submittedUser, 10_000);
        var started = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            submittedUser, "Alice", 4, 500, 1, "submit-join-command", 6060, Start, default)).Session);

        var submitted = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            submittedUser,
            started.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.Submit, started.Version, null, null, null, null),
            "submit-game-command",
            Start.AddMinutes(1),
            default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Finished,
            Assert.Single(submitted.Players, player => player.IsCurrentPlayer).Status);
        var pendingResult = Assert.Single(await CardGameResultsAsync(database, started.MatchId));
        Assert.False(string.IsNullOrWhiteSpace(Field<string>(pendingResult, "resultId")));
        Assert.Equal("solitaire", Field<string>(pendingResult, "game"));
        Assert.Equal("competitive", Field<string>(pendingResult, "mode"));
        Assert.Equal(started.MatchId, Field<string>(pendingResult, "matchId"));
        Assert.Equal(submittedUser, Field<string>(pendingResult, "userId"));
        Assert.Equal(SolitaireClaimStatuses.Unclaimed, Field<string>(pendingResult, "claimStatus"));
        Assert.Equal("pending", Field<string>(pendingResult, "settlementStatus"));
        Assert.Equal("slotsCredits", Field<string>(pendingResult, "currencyId"));
        Assert.IsType<Timestamp>(pendingResult.ToDictionary()["completedAt"]);
        Assert.Equal(1, Field<long>(pendingResult, "schemaVersion"));
        Assert.Equal(9_500, await ReadBalanceAsync(database, submittedUser));

        var settled = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(submittedUser, Start.AddMinutes(4), default)).Session);
        Assert.True(settled.CanClaim);
        Assert.Equal(9_500, await ReadBalanceAsync(database, submittedUser));
        var claimable = Assert.Single(await CardGameResultsAsync(database, started.MatchId));
        Assert.Equal("claimable", Field<string>(claimable, "settlementStatus"));
        Assert.Equal(500, Field<long>(claimable, "payoutCents"));

        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ClaimAsync(
            submittedUser,
            started.MatchId,
            "submit-result-claim1",
            Start.AddMinutes(4).AddSeconds(1),
            default)));
        Assert.All(claims, claim => Assert.IsType<SolitaireIdleSessionResponse>(claim.Session));
        Assert.Equal(10_000, await ReadBalanceAsync(database, submittedUser));
        Assert.Equal(SolitaireClaimStatuses.Completed,
            Field<string>(Assert.Single(await CardGameResultsAsync(database, started.MatchId)), "claimStatus"));

        var integrityUser = $"integrity-{suffix}";
        await SeedBalanceAsync(database, integrityUser, 10_000);
        var integrityStarted = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            integrityUser,
            "Bruno",
            4,
            500,
            3,
            "integrity-join-0001",
            7070,
            Start.AddMinutes(5),
            default)).Session);
        var warned = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            integrityUser,
            integrityStarted.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.Move,
                integrityStarted.Version,
                new SolitairePileReference("waste", 0),
                0,
                new SolitairePileReference("foundation", 0),
                null),
            "integrity-bad-move1",
            Start.AddMinutes(5).AddSeconds(1),
            default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Playing,
            Assert.Single(warned.Players, player => player.IsCurrentPlayer).Status);
        Assert.Equal(integrityStarted.Game.Score, warned.Game.Score);
        Assert.Equal(integrityStarted.Game.Moves, warned.Game.Moves);
        Assert.Equal(
            JsonSerializer.Serialize(integrityStarted.Game with { Message = string.Empty }),
            JsonSerializer.Serialize(warned.Game with { Message = string.Empty }));
        Assert.Equal(integrityStarted.Version + 1, warned.Version);
        Assert.False(warned.IntegrityWarning?.Acknowledged);
        Assert.Contains("last verified board", warned.IntegrityWarning?.Reason);
        Assert.Contains("fair competitive play", warned.IntegrityWarning?.Purpose);
        Assert.Empty(await CardGameResultsAsync(database, integrityStarted.MatchId));

        var reconnected = Assert.IsType<SolitaireMatchSessionResponse>((await store.GetSessionAsync(
            integrityUser,
            Start.AddMinutes(5).AddSeconds(2),
            default)).Session);
        Assert.Equal(warned.IntegrityWarning, reconnected.IntegrityWarning);
        var acknowledged = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            integrityUser,
            integrityStarted.MatchId,
            new SolitaireCommandRequest(
                SolitaireCommandTypes.AcknowledgeWarning,
                reconnected.Version,
                null,
                null,
                null,
                null),
            "integrity-acknowledge1",
            Start.AddMinutes(5).AddSeconds(3),
            default)).Session);
        Assert.True(acknowledged.IntegrityWarning?.Acknowledged);
        Assert.Equal(SolitairePlayerStatuses.Playing,
            Assert.Single(acknowledged.Players, player => player.IsCurrentPlayer).Status);
    }

    [Fact]
    public async Task ExactBoundaryRace_ClosesOldSeatsAndStartsNewMatchForNewcomer()
    {
        var (database, store, suffix) = CreateStore();
        var firstUser = $"boundary-first-{suffix}";
        var newcomer = $"boundary-next-{suffix}";
        await Task.WhenAll(
            SeedBalanceAsync(database, firstUser, 10_000),
            SeedBalanceAsync(database, newcomer, 10_000));
        var first = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            firstUser, "Alice", 4, 500, 3, "boundary-first-join01", 3003, Start, default)).Session);
        _ = await store.ForfeitAsync(
            firstUser,
            first.MatchId,
            first.Version,
            "boundary-first-fold01",
            Start.AddMinutes(1),
            default);

        var boundary = Start.AddMinutes(4);
        var advance = store.GetSessionAsync(firstUser, boundary, default);
        var join = store.JoinAsync(
            newcomer,
            "Casey",
            4,
            500,
            3,
            "boundary-next-join001",
            4004,
            boundary,
            default);
        await Task.WhenAll(advance, join);

        var oldResult = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(firstUser, boundary, default)).Session);
        var newMatch = Assert.IsType<SolitaireMatchSessionResponse>((await join).Session);
        Assert.NotEqual(oldResult.MatchId, newMatch.MatchId);
        Assert.Equal(3, newMatch.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.Single((await StoredPlayersAsync(database, newMatch.MatchId)));
        Assert.Equal(9_500, await ReadBalanceAsync(database, newcomer));
    }

    private static (FirestoreDb Database, FirestoreCompetitiveSolitaireStore Store, string Suffix) CreateStore()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-{suffix}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        return (database, new FirestoreCompetitiveSolitaireStore(database, new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = true
        }), suffix);
    }

    private static Task SeedBalanceAsync(FirestoreDb database, string userId, long cents) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(
            new Dictionary<string, object>
            {
                ["available"] = cents / 100,
                ["availableFractionalCents"] = cents % 100,
                ["version"] = 1L,
                ["updatedAt"] = Timestamp.FromDateTime(Start)
            });

    private static async Task<long> ReadBalanceAsync(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances")
            .Document($"{userId}_slotsCredits")
            .GetSnapshotAsync();
        return checked(
            Field<long>(snapshot, "available") * 100 +
            Field<long>(snapshot, "availableFractionalCents"));
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> LedgerAsync(FirestoreDb database) =>
        (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> StoredPlayersAsync(
        FirestoreDb database,
        string matchId) => (await database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("matchId", matchId)
            .GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> CardGameResultsAsync(
        FirestoreDb database,
        string matchId) => (await database.Collection("cardGameResults")
            .WhereEqualTo("matchId", matchId)
            .GetSnapshotAsync()).Documents;

    private static T Field<T>(DocumentSnapshot snapshot, string field) => snapshot.GetValue<T>(field);

    private static void AssertAutomationNeutral(SolitaireSessionResponse session)
    {
        var json = JsonSerializer.Serialize(
            session,
            session.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("isBot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skill", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("difficulty", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__solitaire_internal__", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dealSeed", json, StringComparison.OrdinalIgnoreCase);
    }
}
