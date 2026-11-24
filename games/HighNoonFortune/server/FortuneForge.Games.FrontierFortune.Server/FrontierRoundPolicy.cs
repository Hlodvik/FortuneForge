using FortuneForge.Games.Slots;

namespace FortuneForge.Games.FrontierFortune.Server;

public static class FrontierRoundPolicy
{
    public static FrontierPreparedRound Prepare(string account, FrontierCommand command, FrontierAccountState before,
        IGameRandomSource random, DateTime nowUtc)
    {
        if (command.MathVersion is not null) return OutlawRoundPolicy.Prepare(account, command, before, random, nowUtc);
        command.Validate();
        before.Validate();
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Use an authoritative UTC time.");
        var admission = Guid.NewGuid().ToString("N");
        FrontierSpin? spin = null;
        FrontierShotPreview? shot = null;
        FrontierAccountState after;
        long wager = 0, payout;
        if (command.Kind == "spin")
        {
            if (before.Bonus is { Completed: false })
                throw new FrontierConflictException("Finish or bank the active Deadeye Bounty before spinning.", "frontier-bounty-active");
            spin = new FrontierFortuneEngine(random).Spin(command.WagerPoints, admission);
            after = new(before.Revision + 1, spin.Reels, spin.Wins, spin.TotalPoints, spin.Bonus);
            wager = checked(command.WagerPoints * FrontierRules.Definition.PointValueInCents);
            payout = checked(spin.TotalPoints * FrontierRules.Definition.PointValueInCents);
        }
        else
        {
            if (before.Revision != command.ExpectedRevision)
                throw new FrontierConflictException("The bounty board changed. Refresh before taking another action.", "frontier-stale-revision");
            if (before.Bonus is not { Completed: false } bonus || bonus.BonusId != command.BonusId)
                throw new FrontierConflictException("That bounty is no longer active.", "frontier-bounty-unavailable");
            var transition = command.Kind == "shoot" ? FrontierBounties.Shoot(bonus, command.TargetIndex!.Value) : FrontierBounties.Bank(bonus);
            shot = transition.Shot;
            after = before with { Revision = checked(before.Revision + 1), Wins = [], LastWinPoints = transition.PayoutPoints,
                Bonus = transition.Bonus };
            payout = checked(transition.PayoutPoints * FrontierRules.Definition.PointValueInCents);
        }
        var round = new FrontierPreparedRound(admission, account, command, before, after, wager, payout, nowUtc, spin, shot, "");
        return round with { OutcomeSha256 = Digest(round) };
    }

    public static string Digest(FrontierPreparedRound round) => FrontierRoundIdentity.Hash(round with { OutcomeSha256 = "" });

    public static void Validate(FrontierPreparedRound round)
    {
        if (round.Outlaw is not null) { OutlawRoundPolicy.Validate(round); return; }
        if (round.Command.MathVersion is not null || round.Before.Outlaw is not null || round.After.Outlaw is not null)
            throw new InvalidOperationException("A v1 record cannot carry Outlaw v2 state.");
        round.Command.Validate();
        round.Before.Validate();
        round.After.Validate();
        if (!Guid.TryParseExact(round.AdmissionId, "N", out var admission) || admission == Guid.Empty ||
            round.StartedAtUtc.Kind != DateTimeKind.Utc || round.OutcomeSha256 != Digest(round) ||
            round.After.Revision != round.Before.Revision + 1)
            throw new InvalidOperationException("The prepared Frontier round failed its integrity checks.");
        FrontierAccountState expected;
        long wager = 0, payout;
        if (round.Command.Kind == "spin")
        {
            if (round.Spin is null || round.Before.Bonus is { Completed: false })
                throw new InvalidOperationException("The prepared spin is invalid.");
            var replay = new FrontierReplayRandom(round.Spin.Draws);
            var spin = new FrontierFortuneEngine(replay).Spin(round.Command.WagerPoints, round.AdmissionId);
            replay.Complete();
            if (FrontierRoundIdentity.Hash(spin) != FrontierRoundIdentity.Hash(round.Spin))
                throw new InvalidOperationException("The recorded Frontier draws do not reproduce its outcome.");
            expected = new(round.Before.Revision + 1, spin.Reels, spin.Wins, spin.TotalPoints, spin.Bonus);
            wager = checked(round.Command.WagerPoints * FrontierRules.Definition.PointValueInCents);
            payout = checked(spin.TotalPoints * FrontierRules.Definition.PointValueInCents);
        }
        else
        {
            if (round.Before.Revision != round.Command.ExpectedRevision || round.Before.Bonus is not { Completed: false } bonus ||
                round.Command.BonusId != bonus.BonusId || round.Spin is not null)
                throw new InvalidOperationException("The prepared bounty action is invalid.");
            var transition = round.Command.Kind == "shoot" ? FrontierBounties.Shoot(bonus, round.Command.TargetIndex!.Value) : FrontierBounties.Bank(bonus);
            expected = round.Before with { Revision = round.Before.Revision + 1, Wins = [], LastWinPoints = transition.PayoutPoints, Bonus = transition.Bonus };
            payout = checked(transition.PayoutPoints * FrontierRules.Definition.PointValueInCents);
            if (FrontierRoundIdentity.Hash(transition.Shot) != FrontierRoundIdentity.Hash(round.Shot))
                throw new InvalidOperationException("The saved shot does not match its preview.");
        }
        if (FrontierRoundIdentity.Hash(expected) != FrontierRoundIdentity.Hash(round.After) ||
            round.WagerCents != wager || round.PayoutCents != payout)
            throw new InvalidOperationException("The saved Frontier settlement does not match its rules.");
    }
}

public sealed class FrontierReplayRandom(IReadOnlyList<FrontierRandomDraw> draws) : IGameRandomSource
{
    private int position;
    public int Next(int maximumExclusive)
    {
        if (position >= draws.Count || draws[position].MaximumExclusive != maximumExclusive)
            throw new InvalidOperationException("The recorded Frontier random sequence is incomplete.");
        var value = draws[position++].Value;
        if (value < 0 || value >= maximumExclusive) throw new InvalidOperationException("A recorded Frontier draw is invalid.");
        return value;
    }
    public void Complete()
    {
        if (position != draws.Count) throw new InvalidOperationException("The recorded Frontier sequence contains unused draws.");
    }
}
