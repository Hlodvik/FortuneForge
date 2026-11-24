using FortuneForge.Games.Slots;

namespace FortuneForge.Games.FrontierFortune.Server;

public sealed record OutlawAnteView(int Chips, int RequiredChips, long WagerPointsTotal, long FundedWagerPoints, bool Ready);
public sealed record OutlawPreparedPayload(int SchemaVersion, string EngineVersion, OutlawDraw? Draw);
public sealed record OutlawAccountState(int SchemaVersion, long[] ChipWagers, OutlawHandRound? HandRound)
{
    public static OutlawAccountState Empty() => new(2, [], null);
    public static OutlawAccountState From(FrontierAccountState state)
    {
        if (state.Outlaw is { } native) { native.Validate(); return native; }
        if (state.Bonus is { Completed: false })
            throw new FrontierConflictException("Finish the previously earned Frontier v1 round through its recovery route before starting Outlaw's Hand.", "frontier-legacy-obligation");
        return Empty();
    }
    public void Validate()
    {
        if (SchemaVersion != 2 || ChipWagers is null || ChipWagers.Length > OutlawRules.Definition.Ante.RequiredChips + 1)
            throw new InvalidOperationException("The saved ante is invalid.");
        foreach (var wager in ChipWagers) FrontierRules.ValidateWager(wager);
        if (HandRound is not null) OutlawHand.Validate(HandRound);
    }
    public OutlawAnteView View()
    {
        var required = OutlawRules.Definition.Ante.RequiredChips;
        var eligible = ChipWagers.Take(required).ToArray();
        var funded = eligible.Length == 0 ? FrontierRules.Definition.MinWagerPoints : Math.Max(FrontierRules.Definition.MinWagerPoints,
            eligible.Sum() / eligible.Length / FrontierRules.Definition.StepPoints * FrontierRules.Definition.StepPoints);
        return new(ChipWagers.Length, required, ChipWagers.Sum(), funded, ChipWagers.Length >= required);
    }
}

public static class FrontierOutlawDefinitions
{
    public const string GameId = FrontierFortuneEngine.GameId;
    public const string EngineVersion = OutlawRules.EngineVersion;
    public static GameDefinition CreateGame() => FrontierFortuneEngine.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => FrontierFortuneEngine.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => FrontierFortuneEngine.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => FrontierFortuneEngine.CreatePaytable();
}

public static class OutlawRoundPolicy
{
    public static void ValidateCommand(FrontierCommand command)
    {
        if (command.MathVersion != OutlawRules.EngineVersion || command.BonusId is not null || command.TargetIndex is not null)
            throw new ArgumentException("Unsupported Outlaw rule version or command fields.");
        if (command.Kind == "spin")
        {
            FrontierRules.ValidateWager(command.WagerPoints);
            if (command.ExpectedRevision is not null || command.RoundId is not null || command.Held is not null || command.SelectedRows is not null)
                throw new ArgumentException("A base spin cannot include hand fields.");
            return;
        }
        if (command.WagerPoints != 0 || command.ExpectedRevision is null or < 0) throw new ArgumentException("Include the current hand revision.");
        if (command.Kind == "deal-in")
        {
            if (command.RoundId is not null || command.Held is not null || command.SelectedRows is not null) throw new ArgumentException("Deal-in takes no hand choices.");
            return;
        }
        if (!Guid.TryParseExact(command.RoundId, "N", out var id) || id == Guid.Empty) throw new ArgumentException("Include the current hand round ID.");
        if (command.Kind == "deal" && command.Held is { Length: 5 } && command.SelectedRows is null) return;
        if (command.Kind == "confirm" && command.SelectedRows is { Length: 5 } && command.Held is null) return;
        throw new ArgumentException("Choose a valid Outlaw hand action.");
    }

    public static FrontierPreparedRound Prepare(string account, FrontierCommand command, FrontierAccountState before,
        IGameRandomSource random, DateTime nowUtc)
    {
        command.Validate(); before.Validate();
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Use an authoritative UTC time.");
        var admission = Guid.NewGuid().ToString("N");
        var (after, wager, payout, draw) = Transition(before, command, admission, random);
        var round = new FrontierPreparedRound(admission, account, command, before, after, wager, payout, nowUtc, null, null, "")
            { Outlaw = new(2, OutlawRules.EngineVersion, draw) };
        return round with { OutcomeSha256 = FrontierRoundPolicy.Digest(round) };
    }

    private static (FrontierAccountState After, long Wager, long Payout, OutlawDraw? Draw) Transition(
        FrontierAccountState before, FrontierCommand command, string admission, IGameRandomSource random)
    {
        var native = OutlawAccountState.From(before);
        OutlawDraw? draw = null;
        long wager = 0, payout = 0;
        var reels = before.Reels;
        FrontierLineWin[] wins = [];
        long lastWin = 0;
        if (command.Kind == "spin")
        {
            if (native.HandRound is { Phase: not "complete" }) throw new FrontierConflictException("Finish the active Outlaw hand before spinning.", "frontier-hand-active");
            if (native.View().Ready) throw new FrontierConflictException("The ante is ready. Deal in before another paid spin.", "frontier-ante-ready");
            draw = OutlawReels.Draw(random, false);
            reels = draw.Reels;
            wins = FrontierFortuneEngine.Evaluate(reels, command.WagerPoints);
            lastWin = wins.Sum(x => x.AwardPoints);
            var wanted = reels.Sum(x => x.Count(id => id == FrontierRules.Definition.ScatterSymbol));
            var chips = wanted >= OutlawRules.Definition.Ante.WantedForTwoChips ? 2 : wanted >= OutlawRules.Definition.Ante.WantedForOneChip ? 1 : 0;
            native = native with { ChipWagers = native.ChipWagers.Concat(Enumerable.Repeat(command.WagerPoints, chips)).ToArray(), HandRound = null };
            wager = checked(command.WagerPoints * FrontierRules.Definition.PointValueInCents);
            payout = checked(lastWin * FrontierRules.Definition.PointValueInCents);
        }
        else
        {
            if (before.Revision != command.ExpectedRevision) throw new FrontierConflictException("The hand changed. Refresh before taking another action.", "frontier-stale-revision");
            if (command.Kind == "deal-in")
            {
                if (!native.View().Ready || native.HandRound is { Phase: not "complete" }) throw new FrontierConflictException("The next Outlaw hand is not ready.", "frontier-hand-unavailable");
                var funded = native.View().FundedWagerPoints;
                draw = OutlawReels.Draw(random, true); reels = draw.Reels;
                native = native with { ChipWagers = native.ChipWagers.Skip(OutlawRules.Definition.Ante.RequiredChips).ToArray(),
                    HandRound = OutlawHand.Start(admission, funded) };
            }
            else
            {
                if (native.HandRound is not { Phase: not "complete" } hand || hand.RoundId != command.RoundId)
                    throw new FrontierConflictException("That Outlaw hand is no longer active.", "frontier-hand-unavailable");
                if (command.Kind == "deal")
                {
                    hand = OutlawHand.Deal(hand, command.Held!);
                    draw = OutlawReels.Draw(random, true); reels = draw.Reels;
                }
                else
                {
                    hand = OutlawHand.Confirm(hand, before.Reels, command.SelectedRows!);
                    lastWin = hand.AwardPoints;
                    payout = checked(lastWin * FrontierRules.Definition.PointValueInCents);
                }
                native = native with { HandRound = hand };
            }
        }
        native.Validate();
        var after = new FrontierAccountState(checked(before.Revision + 1), reels, wins, lastWin, null) { Outlaw = native };
        return (after, wager, payout, draw);
    }

    public static void Validate(FrontierPreparedRound round)
    {
        round.Command.Validate(); round.Before.Validate(); round.After.Validate();
        if (round.Outlaw is not { SchemaVersion: 2, EngineVersion: OutlawRules.EngineVersion } payload ||
            round.Command.MathVersion != OutlawRules.EngineVersion || round.Spin is not null || round.Shot is not null ||
            !Guid.TryParseExact(round.AdmissionId, "N", out var id) || id == Guid.Empty || round.StartedAtUtc.Kind != DateTimeKind.Utc ||
            round.OutcomeSha256 != FrontierRoundPolicy.Digest(round)) throw new InvalidOperationException("The Outlaw prepared round is invalid.");
        var replay = new FrontierReplayRandom(payload.Draw?.Draws ?? []);
        var expected = Transition(round.Before, round.Command, round.AdmissionId, replay);
        replay.Complete();
        if (FrontierRoundIdentity.Hash(expected.After) != FrontierRoundIdentity.Hash(round.After) || expected.Wager != round.WagerCents ||
            expected.Payout != round.PayoutCents || FrontierRoundIdentity.Hash(expected.Draw) != FrontierRoundIdentity.Hash(payload.Draw))
            throw new InvalidOperationException("The recorded Outlaw action does not reproduce its settlement.");
    }
}
