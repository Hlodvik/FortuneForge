using FortuneForge.Server.Games.LiarsDice;
using Xunit;

namespace FortuneForge.Server.Tests.Games.LiarsDice;

public sealed class LiarsDiceGameServiceTests
{
    [Fact]
    public void Started_bot_match_is_private_and_deals_the_human_a_hand()
    {
        var service = new LiarsDiceGameService();
        var match = service.Start("player-a", new StartLiarsDiceMatchRequest(77, 3));

        Assert.Equal("bidding", match.Phase);
        Assert.Equal(3, match.Hand.Count);
        Assert.Throws<LiarsDiceAccessException>(() => service.Get("player-b", match.MatchId));
    }

    [Fact]
    public void Human_can_make_a_legal_bid_after_opening_bots_finish()
    {
        var service = new LiarsDiceGameService();
        var match = service.Start("player-a", new StartLiarsDiceMatchRequest(98, 3));

        for (var attempt = 0; match.CurrentPlayerId != "you" && attempt < 12; attempt++)
            match = service.Advance("player-a", match.MatchId);

        Assert.Equal("you", match.CurrentPlayerId);
        var quantity = match.CurrentBid?.Quantity ?? 1;
        var face = match.CurrentBid is null ? 1 : match.CurrentBid.Face < 6 ? match.CurrentBid.Face + 1 : 1;
        var bid = service.Bid("player-a", match.MatchId, new PlaceLiarsDiceBidRequest(quantity, face));

        Assert.True(bid.CurrentBid is not null || bid.Phase == "resolved");
    }
}
