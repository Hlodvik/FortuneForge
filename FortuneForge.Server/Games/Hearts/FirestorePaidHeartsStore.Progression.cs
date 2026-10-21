using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

internal sealed partial class FirestorePaidHeartsStore
{
    public async Task SweepAsync(DateTime now, CancellationToken ct)
    {
        var live = await database.Collection("heartsPaidMatches").WhereLessThanOrEqualTo("nextSweepAt", Timestamp.FromDateTime(now))
            .OrderBy("nextSweepAt").Limit(100).GetSnapshotAsync(ct);
        foreach (var snapshot in live.Documents)
        {
            try { await AdvanceAsync(snapshot.Id, now, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger?.LogError(exception, "Hearts table {MatchId} will retry progression.", snapshot.Id); }
        }
        var cleanup = await database.Collection("heartsPaidMatches").WhereEqualTo("releasePending", true).Limit(100).GetSnapshotAsync(ct);
        foreach (var snapshot in cleanup.Documents)
        {
            try { await ReleaseIfNeededAsync(snapshot.Id, now, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger?.LogError(exception, "Hearts table {MatchId} will retry profile cleanup.", snapshot.Id); }
        }
    }

    private async Task AdvanceAsync(string id, DateTime now, CancellationToken ct)
    {
        var snapshot = await Match(id).GetSnapshotAsync(ct);
        if (!snapshot.Exists) return;
        var observed = Read(snapshot);
        if (observed.Status == "queue")
        {
            if (now >= observed.CreatedAtUtc.AddMinutes(2)) await ExpireQueueAsync(id, now, ct);
            else if (now >= observed.NextAdmissionAtUtc && options.AllowManagedPlayers) await AdmitManagedAsync(id, now, ct);
        }
        else if (observed.Status == "match")
        {
            if (observed.LeaseRenewalAtUtc is null || now >= observed.LeaseRenewalAtUtc)
                await RenewAsync(observed, now, ct);
            if (observed.NextActionAtUtc is { } due && now >= due)
            {
                var seed = PaidHeartsIds.Seed();
                await TransactionAsync(async tx =>
                {
                    var latest = Read(await tx.GetSnapshotAsync(Match(id), ct));
                    if (latest.Status != "match" || latest.NextActionAtUtc is not { } at || now < at) return false;
                    var balances = await BalancesAsync(tx, latest, ct);
                    if (latest.State!.Round.Phase == HeartsPhase.Complete)
                    {
                        latest.State = HeartsMatchEngine.StartNextRound(latest.State, seed);
                        latest.RecentTrick = null;
                        latest.Message = "A new round has been dealt.";
                    }
                    else if (latest.ScheduledSeat is { } seat)
                    {
                        var actor = latest.Players.Single(player => player.Seat == seat);
                        if (actor.IsAccountBacked && !actor.HasLeft)
                        {
                            actor.MissedTurns++;
                            if (actor.MissedTurns >= 2) Forfeit(actor);
                        }
                        var round = latest.State.Round;
                        HeartsCommand action = round.Phase == HeartsPhase.Passing
                            ? new PassHeartsCards(seat, HeartsManagedActionPolicy.ChoosePass(round, seat,
                                actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion++))
                            : new PlayHeartsCard(seat, HeartsManagedActionPolicy.ChooseCard(round, seat,
                                actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion++));
                        Apply(latest, action);
                        if (actor.IsAccountBacked && actor.MissedTurns > 0)
                            latest.Message = actor.HasLeft
                                ? $"{actor.DisplayName} was removed for inactivity and forfeited their entry."
                                : $"An automatic legal play was made for {actor.DisplayName}. Another missed turn forfeits their entry.";
                    }
                    if (latest.State.Winner is not null || latest.Players.All(player => !player.IsAccountBacked || player.HasLeft))
                        Settle(tx, latest, balances, now, abandoned: latest.State.Winner is null);
                    else Schedule(latest, now);
                    latest.Version++;
                    latest.UpdatedAtUtc = now;
                    Write(tx, latest);
                    return true;
                }, ct);
            }
        }
        await ReleaseIfNeededAsync(id, now, ct);
    }

    private async Task AdmitManagedAsync(string id, DateTime now, CancellationToken ct)
    {
        var claimId = Guid.NewGuid().ToString("N");
        var claimed = await TransactionAsync(async tx =>
        {
            var match = Read(await tx.GetSnapshotAsync(Match(id), ct));
            if (match.Status != "queue" || match.Players.Count >= 4 || now < match.NextAdmissionAtUtc ||
                !match.Players.Any(player => player.IsAccountBacked) ||
                (match.AdmissionClaimExpiresAtUtc is { } expires && now < expires)) return false;
            var humans = matchmaking.PlanQueue(match.Players,
                new MultiplayerQueueRules(1, 4), now, player => player.IsAccountBacked,
                player => player.JoinedAtUtc, _ => match.CreatedAtUtc.AddSeconds(options.HumanQueueSeconds), player => player.UserId);
            if (!humans.IsReady) return false;
            match.AdmissionClaim = claimId;
            match.AdmissionClaimExpiresAtUtc = now.AddSeconds(30);
            Write(tx, match);
            return true;
        }, ct);
        if (!claimed) return;
        IReadOnlyList<ManagedPlayerProfile> reserved = [];
        var assignmentId = $"{Assignment(id)}:{claimId}";
        var admitted = false;
        try
        {
            var observed = Read(await Match(id).GetSnapshotAsync(ct));
            reserved = await matchmaking.ReserveManagedPlayersAsync(ManagedPlayerGames.Hearts, assignmentId, 1,
                observed.Players.Select(player => player.UserId).ToArray(), now, ct);
            if (reserved.Count != 1) return;
            var profile = reserved[0];
            var decisionSeed = PaidHeartsIds.PrivateSeed();
            var timingSeed = PaidHeartsIds.PrivateSeed();
            var shuffleSeed = PaidHeartsIds.Seed();
            admitted = await TransactionAsync(async tx =>
            {
                var match = Read(await tx.GetSnapshotAsync(Match(id), ct));
                if (match.Status != "queue" || match.AdmissionClaim != claimId || match.Players.Count >= 4 ||
                    !match.Players.Any(player => player.IsAccountBacked)) return false;
                match.Players.Add(new PaidHeartsParticipant
                {
                    UserId = profile.UserId, DisplayName = profile.PlayerName, SkillLevel = profile.SkillLevel,
                    ManagedAssignmentId = assignmentId,
                    Seat = Enum.GetValues<PlayerSeat>().First(seat => match.Players.All(player => player.Seat != seat)),
                    JoinedAtUtc = now, DecisionSeed = decisionSeed, TimingSeed = timingSeed
                });
                match.AdmissionClaim = null;
                match.AdmissionClaimExpiresAtUtc = null;
                match.Version++;
                match.UpdatedAtUtc = now;
                match.NextAdmissionAtUtc = now + ManagedQueueArrivalPolicy.NextGap(
                    ManagedPlayerGames.Hearts, id, match.Players.Count(player => !player.IsAccountBacked) + 1);
                if (match.Players.Count == 4)
                {
                    Start(match, now, shuffleSeed);
                    WriteFunding(tx, match, now);
                    tx.Set(Partition(match.EntryCents), new Dictionary<string, object> { ["matchId"] = "" });
                }
                Write(tx, match);
                return true;
            }, ct);
        }
        finally
        {
            if (!admitted && reserved.Count > 0)
                await matchmaking.ReleaseManagedPlayersAsync(assignmentId, reserved.Select(profile => profile.UserId).ToArray(), now, CancellationToken.None);
            if (!admitted)
                await TransactionAsync(async tx =>
                {
                    var match = Read(await tx.GetSnapshotAsync(Match(id), CancellationToken.None));
                    if (match.AdmissionClaim != claimId) return false;
                    match.AdmissionClaim = null;
                    match.AdmissionClaimExpiresAtUtc = null;
                    Write(tx, match);
                    return true;
                }, CancellationToken.None);
        }
    }

    private async Task ExpireQueueAsync(string id, DateTime now, CancellationToken ct)
    {
        await TransactionAsync(async tx =>
        {
            var match = Read(await tx.GetSnapshotAsync(Match(id), ct));
            if (match.Status != "queue" || now < match.CreatedAtUtc.AddMinutes(2)) return false;
            var balances = await BalancesAsync(tx, match, ct);
            var humans = match.Players.Where(player => player.IsAccountBacked).ToArray();
            var sessions = await Task.WhenAll(humans.Select(player => tx.GetSnapshotAsync(Session(player.UserId), ct)));
            for (var index = 0; index < humans.Length; index++)
            {
                var player = humans[index];
                var after = checked(balances[player.UserId] + match.EntryCents);
                tx.Set(Balance(player.UserId), BalanceData(after, now), SetOptions.MergeAll);
                Ledger(tx, $"{id}-queue-refund-{player.EntryId}", player.UserId,
                    match.EntryCents, after, "hearts-queue-refund", id, now);
                if (Text(sessions[index], "matchId") == id)
                    tx.Set(Session(player.UserId), new Dictionary<string, object> { ["matchId"] = "" });
            }
            CancelEmptyQueue(match, now);
            match.Version++;
            Write(tx, match);
            return true;
        }, ct);
    }

    private async Task RenewAsync(PaidHeartsMatch observed, DateTime now, CancellationToken ct)
    {
        var lost = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in observed.Players.Where(player => !player.IsAccountBacked && !player.HasLeft))
            if (!await matchmaking.TryRenewManagedPlayersAsync(ManagedPlayerGames.Hearts,
                    player.ManagedAssignmentId!, [player.UserId], now, ct)) lost.Add(player.UserId);
        await TransactionAsync(async tx =>
        {
            var match = Read(await tx.GetSnapshotAsync(Match(observed.Id), ct));
            if (match.Status != "match") return false;
            if (lost.Count > 0)
            {
                foreach (var player in match.Players.Where(player => lost.Contains(player.UserId)))
                {
                    player.HasLeft = true;
                    player.DisplayName = "Table substitute";
                }
                // Complete those seats automatically without continuing to display
                // a profile whose cross-game reservation no longer belongs here.
                match.Message = "A disconnected seat is being completed automatically.";
                match.Version++;
            }
            match.LeaseRenewalAtUtc = now.AddMinutes(3);
            Write(tx, match);
            return true;
        }, ct);
    }

    private async Task ReleaseIfNeededAsync(string id, DateTime now, CancellationToken ct)
    {
        var snapshot = await Match(id).GetSnapshotAsync(ct);
        if (!snapshot.Exists || Read(snapshot) is not { ReleasePending: true } match) return;
        foreach (var group in match.Players.Where(player => !player.IsAccountBacked).GroupBy(player => player.ManagedAssignmentId!))
            await matchmaking.ReleaseManagedPlayersAsync(group.Key, group.Select(player => player.UserId).ToArray(), now, ct);
        await TransactionAsync(async tx =>
        {
            var latest = Read(await tx.GetSnapshotAsync(Match(id), ct));
            if (!latest.ReleasePending) return false;
            latest.ReleasePending = false;
            Write(tx, latest);
            return true;
        }, ct);
    }

    private void Start(PaidHeartsMatch match, DateTime now, uint seed)
    {
        match.Status = "match";
        match.State = HeartsMatchEngine.Start(seed, 100);
        match.Message = "Pass three cards to the left.";
        match.AdmissionClaim = null;
        match.AdmissionClaimExpiresAtUtc = null;
        match.LeaseRenewalAtUtc = now.AddMinutes(3);
        Schedule(match, now);
    }

    private static void Apply(PaidHeartsMatch match, HeartsCommand command)
    {
        var before = match.State!.Round.CompletedTricks.Count;
        var transition = HeartsMatchEngine.Apply(match.State, command);
        match.State = transition.State;
        match.Message = transition.Message;
        if (transition.State.Round.CompletedTricks.Count > before)
            match.RecentTrick = transition.State.Round.CompletedTricks[^1];
        match.ScheduledSeat = null;
        match.NextActionAtUtc = null;
    }

    private void Schedule(PaidHeartsMatch match, DateTime now)
    {
        var state = match.State!;
        if (state.Round.Phase == HeartsPhase.Complete)
        {
            match.ScheduledSeat = null;
            match.NextActionAtUtc = now.AddSeconds(6);
            return;
        }
        var actor = state.Round.Phase == HeartsPhase.Passing
            ? match.Players.Where(player => !state.Round.SubmittedPasses.Any(pass => pass.Seat == player.Seat))
                .OrderBy(player => player.IsAccountBacked && !player.HasLeft).ThenBy(player => player.Seat).First()
            : match.Players.Single(player => player.Seat == state.Round.Turn);
        match.ScheduledSeat = actor.Seat;
        if (actor.IsAccountBacked && !actor.HasLeft)
            match.NextActionAtUtc = now.AddSeconds(options.ActionTimeoutSeconds);
        else
        {
            var random = new DeterministicManagedPlayerRandom(actor.TimingSeed, $"paid-hearts-think:{actor.UserId}:{actor.TimingVersion++}");
            var milliseconds = 1_200 + random.Next(2_801);
            if (random.NextDouble() < 0.08) milliseconds += 2_500 + random.Next(3_000);
            match.NextActionAtUtc = now.AddMilliseconds(milliseconds);
        }
    }

    private static void Forfeit(PaidHeartsParticipant actor)
    {
        if (actor.HasLeft) throw new PaidHeartsConflictException("This seat has already been forfeited.");
        actor.HasLeft = true;
    }

    private static void CancelEmptyQueue(PaidHeartsMatch match, DateTime now)
    {
        match.Status = "cancelled";
        match.CompletedAtUtc = now;
        match.UpdatedAtUtc = now;
        match.ReleasePending = match.Players.Any(player => !player.IsAccountBacked);
    }

    private void WriteFunding(Transaction tx, PaidHeartsMatch match, DateTime now) =>
        tx.Create(database.Collection("heartsPaidFunding").Document(match.Id), new Dictionary<string, object>
        {
            ["matchId"] = match.Id, ["currencyId"] = Currency, ["entryCents"] = match.EntryCents,
            ["humanEntryCents"] = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked)),
            ["houseFundedEntryCents"] = checked(match.EntryCents * match.Players.Count(player => !player.IsAccountBacked)),
            ["grossPoolCents"] = match.PotCents, ["financialClassification"] = "house-funded-hearts-pool-v1",
            ["fundedAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L
        });

    private void Settle(Transaction tx, PaidHeartsMatch match, IReadOnlyDictionary<string, long> balances, DateTime now, bool abandoned = false)
    {
        if (match.Status == "result") return;
        var eligible = match.Players.Where(player => !player.HasLeft || !player.IsAccountBacked).ToArray();
        abandoned |= eligible.Length == 0;
        var shares = abandoned ? new Dictionary<string, long>() : PaidHeartsSettlement.Split(match.Id, match.PotCents,
            eligible, match.State!.Score);
        match.Status = "result";
        match.CompletedAtUtc = now;
        match.NextActionAtUtc = null;
        match.ScheduledSeat = null;
        match.ReleasePending = match.Players.Any(player => !player.IsAccountBacked);
        match.Winners = match.Players.Where(player => shares.ContainsKey(player.UserId)).Select(player => player.Seat).ToList();
        match.Message = abandoned ? "All players left. Entries were forfeited."
            : match.Winners.Count > 1 ? "The lowest scores tied. The full pot was shared."
            : "The player with the lowest score won the full pot.";
        foreach (var player in match.Players)
        {
            player.PayoutCents = shares.GetValueOrDefault(player.UserId);
            if (player.IsAccountBacked && player.PayoutCents > 0)
            {
                var after = checked(balances[player.UserId] + player.PayoutCents);
                tx.Set(Balance(player.UserId), BalanceData(after, now), SetOptions.MergeAll);
                Ledger(tx, $"{match.Id}-payout-{PaidHeartsIds.Hash(player.UserId)}", player.UserId,
                    player.PayoutCents, after, "hearts-match-payout", match.Id, now);
            }
        }
        var humanEntry = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked));
        var houseEntry = checked(match.PotCents - humanEntry);
        var humanPayout = match.Players.Where(player => player.IsAccountBacked).Sum(player => player.PayoutCents);
        var botPrize = match.Players.Where(player => !player.IsAccountBacked).Sum(player => player.PayoutCents);
        var forfeited = abandoned ? match.PotCents : 0L;
        if (checked(humanPayout + botPrize + forfeited) != match.PotCents)
            throw new InvalidOperationException("The Hearts prize pool does not reconcile.");
        tx.Create(database.Collection("heartsPaidRevenue").Document(match.Id), new Dictionary<string, object>
        {
            ["matchId"] = match.Id, ["currencyId"] = Currency, ["humanEntryCents"] = humanEntry,
            ["houseFundedEntryCents"] = houseEntry, ["grossPoolCents"] = match.PotCents,
            ["humanPayoutCents"] = humanPayout, ["botPrizeCents"] = botPrize,
            ["forfeitedPoolCents"] = forfeited, ["houseNetCents"] = checked(humanEntry - humanPayout),
            ["platformFeeCents"] = 0L, ["financialClassification"] = "house-funded-hearts-pool-v1",
            ["botFinancialContributionCents"] = 0L,
            ["recognizedAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L
        });
        foreach (var player in match.Players.Where(player => player.IsAccountBacked))
        {
            var data = Data(match);
            data["userId"] = player.UserId;
            tx.Set(database.Collection("heartsPaidHistory").Document(PaidHeartsIds.Hash($"{match.Id}\n{player.UserId}")), data);
        }
    }
}
