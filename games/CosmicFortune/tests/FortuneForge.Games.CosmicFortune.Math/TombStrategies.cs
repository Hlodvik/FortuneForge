using FortuneForge.Games.Slots;

namespace FortuneForge.Games.CosmicFortune.RtpAnalysis;

internal sealed record TombTrial(SealedTombState After, SealedTombResult Result, int Actions);

internal static class TombStrategies
{
    internal static decimal OptimalExpectedMultiplier => TombOptimalPolicy.ExpectedPayoutMultiplier;
    internal static TombTrial Redeem(SealedTombState state, string strategy, IGameRandomSource prizes, IGameRandomSource decisions)
    {
        var transition = SealedTombRules.Act(state, new("enter", state.Revision), prizes);
        state = transition.After;
        var actions = 1;
        while (state.ActiveRun is { } run)
        {
            if (++actions > 10) throw new InvalidOperationException("The production expedition exceeded its finite action bound.");
            SealedTombAction action;
            if (run.Phase == "finale") action = new("open-sarcophagus", state.Revision);
            else
            {
                var kind = strategy switch
                {
                    "coins" => "coin",
                    "keys" => run.Path.Count < 3 ? "coin" : "key",
                    "random" => run.Choices[decisions.Next(run.Choices.Count)].Kind,
                    "optimal" => TombOptimalPolicy.ChooseRoomKind(state),
                    _ => throw new ArgumentException("Unknown tomb strategy."),
                };
                action = kind == "collect" ? new("collect", state.Revision) :
                    new("open-room", state.Revision, run.Choices.Single(choice => choice.Kind == kind).Id);
            }
            transition = SealedTombRules.Act(state, action, prizes);
            state = transition.After;
        }
        var result = state.LastRun ?? throw new InvalidOperationException("A production expedition did not produce its settled receipt.");
        if (transition.PayoutPoints != result.PayoutPoints || result.PayoutPoints < result.FundedWagerPoints * SealedTombRules.BaseMultiplier)
            throw new InvalidOperationException("The production expedition receipt disagrees with its settlement.");
        state = SealedTombRules.Act(state, new("dismiss", state.Revision), prizes).After;
        return new(state, result, actions + 1);
    }
}
