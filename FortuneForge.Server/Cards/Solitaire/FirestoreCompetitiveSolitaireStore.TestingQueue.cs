using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    private async Task<SolitaireStoreSession> JoinTestingMatchAsync(
        string userId,
        string displayName,
        int playerCount,
        long buyInCents,
        int drawCount,
        string idempotencyKey,
        uint dealSeed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var partitionKey = PartitionKey(playerCount, buyInCents, drawCount);
        await AdvancePartitionMatchAsync(partitionKey, nowUtc, cancellationToken);

        var ticketId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var ticketReference = TicketDocument(ticketId);
        var partitionReference = PartitionDocument(partitionKey);
        var sessionReference = SessionDocument(userId);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var balanceReference = BalanceDocument(userId);
        var buyInReference = BalanceTransactionDocument($"{ticketId}-buyin");
        var detail = $"{playerCount}:{buyInCents}:{drawCount}";

        await database.RunTransactionAsync(
            async transaction =>
            {
                var initial = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(sessionReference, cancellationToken),
                    transaction.GetSnapshotAsync(partitionReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken));
                if (initial[0].Exists)
                {
                    VerifyAction(initial[0], "join", ticketId, detail);
                    return false;
                }

                var sessionKind = ReadString(initial[1], "kind");
                if (!string.IsNullOrEmpty(sessionKind) && sessionKind != SolitaireSessionKinds.Idle)
                {
                    throw new SolitaireConflictException(
                        "Finish or dismiss the current Solitaire session before joining another match.");
                }

                var availableCents = ReadBalanceCents(initial[3]);
                if (availableCents < buyInCents)
                {
                    throw new SolitaireInsufficientCreditsException(availableCents, buyInCents);
                }

                SolitaireMatch? acceptingMatch = null;
                var activeMatchId = EmptyToNull(ReadString(initial[2], "activeMatchId"));
                if (activeMatchId is not null)
                {
                    var activeSnapshot = await transaction.GetSnapshotAsync(
                        MatchDocument(activeMatchId),
                        cancellationToken);
                    if (activeSnapshot.Exists)
                    {
                        var candidate = ReadMatch(activeSnapshot);
                        if (candidate.Status == PlayingMatchStatus &&
                            !candidate.BotsFilled &&
                            !candidate.PlayerIds.Contains(userId, StringComparer.Ordinal) &&
                            candidate.PlayerIds.Count < candidate.PlayerCount &&
                            (candidate.BotFillEligibleAtUtc is null || nowUtc < candidate.BotFillEligibleAtUtc))
                        {
                            acceptingMatch = candidate;
                        }
                    }
                }

                var matchedTicket = new SolitaireTicket(
                    ticketId,
                    userId,
                    displayName,
                    playerCount,
                    buyInCents,
                    partitionKey,
                    MatchedStatus,
                    nowUtc,
                    null)
                {
                    DrawCount = drawCount
                };
                SolitaireMatch match;
                int seat;
                if (acceptingMatch is null)
                {
                    var matchId = CreateLookupKey($"{partitionKey}\n{ticketId}");
                    match = new SolitaireMatch(
                        matchId,
                        playerCount,
                        buyInCents,
                        buyInCents,
                        buyInCents,
                        0,
                        dealSeed,
                        nowUtc,
                        nowUtc.Add(SolitaireCompetitionRules.MatchDuration),
                        PlayingMatchStatus,
                        [userId],
                        [displayName],
                        [ticketId],
                        [nowUtc],
                        null,
                        null)
                    {
                        PartitionKey = partitionKey,
                        DrawCount = drawCount
                    };
                    seat = 1;
                    matchedTicket = matchedTicket with { MatchId = matchId };
                }
                else
                {
                    seat = acceptingMatch.PlayerIds.Count + 1;
                    var realPlayerCount = acceptingMatch.PlayerIds.Count + 1;
                    var poolCents = checked(realPlayerCount * buyInCents);
                    var payoutCents = realPlayerCount == 1
                        ? buyInCents
                        : checked(poolCents * 90 / 100);
                    match = acceptingMatch with
                    {
                        PrizePoolCents = poolCents,
                        WinnerPayoutCents = payoutCents,
                        PlatformFeeCents = checked(poolCents - payoutCents),
                        PlayerIds = [.. acceptingMatch.PlayerIds, userId],
                        DisplayNames = [.. acceptingMatch.DisplayNames, displayName],
                        TicketIds = [.. acceptingMatch.TicketIds, ticketId],
                        JoinedAtUtc = [.. acceptingMatch.JoinedAtUtc, nowUtc],
                        BotsFilled = realPlayerCount == playerCount
                    };
                    matchedTicket = matchedTicket with { MatchId = match.MatchId };
                }

                transaction.Create(ticketReference, TicketData(matchedTicket));
                transaction.Set(
                    balanceReference,
                    BalanceUpdate(checked(availableCents - buyInCents), nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    buyInReference,
                    BalanceTransactionData(
                        buyInReference.Id,
                        userId,
                        -buyInCents,
                        checked(availableCents - buyInCents),
                        "solitaire-buyin",
                        idempotencyKey,
                        nowUtc));
                transaction.Create(
                    actionReference,
                    ActionData(userId, "join", ticketId, detail, nowUtc));
                transaction.Set(
                    MatchDocument(match.MatchId),
                    MatchData(match),
                    acceptingMatch is null ? SetOptions.Overwrite : SetOptions.MergeAll);
                transaction.Create(
                    PlayerDocument(match.MatchId, userId),
                    PlayerData(new SolitairePlayerState(
                        match.MatchId,
                        userId,
                        displayName,
                        seat,
                        SolitairePlayerStatuses.Playing,
                        SolitaireEngine.CreateGame(match.DealSeed, match.DrawCount),
                        1,
                        null,
                        null,
                        0,
                        false)
                    {
                        StartedAtUtc = nowUtc,
                        DeadlineAtUtc = nowUtc.Add(SolitaireCompetitionRules.MatchDuration)
                    }));
                transaction.Set(
                    sessionReference,
                    SessionData(userId, SolitaireSessionKinds.Match, null, match.MatchId, nowUtc),
                    SetOptions.MergeAll);
                transaction.Set(partitionReference, new Dictionary<string, object>
                {
                    ["partitionKey"] = partitionKey,
                    ["playerCount"] = playerCount,
                    ["buyInCents"] = buyInCents,
                    ["drawCount"] = drawCount,
                    ["activeMatchId"] = match.BotsFilled ? string.Empty : match.MatchId,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                return true;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }
}
