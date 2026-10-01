using System.Collections.Immutable;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Server.Bots;

namespace FortuneForge.Server.Bots.LiarsDice;

internal static class LiarsDiceManagedActionPolicy
{
    public static LiarsDiceCommand Choose(
        LiarsDiceMatchState state,
        int skillLevel,
        ulong seed,
        int version)
    {
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var playerId = state.CurrentPlayerId;
        var hand = state.Round.Hands[playerId];
        var currentBid = state.Round.CurrentBid;
        if (currentBid is null)
        {
            var face = MostCommonFace(hand);
            return new PlaceLiarsDiceBid(playerId, new LiarsDiceBid(1, face));
        }

        var ownMatches = hand.Count(die => die == currentBid.Face);
        var totalDice = state.Round.Hands.Values.Sum(values => values.Length);
        var opponents = totalDice - hand.Length;
        var expected = ownMatches + ((opponents + 5) / 6);
        var challengeMargin = skillLevel == ManagedPlayerSkillLevels.Poor ? 2 : 1;
        var random = new DeterministicManagedPlayerRandom(seed, $"liars-dice:{version}:{skillLevel}");
        if (currentBid.Quantity > expected + challengeMargin ||
            currentBid.Quantity == totalDice && currentBid.Face.Value == 6 ||
            skillLevel < ManagedPlayerSkillLevels.Strong && random.NextDouble() < 0.04)
            return new ChallengeLiarsDiceBid(playerId);

        return new PlaceLiarsDiceBid(playerId, Raise(currentBid, totalDice));
    }

    private static DieValue MostCommonFace(ImmutableArray<DieValue> hand) => hand
        .GroupBy(die => die.Value)
        .OrderByDescending(group => group.Count())
        .ThenByDescending(group => group.Key)
        .Select(group => new DieValue(group.Key))
        .First();

    private static LiarsDiceBid Raise(LiarsDiceBid current, int totalDice)
    {
        var next = current.Face.Value < 6
            ? new LiarsDiceBid(current.Quantity, new DieValue(current.Face.Value + 1))
            : new LiarsDiceBid(current.Quantity + 1, new DieValue(1));
        if (next.Quantity > totalDice)
            throw new LiarsDiceRuleException("The current bid cannot be raised; it must be challenged.");
        return next;
    }
}
