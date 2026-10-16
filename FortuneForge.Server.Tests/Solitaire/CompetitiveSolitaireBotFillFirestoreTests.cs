using System.Text.Json;
using FortuneForge.Server.Bots;
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
    public async Task FiveSecondLobby_ReusesProfiles_HoldsTheirLeasesUntilSettlement_AndRefundsSoloHumanOnce()
    {
        var (database, store) = CreateStore();
        const string user = "solo-human";
        await SeedBalanceAsync(database, user);
        var profiles = await SeedProfilesAsync(database, 3);

        var joins = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => JoinAsync(store, user, Start)));
        var queued = Assert.IsType<SolitaireQueueSessionResponse>(joins[0].Session);
        Assert.All(joins, value => Assert.Equal(queued.TicketId,
            Assert.IsType<SolitaireQueueSessionResponse>(value.Session).TicketId));
        Assert.Single(queued.Players);
        Assert.Equal((5m, 5m), (queued.PrizePoolCredits, queued.WinnerPayoutCredits));
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));
        Assert.Single(await LedgerAsync(database));
        Assert.Empty((await database.Collection("solitaireMatchPlayers").GetSnapshotAsync()).Documents);

        Assert.IsType<SolitaireQueueSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMilliseconds(4_999), default)).Session);
        Assert.Empty(await AssignmentsAsync(database));

        var deals = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            store.GetSessionAsync(user, Start.AddSeconds(5), default)));
        var started = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddSeconds(5), default)).Session);
        Assert.All(deals, value => AssertQueueOrMatch(value.Session, queued.TicketId, started.MatchId));
        Assert.Equal(Start.AddSeconds(5), started.StartedAtUtc);
        Assert.Equal(started.StartedAtUtc.AddMinutes(10), started.DeadlineAtUtc);
        Assert.Equal(4, started.Players.Count);
        Assert.All(started.Players, player => Assert.Equal(SolitairePlayerStatuses.Playing, player.Status));
        Assert.All(profiles, profile => Assert.Contains(started.Players,
            player => player.DisplayName == profile.PlayerName));
        AssertAutomationNeutral(started);
        var stored = await StoredPlayersAsync(database, started.MatchId);
        Assert.Equal(profiles.Select(profile => profile.UserId).Order(), stored
            .Where(player => Field<bool>(player, "isSynthetic"))
            .Select(player => Field<string>(player, "userId")).Order());
        Assert.Empty(await CardGameResultsAsync(database, started.MatchId));
        Assert.Equal(3, (await AssignmentsAsync(database)).Count);
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.CancelAsync(
            user, queued.TicketId, "cancel-after-deal-key", Start.AddSeconds(6), default));
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));

        var reconnect = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddSeconds(30), default)).Session);
        Assert.Equal(started.Version, reconnect.Version);
        Assert.Equal(JsonSerializer.Serialize(started.Game), JsonSerializer.Serialize(reconnect.Game));
        var finished = Assert.IsType<SolitaireMatchSessionResponse>((await store.ForfeitAsync(
            user, started.MatchId, started.Version, "solo-forfeit-command", Start.AddMinutes(1), default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Forfeited,
            Assert.Single(finished.Players, player => player.IsCurrentPlayer).Status);
        Assert.Contains(finished.Players, player => !player.IsCurrentPlayer && player.Status == SolitairePlayerStatuses.Playing);

        _ = await store.GetSessionAsync(user, Start.AddMinutes(2), default);
        Assert.All(await AssignmentsAsync(database), assignment =>
        {
            Assert.Equal(ManagedPlayerGames.Solitaire, Field<string>(assignment, "gameId"));
            Assert.Equal(Timestamp.FromDateTime(Start.AddMinutes(2)), Field<Timestamp>(assignment, "heartbeatAt"));
        });

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(8), default)).Session);
        Assert.Equal((5m, 5m, 0m),
            (result.PrizePoolCredits, result.WinnerPayoutCredits, result.PlatformFeeCredits));
        Assert.Equal(9_500, await ReadBalanceAsync(database, user));
        Assert.Empty(await AssignmentsAsync(database));
        Assert.Empty((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("solitaireTestMatchTrace").GetSnapshotAsync()).Documents);
        AssertAutomationNeutral(result);
        Assert.Equal(3, (await CardGameResultsAsync(database, started.MatchId)).Count(snapshot =>
            snapshot.ContainsField("financialClassification") &&
            Field<string>(snapshot, "financialClassification") == "managed-player-virtual-v1"));

        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ClaimAsync(
            user, started.MatchId, "solo-result-claim-key", Start.AddMinutes(8).AddSeconds(1), default)));
        Assert.All(claims, claim => Assert.IsType<SolitaireIdleSessionResponse>(claim.Session));
        Assert.Equal(10_000, await ReadBalanceAsync(database, user));
        var ledger = await LedgerAsync(database);
        Assert.Equal(2, ledger.Count);
        Assert.Single(ledger, entry => Field<string>(entry, "type") == "solitaire-test-refund-claim");
        Assert.All(ledger, entry => Assert.Equal(user, Field<string>(entry, "userId")));
        foreach (var profile in profiles) Assert.Equal(0, await ReadBalanceAsync(database, profile.UserId));

        // Reconnect/new matches keep the same persisted identities after their normal break.
        var nextTime = Start.AddMinutes(50);
        _ = await JoinAsync(store, user, nextTime, "solo-second-join-key");
        var next = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, nextTime.AddSeconds(5), default)).Session);
        Assert.NotEqual(started.MatchId, next.MatchId);
        Assert.All(profiles, profile => Assert.Contains(next.Players,
            player => player.DisplayName == profile.PlayerName));
        Assert.Equal(3, (await database.Collection("users").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task HumanJoiningDuringGraceGetsPriority_AndOnlyHumanBuyInsFundThePrize()
    {
        var (database, store) = CreateStore();
        await Task.WhenAll(SeedBalanceAsync(database, "alice"), SeedBalanceAsync(database, "bruno"));
        _ = await SeedProfilesAsync(database, 3);
        Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, "alice", Start)).Session);
        Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, "bruno", Start.AddSeconds(4))).Session);
        var before = Assert.IsType<SolitaireQueueSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMilliseconds(4_999), default)).Session);
        Assert.Equal(2, before.Players.Count);
        Assert.Equal((10m, 9m), (before.PrizePoolCredits, before.WinnerPayoutCredits));

        var first = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddSeconds(5), default)).Session);
        var second = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("bruno", Start.AddSeconds(5), default)).Session);
        Assert.Equal(first.MatchId, second.MatchId);
        Assert.Equal(first.StartedAtUtc, second.StartedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(first.Game), JsonSerializer.Serialize(second.Game));
        Assert.Equal((10m, 9m), (first.PrizePoolCredits, first.WinnerPayoutCredits));
        Assert.Equal(2, (await StoredPlayersAsync(database, first.MatchId)).Count(player => Field<bool>(player, "isSynthetic")));

        _ = await store.ForfeitAsync("alice", first.MatchId, first.Version, "alice-forfeit-key", Start.AddMinutes(1), default);
        _ = await store.ForfeitAsync("bruno", first.MatchId, second.Version, "bruno-forfeit-key", Start.AddMinutes(1), default);
        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMinutes(8), default)).Session);
        Assert.Equal((10m, 9m, 1m),
            (result.PrizePoolCredits, result.WinnerPayoutCredits, result.PlatformFeeCredits));
        var paid = Assert.Single(result.Standings, standing => standing.PayoutCredits > 0);
        Assert.Contains(paid.PlayerId, new[] { "alice", "bruno" });
        Assert.Equal(2, (await LedgerAsync(database)).Count);
        _ = await store.ClaimAsync("alice", first.MatchId, "alice-result-claim-key", Start.AddMinutes(8).AddSeconds(1), default);
        _ = await store.ClaimAsync("bruno", first.MatchId, "bruno-result-claim-key", Start.AddMinutes(8).AddSeconds(1), default);
        Assert.Equal(19_900, await ReadBalanceAsync(database, "alice") + await ReadBalanceAsync(database, "bruno"));
        Assert.Equal(3, (await LedgerAsync(database)).Count);
        var revenue = Assert.Single((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(1_000, Field<long>(revenue, "grossPoolCents"));
        Assert.Equal(900, Field<long>(revenue, "winnerPayoutCents"));
        Assert.Equal(100, Field<long>(revenue, "platformFeeCents"));
        Assert.Equal(0, Field<long>(revenue, "botFinancialContributionCents"));
        Assert.Equal(2, Field<long>(revenue, "humanPlayerCount"));
        Assert.Equal("real-human-pool-v1", Field<string>(revenue, "financialClassification"));
    }

    [Fact]
    public async Task FullHumanLobbyUsesTheSameGraceWithoutGeneratingOrReservingProfiles()
    {
        var (database, store) = CreateStore();
        var users = new[] { "alice", "bruno", "casey", "daria" };
        await Task.WhenAll(users.Select(user => SeedBalanceAsync(database, user)));
        for (var index = 0; index < 4; index++)
            Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, users[index], Start.AddSeconds(index))).Session);
        var match = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(users[3], Start.AddSeconds(5), default)).Session);
        Assert.Equal(Start.AddSeconds(5), match.StartedAtUtc);
        Assert.Equal(4, match.Players.Count);
        Assert.All(await StoredPlayersAsync(database, match.MatchId), player => Assert.False(Field<bool>(player, "isSynthetic")));
        Assert.Empty(await AssignmentsAsync(database));
        Assert.Empty((await database.Collection("users").GetSnapshotAsync()).Documents);

        var matchReference = database.Collection("solitaireMatches").Document(match.MatchId);
        await matchReference.UpdateAsync("botsFilled", FieldValue.Delete);
        for (var index = 0; index < users.Length; index++)
            _ = await store.ForfeitAsync(users[index], match.MatchId, 1,
                $"full-human-forfeit-{index}", Start.AddMinutes(1).AddSeconds(index), default);
        Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(users[0], Start.AddMinutes(2), default)).Session);
        var rewritten = await matchReference.GetSnapshotAsync();
        Assert.True(Field<bool>(rewritten, "botsFilled"));
        Assert.Equal("settled", Field<string>(rewritten, "status"));
    }

    [Fact]
    public async Task AllHumanOptOutWaitsPastGrace_AndQueueCancellationRefundsExactlyOnce()
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        await SeedBalanceAsync(database, "alice");
        var queued = Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, "alice", Start)).Session);
        Assert.IsType<SolitaireQueueSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMinutes(10), default)).Session);
        var cancellations = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.CancelAsync(
            "alice", queued.TicketId, "cancel-queued-solitaire", Start.AddMinutes(10), default)));
        Assert.All(cancellations, value => Assert.IsType<SolitaireIdleSessionResponse>(value.Session));
        Assert.Equal(10_000, await ReadBalanceAsync(database, "alice"));
        var ledger = await LedgerAsync(database);
        Assert.Equal(2, ledger.Count);
        Assert.Single(ledger, entry => Field<string>(entry, "type") == "solitaire-refund");
        Assert.Empty((await database.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        Assert.Empty(await AssignmentsAsync(database));
    }

    [Fact]
    public async Task ConcurrentServerInstancesFillOnce_AndRespectAnotherGamesProfileLease()
    {
        var (database, store) = CreateStore();
        var otherStore = new FirestoreCompetitiveSolitaireStore(database,
            new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = true });
        await SeedBalanceAsync(database, "alice");
        var profiles = await SeedProfilesAsync(database, 4);
        var assignments = new FirestoreManagedPlayerAssignmentStore(database);
        Assert.True(await assignments.TryReserveAsync(profiles[0].UserId,
            ManagedPlayerGames.Blackjack, "blackjack-still-playing", Start, default));
        var queued = Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, "alice", Start)).Session);

        var filled = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            (index % 2 == 0 ? store : otherStore).GetSessionAsync("alice", Start.AddSeconds(5), default)));
        var match = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddSeconds(5), default)).Session);
        Assert.All(filled, value => AssertQueueOrMatch(value.Session, queued.TicketId, match.MatchId));
        Assert.Equal(4, (await StoredPlayersAsync(database, match.MatchId)).Count);
        Assert.Single((await database.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        Assert.DoesNotContain(match.Players, player => player.DisplayName == profiles[0].PlayerName);
        var leases = await AssignmentsAsync(database);
        Assert.Equal(4, leases.Count);
        Assert.Equal("blackjack-still-playing", Field<string>(Assert.Single(leases,
            lease => lease.Id == profiles[0].UserId), "assignmentId"));
        Assert.Equal(3, leases.Count(lease => Field<string>(lease, "gameId") == ManagedPlayerGames.Solitaire));
        Assert.Single(await LedgerAsync(database));
    }

    [Fact]
    public async Task PauseBudgetRemainsCumulativeAndOnlyExtendsTheHumansDeadline()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        var started = await JoinStartedAsync(store, "alice", Start);
        var matchStart = started.StartedAtUtc;
        var paused = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("alice", started.MatchId,
            Command(SolitaireCommandTypes.Pause, started.Version), "pause-first-command", matchStart.AddMinutes(1), default)).Session);
        Assert.True(paused.IsPaused);
        Assert.Equal(9 * 60_000, paused.RemainingMilliseconds);
        Assert.Equal(10 * 60_000, paused.PauseRemainingMilliseconds);
        var stillPaused = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", matchStart.AddMinutes(5), default)).Session);
        Assert.Equal(9 * 60_000, stillPaused.RemainingMilliseconds);
        Assert.Equal(6 * 60_000, stillPaused.PauseRemainingMilliseconds);
        var resumed = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("alice", started.MatchId,
            Command(SolitaireCommandTypes.Resume, stillPaused.Version), "pause-resume-command", matchStart.AddMinutes(5), default)).Session);
        Assert.False(resumed.IsPaused);
        Assert.Equal(matchStart.AddMinutes(14), resumed.DeadlineAtUtc);
        _ = await store.CommandAsync("alice", started.MatchId, Command(SolitaireCommandTypes.Pause, resumed.Version),
            "pause-second-command", matchStart.AddMinutes(6), default);
        var exhausted = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", matchStart.AddMinutes(12), default)).Session);
        Assert.False(exhausted.IsPaused);
        Assert.Equal(0, exhausted.PauseRemainingMilliseconds);
        Assert.Equal(matchStart.AddMinutes(20), exhausted.DeadlineAtUtc);
        Assert.Equal(8 * 60_000, exhausted.RemainingMilliseconds);
        Assert.Equal(3, (await AssignmentsAsync(database)).Count);
        var before = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", matchStart.AddMinutes(20).AddMilliseconds(-1), default)).Session);
        Assert.Equal(1, before.RemainingMilliseconds);
        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("alice", matchStart.AddMinutes(20), default)).Session);
        Assert.Equal(SolitairePlayerStatuses.Finished,
            Assert.Single(result.Standings, player => player.IsCurrentPlayer).Status);
    }

    [Fact]
    public async Task WorkerStartsAndSettlesAQueuedMatchAfterTheHumanLeavesTheWaitingScreen()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        _ = await SeedProfilesAsync(database, 3);
        Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, "alice", Start)).Session);

        await store.SweepAsync(Start.AddMilliseconds(4_999), default);
        Assert.Empty((await database.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        await store.SweepAsync(Start.AddSeconds(5), default);
        var durableMatch = Assert.Single((await database.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        Assert.Equal("playing", Field<string>(durableMatch, "status"));
        Assert.Equal(4, (await StoredPlayersAsync(database, durableMatch.Id)).Count);
        var match = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddSeconds(6), default)).Session);
        _ = await store.ForfeitAsync("alice", match.MatchId, match.Version,
            "worker-human-forfeit", Start.AddMinutes(1), default);
        var dismissed = await store.DismissAsync("alice", match.MatchId,
            "worker-human-dismiss", Start.AddMinutes(1).AddSeconds(1), default);
        Assert.IsType<SolitaireIdleSessionResponse>(dismissed.Session);

        await store.SweepAsync(Start.AddMinutes(8), default);

        Assert.Equal("settled", Field<string>(await durableMatch.Reference.GetSnapshotAsync(), "status"));
        Assert.Empty(await AssignmentsAsync(database));
        Assert.IsType<SolitaireIdleSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMinutes(8), default)).Session);
        var claimable = Assert.Single(await CardGameResultsAsync(database, match.MatchId),
            value => Field<string>(value, "userId") == "alice");
        Assert.Equal("claimable", Field<string>(claimable, "settlementStatus"));
        _ = await store.ClaimAsync("alice", match.MatchId, "worker-human-result-claim", Start.AddMinutes(8), default);
        Assert.Equal(10_000, await ReadBalanceAsync(database, "alice"));
    }

    [Fact]
    public async Task LegacyPlayerWithoutTimingFieldsUsesMatchClockAndRewritesCurrentSchema()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        var started = await JoinStartedAsync(store, "alice", Start);
        var player = Assert.Single(await StoredPlayersAsync(database, started.MatchId),
            value => !Field<bool>(value, "isSynthetic"));
        await player.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["startedAt"] = FieldValue.Delete,
            ["deadlineAt"] = FieldValue.Delete
        });
        var paused = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("alice", started.MatchId,
            Command(SolitaireCommandTypes.Pause, started.Version), "legacy-pause-command", started.StartedAtUtc.AddMinutes(1), default)).Session);
        Assert.Equal(started.StartedAtUtc, paused.StartedAtUtc);
        Assert.Equal(started.DeadlineAtUtc, paused.DeadlineAtUtc);
        player = await player.Reference.GetSnapshotAsync();
        Assert.Equal(Timestamp.FromDateTime(started.StartedAtUtc), Field<Timestamp>(player, "startedAt"));
        Assert.Equal(Timestamp.FromDateTime(started.DeadlineAtUtc), Field<Timestamp>(player, "deadlineAt"));
        var resumed = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("alice", started.MatchId,
            Command(SolitaireCommandTypes.Resume, paused.Version), "legacy-resume-command", started.StartedAtUtc.AddMinutes(2), default)).Session);
        _ = await store.CommandAsync("alice", started.MatchId, Command(SolitaireCommandTypes.Submit, resumed.Version),
            "legacy-submit-command", started.StartedAtUtc.AddMinutes(3), default);
        player = await player.Reference.GetSnapshotAsync();
        Assert.Equal(2 * 60_000, Field<long>(player, "elapsedMilliseconds"));
    }

    [Fact]
    public async Task LegacyMatchWithOpenSeatsCanFinishWithoutReopeningTheOldFabricatedOpponentFlow()
    {
        var (database, store) = CreateStore();
        const string user = "legacy-human";
        const string matchId = "legacy-underfilled-match";
        var startedAt = Timestamp.FromDateTime(Start);
        var deadline = Timestamp.FromDateTime(Start.AddMinutes(10));
        await database.Collection("userBalances").Document($"{user}_slotsCredits").SetAsync(
            new Dictionary<string, object> { ["available"] = 95L, ["availableFractionalCents"] = 0L });
        await database.Collection("solitairePlayerSessions")
            .Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey(user)).SetAsync(new Dictionary<string, object>
            {
                ["userId"] = user, ["kind"] = SolitaireSessionKinds.Match, ["matchId"] = matchId
            });
        await database.Collection("solitaireMatches").Document(matchId).SetAsync(new Dictionary<string, object>
        {
            ["matchId"] = matchId, ["playerCount"] = 4L, ["buyInCents"] = 500L,
            ["prizePoolCents"] = 500L, ["winnerPayoutCents"] = 500L, ["platformFeeCents"] = 0L,
            ["dealSeed"] = 1001L, ["startedAt"] = startedAt, ["deadlineAt"] = deadline,
            ["status"] = "playing", ["playerIds"] = new[] { user }, ["displayNames"] = new[] { user },
            ["ticketIds"] = new[] { "legacy-ticket" }, ["joinedAt"] = new[] { startedAt },
            ["botsFilled"] = false, ["drawCount"] = 3L
        });
        await database.Collection("solitaireMatchPlayers")
            .Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{matchId}\n{user}"))
            .SetAsync(new Dictionary<string, object>
            {
                ["matchId"] = matchId, ["userId"] = user, ["displayName"] = user,
                ["seat"] = 1L, ["status"] = SolitairePlayerStatuses.Playing,
                ["gameStateJson"] = JsonSerializer.Serialize(SolitaireEngine.CreateGame(1001, 3),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ["version"] = 1L, ["elapsedMilliseconds"] = -1L,
                ["payoutCents"] = 0L, ["acknowledged"] = false, ["isSynthetic"] = false
            });

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync(user, Start.AddMinutes(10), default)).Session);

        Assert.Single(result.Standings);
        Assert.True(result.CanClaim);
        Assert.Equal(5m, result.WinnerPayoutCredits);
        Assert.Empty(await AssignmentsAsync(database));
        Assert.Empty((await database.Collection("users").GetSnapshotAsync()).Documents);
        _ = await store.ClaimAsync(user, matchId, "legacy-open-seats-claim", Start.AddMinutes(10), default);
        Assert.Equal(10_000, await ReadBalanceAsync(database, user));
    }

    [Fact]
    public async Task SubmitAndIntegrityRollbackKeepClaimsAndVerifiedBoardWarnings()
    {
        var (database, store) = CreateStore();
        await Task.WhenAll(SeedBalanceAsync(database, "alice"), SeedBalanceAsync(database, "bruno"));
        var started = await JoinStartedAsync(store, "alice", Start);
        _ = await store.CommandAsync("alice", started.MatchId, Command(SolitaireCommandTypes.Submit, started.Version),
            "submit-human-command", Start.AddMinutes(1), default);
        var pending = Assert.Single(await CardGameResultsAsync(database, started.MatchId));
        Assert.Equal("pending", Field<string>(pending, "settlementStatus"));
        Assert.Equal(SolitaireClaimStatuses.Unclaimed, Field<string>(pending, "claimStatus"));
        var settled = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMinutes(8), default)).Session);
        Assert.True(settled.CanClaim);
        _ = await store.ClaimAsync("alice", started.MatchId, "submit-claim-command", Start.AddMinutes(8).AddSeconds(1), default);
        var claimed = Assert.Single(await CardGameResultsAsync(database, started.MatchId),
            value => Field<string>(value, "userId") == "alice");
        Assert.Equal(SolitaireClaimStatuses.Completed, Field<string>(claimed, "claimStatus"));
        Assert.Equal(10_000, await ReadBalanceAsync(database, "alice"));

        var integrityStart = await JoinStartedAsync(store, "bruno", Start.AddMinutes(8));
        var warned = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("bruno", integrityStart.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Move, integrityStart.Version,
                new SolitairePileReference("waste", 0), 0, new SolitairePileReference("foundation", 0), null),
            "integrity-bad-command", Start.AddMinutes(8).AddSeconds(6), default)).Session);
        Assert.Equal(integrityStart.Game.Score, warned.Game.Score);
        Assert.Equal(integrityStart.Game.Moves, warned.Game.Moves);
        Assert.Equal(JsonSerializer.Serialize(integrityStart.Game with { Message = string.Empty }),
            JsonSerializer.Serialize(warned.Game with { Message = string.Empty }));
        Assert.Equal(integrityStart.Version + 1, warned.Version);
        Assert.False(warned.IntegrityWarning?.Acknowledged);
        Assert.Contains("last verified board", warned.IntegrityWarning?.Reason);
        Assert.Contains("fair competitive play", warned.IntegrityWarning?.Purpose);
        var reconnect = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("bruno", Start.AddMinutes(8).AddSeconds(7), default)).Session);
        Assert.Equal(warned.IntegrityWarning, reconnect.IntegrityWarning);
        var acknowledged = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("bruno", integrityStart.MatchId,
            Command(SolitaireCommandTypes.AcknowledgeWarning, reconnect.Version), "integrity-ack-command",
            Start.AddMinutes(8).AddSeconds(8), default)).Session);
        Assert.True(acknowledged.IntegrityWarning?.Acknowledged);
    }

    private static (FirestoreDb Database, FirestoreCompetitiveSolitaireStore Store) CreateStore(bool allowManagedPlayers = true)
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        return (database, new FirestoreCompetitiveSolitaireStore(database, new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = allowManagedPlayers
        }));
    }

    private static Task<SolitaireStoreSession> JoinAsync(FirestoreCompetitiveSolitaireStore store, string user,
        DateTime now, string? key = null) => store.JoinAsync(user, user, 4, 500, 3,
            key ?? $"join-command-{user}", 1001, now, default);

    private static async Task<SolitaireMatchSessionResponse> JoinStartedAsync(
        FirestoreCompetitiveSolitaireStore store, string user, DateTime now)
    {
        Assert.IsType<SolitaireQueueSessionResponse>((await JoinAsync(store, user, now)).Session);
        return Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync(user, now.AddSeconds(5), default)).Session);
    }

    private static SolitaireCommandRequest Command(string type, int version) => new(type, version, null, null, null, null);

    private static void AssertQueueOrMatch(SolitaireSessionResponse session, string ticketId, string matchId)
    {
        if (session is SolitaireQueueSessionResponse queued)
            Assert.Equal(ticketId, queued.TicketId);
        else
            Assert.Equal(matchId, Assert.IsType<SolitaireMatchSessionResponse>(session).MatchId);
    }

    private static async Task<IReadOnlyList<ManagedPlayerProfile>> SeedProfilesAsync(FirestoreDb database, int count)
    {
        string[] names = ["CopperGarden", "SilverMeteor", "QuietMaple", "BlueLantern"];
        var profiles = new List<ManagedPlayerProfile>();
        var repository = new FirestoreManagedPlayerProfileRepository(database);
        for (var index = 0; profiles.Count < count; index++)
        {
            var id = $"managed-solitaire-test-{index}";
            if (ManagedPlayerAvailabilityPolicy.IsSleeping(id, Start) ||
                ManagedPlayerAvailabilityPolicy.IsSleeping(id, Start.AddHours(2))) continue;
            // Profiles created before the Solitaire adapter existed are reusable too.
            var profile = new ManagedPlayerProfile(id, names[profiles.Count], 2 + profiles.Count % 3,
                new HashSet<string>([ManagedPlayerGames.Blackjack],
                    StringComparer.OrdinalIgnoreCase), Start);
            Assert.True(await repository.TryCreateAsync(profile, default));
            profiles.Add(profile);
        }
        return profiles;
    }

    private static Task SeedBalanceAsync(FirestoreDb database, string userId) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 100L,
            ["availableFractionalCents"] = 0L,
            ["version"] = 1L,
            ["updatedAt"] = Timestamp.FromDateTime(Start)
        });

    private static async Task<long> ReadBalanceAsync(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return checked(Field<long>(snapshot, "available") * 100 +
            (snapshot.TryGetValue<long>("availableFractionalCents", out var cents) ? cents : 0));
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> LedgerAsync(FirestoreDb database) =>
        (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> AssignmentsAsync(FirestoreDb database) =>
        (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> StoredPlayersAsync(FirestoreDb database, string matchId) =>
        (await database.Collection("solitaireMatchPlayers").WhereEqualTo("matchId", matchId).GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> CardGameResultsAsync(FirestoreDb database, string matchId) =>
        (await database.Collection("cardGameResults").WhereEqualTo("matchId", matchId).GetSnapshotAsync()).Documents;

    private static T Field<T>(DocumentSnapshot snapshot, string field) => snapshot.GetValue<T>(field);

    private static void AssertAutomationNeutral(SolitaireSessionResponse session)
    {
        var json = JsonSerializer.Serialize(session, session.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var privateField in new[] { "isBot", "skill", "difficulty", "synthetic", "managed-", "dealSeed" })
            Assert.DoesNotContain(privateField, json, StringComparison.OrdinalIgnoreCase);
    }
}
