using FortuneForge.Server.Bots.TexasHoldem;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed record CreditHoldemTableAdvance(
    bool Changed,
    IReadOnlyList<string> ReleasedHumanActorIds);

internal static class CreditHoldemTableCoordinator
{
    // A completed hand remains visible long enough to read the result. A player
    // who does not choose the next hand or leave is then released from the table.
    internal static readonly TimeSpan ResultInactivityDuration = TimeSpan.FromSeconds(45);

    public static CreditHoldemTableAdvance AdvanceIfDue(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (match.Status == "active")
        {
            if (nowUtc >= match.MatchDeadlineAtUtc)
            {
                CreditHoldemEngine.ForceComplete(match, nowUtc);
                return new(true, []);
            }

            var active = match.Players.Single(player => player.Seat == match.ActiveSeat);
            if (TexasHoldemManagedPlayers.IsManaged(active))
                return new(TexasHoldemManagedPlayers.AdvanceIfDue(match, nowUtc), []);

            if (match.ActionDeadlineAtUtc is not { } deadline || nowUtc < deadline)
                return new(false, []);

            if (active.LastMissedActionHand != match.HandNumber)
            {
                active.ConsecutiveMissedActionHands = active.LastMissedActionHand == match.HandNumber - 1
                    ? checked(active.ConsecutiveMissedActionHands + 1)
                    : 1;
                active.LastMissedActionHand = match.HandNumber;
            }

            if (active.ConsecutiveMissedActionHands >= 2)
            {
                CreditHoldemEngine.Leave(match, active.ActorId, nowUtc);
                return new(true, [active.ActorId]);
            }

            var missedHands = active.ConsecutiveMissedActionHands;
            var missedHand = active.LastMissedActionHand;
            CreditHoldemEngine.AdvanceExpiredTurn(match, nowUtc);
            active.ConsecutiveMissedActionHands = missedHands;
            active.LastMissedActionHand = missedHand;
            return new(true, []);
        }

        if (match.Status != "completed" ||
            nowUtc < (match.CompletedAtUtc ?? match.UpdatedAtUtc).Add(ResultInactivityDuration))
            return new(false, []);

        var released = match.Players
            .Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId))
            .Select(player => player.ActorId)
            .ToArray();
        if (released.Length == 0) return new(false, []);

        foreach (var actorId in released) match.LeavingActorIds.Add(actorId);
        match.Version = checked(match.Version + 1);
        match.UpdatedAtUtc = nowUtc;
        return new(true, released);
    }
}
