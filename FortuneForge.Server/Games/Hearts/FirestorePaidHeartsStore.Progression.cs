using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
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
            // A legacy pre-consent queue may already contain shared profiles.
            // Refund and close it instead of silently dealing with those seats.
            var legacySeats = observed.Players.Any(player => !player.IsAccountBacked);
            if (legacySeats || now >= observed.CreatedAtUtc.AddMinutes(2))
                await ExpireQueueAsync(id, now, ct, legacySeats);
        }
        else if (observed.Status == "match")
        {
            if (observed.Players.Any(player => !player.IsAccountBacked && player.ManagedAssignmentId is not null) &&
                (observed.LeaseRenewalAtUtc is null || now >= observed.LeaseRenewalAtUtc))
                await RenewAsync(observed, now, ct);
            if (observed.NextActionAtUtc is { } due && now >= due)
            {
                var seed = PaidHeartsIds.Seed();
                await TransactionAsync(async tx =>
                {
                    var latest = Read(await tx.GetSnapshotAsync(Match(id), ct));
                    if (!ActionIsDue(latest, now)) return false;
                    var balances = await BalancesAsync(tx, latest, ct);
                    AdvanceDueAction(tx, latest, balances, now, seed);
                    return true;
                }, ct);
            }
        }
        await ReleaseIfNeededAsync(id, now, ct);
    }

    private static bool ActionIsDue(PaidHeartsMatch match, DateTime now) =>
        match.Status == "match" && match.NextActionAtUtc is { } due && now >= due;

    private void AdvanceDueAction(Transaction tx, PaidHeartsMatch match,
        IReadOnlyDictionary<string, long> balances, DateTime now, uint nextSeed)
    {
        if (match.State!.Round.Phase == HeartsPhase.Complete)
        {
            match.State = HeartsMatchEngine.StartNextRound(match.State, nextSeed);
            match.RecentTrick = null;
            match.Message = "A new round has been dealt.";
        }
        else if (match.ScheduledSeat is { } seat)
        {
            var actor = match.Players.Single(player => player.Seat == seat);
            if (actor.IsAccountBacked && !actor.HasLeft)
            {
                actor.MissedTurns++;
                // Prize eligibility ends before any automatic choice is made.
                // The substitute only keeps the remaining players' table moving.
                Forfeit(actor);
            }
            var round = match.State.Round;
            HeartsCommand action = round.Phase == HeartsPhase.Passing
                ? new PassHeartsCards(seat, HeartsManagedActionPolicy.ChoosePass(round, seat,
                    actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion++))
                : new PlayHeartsCard(seat, HeartsManagedActionPolicy.ChooseCard(round, seat,
                    actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion++));
            Apply(match, action);
            if (actor.IsAccountBacked && actor.MissedTurns > 0)
                match.Message = $"{actor.DisplayName} was removed when their turn timer expired and forfeited their entry. Their remaining cards will be played automatically.";
        }
        if (match.State.Winner is not null || match.Players.All(player => !player.IsAccountBacked || player.HasLeft))
            Settle(tx, match, balances, now, abandoned: match.State.Winner is null);
        else Schedule(match, now);
        match.Version++;
        match.UpdatedAtUtc = now;
        Write(tx, match);
    }

    private async Task ExpireQueueAsync(string id, DateTime now, CancellationToken ct, bool legacySeats = false)
    {
        await TransactionAsync(async tx =>
        {
            var match = Read(await tx.GetSnapshotAsync(Match(id), ct));
            if (match.Status != "queue" ||
                (!legacySeats && now < match.CreatedAtUtc.AddMinutes(2))) return false;
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
            match.Message = legacySeats
                ? "This older queue was closed and your entry refunded. Join again to choose human players or explicit house opponents."
                : "No table filled within two minutes. Your entry was refunded; you can search again.";
            match.Version++;
            Write(tx, match);
            return true;
        }, ct);
    }

    private async Task RenewAsync(PaidHeartsMatch observed, DateTime now, CancellationToken ct)
    {
        var lost = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in observed.Players.Where(player => !player.IsAccountBacked && !player.HasLeft && player.ManagedAssignmentId is not null))
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
        foreach (var group in match.Players.Where(player => !player.IsAccountBacked && player.ManagedAssignmentId is not null)
                     .GroupBy(player => player.ManagedAssignmentId!))
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
        match.InitialHumanEntryCents = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked));
        match.Message = "Pass three cards to the left.";
        match.AdmissionClaim = null;
        match.AdmissionClaimExpiresAtUtc = null;
        match.LeaseRenewalAtUtc = match.Players.Any(player => !player.IsAccountBacked && player.ManagedAssignmentId is not null)
            ? now.AddMinutes(3) : null;
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
        // Passing is simultaneous. A different player's pass must not restart
        // the timer of the human seat that is already being waited on.
        if (transition.State.Round.Phase != HeartsPhase.Passing || match.ScheduledSeat is not { } waiting ||
            transition.State.Round.SubmittedPasses.Any(pass => pass.Seat == waiting))
        {
            match.ScheduledSeat = null;
            match.NextActionAtUtc = null;
        }
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
        var unchangedDeadline = match.ScheduledSeat == actor.Seat ? match.NextActionAtUtc : null;
        match.ScheduledSeat = actor.Seat;
        if (actor.IsAccountBacked && !actor.HasLeft)
            match.NextActionAtUtc = unchangedDeadline is { } existing && existing > now
                ? existing : now.AddSeconds(options.ActionTimeoutSeconds);
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
        match.ReleasePending = match.Players.Any(player => !player.IsAccountBacked && player.ManagedAssignmentId is not null);
    }

    private void WriteFunding(Transaction tx, PaidHeartsMatch match, DateTime now) =>
        tx.Create(Funding(match.Id), FundingData(match, now));

    private static Dictionary<string, object> FundingData(PaidHeartsMatch match, DateTime now) => new()
        {
            ["matchId"] = match.Id, ["currencyId"] = Currency, ["entryCents"] = match.EntryCents,
            ["humanEntryCents"] = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked)),
            ["houseFundedEntryCents"] = checked(match.EntryCents * match.Players.Count(player => !player.IsAccountBacked)),
            ["initialHumanEntryCents"] = match.InitialHumanEntryCents,
            ["initialHouseFundedEntryCents"] = checked(match.PotCents - match.InitialHumanEntryCents),
            ["takeoverCount"] = 0L,
            ["grossPoolCents"] = match.PotCents, ["financialClassification"] = "house-funded-hearts-pool-v1",
            ["fundedAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L
        };

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
        match.ReleasePending = match.Players.Any(player => !player.IsAccountBacked && player.ManagedAssignmentId is not null);
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
            ["initialHumanEntryCents"] = match.InitialHumanEntryCents > 0 ? match.InitialHumanEntryCents : humanEntry,
            ["houseStakeReclassifiedCents"] = checked(match.HouseTakeoverCount * match.EntryCents),
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
