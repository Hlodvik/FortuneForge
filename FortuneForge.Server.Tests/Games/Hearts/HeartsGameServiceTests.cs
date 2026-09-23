using FortuneForge.Server.Games.Hearts;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class HeartsGameServiceTests
{
    [Fact]
    public void Started_match_is_private_and_deals_the_human_thirteen_cards()
    {
        var service = new HeartsGameService();
        var match = service.Start("player-a", new StartHeartsMatchRequest(1931, 50, "relaxed"));

        Assert.Equal(50, match.TargetScore);
        Assert.Equal("relaxed", match.Difficulty);
        Assert.Equal("passing", match.Phase);
        Assert.Equal(13, match.Hand.Count);
        Assert.Throws<HeartsAccessException>(() => service.Get("player-b", match.MatchId));
    }

    [Fact]
    public void Passing_cards_advances_bot_turns_until_a_human_play_is_available()
    {
        var service = new HeartsGameService();
        var started = service.Start("player-a", new StartHeartsMatchRequest(1942, 50));
        var next = service.Pass("player-a", started.MatchId, started.Hand.Take(3).Select(card => card.Code).ToArray());

        for (var action = 0; next.BotsThinking && action < 12; action++)
            next = service.Advance("player-a", started.MatchId);

        Assert.Equal("playing", next.Phase);
        Assert.True(next.YourTurn);
        Assert.NotEmpty(next.LegalCards);

        var afterPlay = service.PlayCard("player-a", started.MatchId, next.LegalCards[0].Code);
        Assert.True(afterPlay.BotsThinking || afterPlay.YourTurn || afterPlay.Phase == "complete");
    }

    [Fact]
    public void Started_match_rejects_an_unknown_bot_difficulty()
    {
        var service = new HeartsGameService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Start("player-a", new StartHeartsMatchRequest(1931, 50, "impossible")));
    }
}
