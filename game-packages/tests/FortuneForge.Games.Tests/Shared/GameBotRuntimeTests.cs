using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Solitaire;
using FortuneForge.Games.TexasHoldem;
using Xunit;

namespace FortuneForge.Games.Tests;

public sealed class GameBotRuntimeTests
{
    private static readonly CardBotGameOptions Options = new();

    [Fact]
    public void IdentitiesAreDeterministicAndContainNoHostState()
    {
        var factory = new BotIdentityFactory();
        var first = factory.Create(42, 5, CardBotSkillLevels.Strong);
        var second = factory.Create(42, 5, CardBotSkillLevels.Strong);

        Assert.Equal(first, second);
        Assert.Equal(5, first.Select(value => value.DisplayName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BlackjackAgentReturnsOnlyLegalActions()
    {
        var observation = new BlackjackBotObservation(
            ["10|clubs", "6|hearts"],
            "10|spades",
            [BlackjackActions.Hit, BlackjackActions.Stand]);

        foreach (var skill in new[] { 2, 3, 4 })
        {
            var action = new BlackjackBotAgent().Choose(observation, skill, 123, 7, Options);
            Assert.Contains(action, observation.LegalActions);
        }
    }

    [Fact]
    public void HoldemAgentIsDeterministicAndBounded()
    {
        var observation = new TexasHoldemBotObservation(
            ["A|spades", "K|spades"],
            ["Q|spades", "J|spades", "2|clubs"],
            120,
            20,
            900,
            60,
            910,
            [HoldemActions.Fold, HoldemActions.Call, HoldemActions.Raise]);

        foreach (var skill in new[] { 2, 3, 4 })
        {
            var agent = new TexasHoldemBotAgent();
            var first = agent.Choose(observation, skill, 999, 4, Options);
            var second = agent.Choose(observation, skill, 999, 4, Options);
            Assert.Equal(first, second);
            Assert.Contains(first.Action, observation.LegalActions);
            if (first.RaiseTo is { } raise) Assert.InRange(raise, 60, 910);
        }
    }

    [Fact]
    public void SolitaireAgentProducesAnEngineLegalMoveAtEverySkill()
    {
        var game = SolitaireEngine.CreateGame(123, 3);
        foreach (var skill in new[] { 2, 3, 4 })
        {
            var command = new SolitaireBotAgent().Choose(game, 1, skill, 456, Options);
            var result = SolitaireEngine.Apply(game, command);
            Assert.Equal(1, result.Moves);
        }
    }
}
