using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Games.Hearts;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class PaidHeartsSettlementTests
{
    [Fact]
    public void Lowest_scores_share_full_pot_and_indivisible_cents_ignore_seat_order()
    {
        var players = Enum.GetValues<PlayerSeat>().Select((seat, index) => new PaidHeartsParticipant
            { Seat = seat, UserId = $"player-{index}" }).ToArray();
        var scores = new HeartsMatchScore(20, 20, 20, 100);
        var result = PaidHeartsSettlement.Split("server-match", 400, players, scores);

        Assert.Equal(400, result.Values.Sum());
        Assert.Equal(3, result.Count);
        Assert.Equal(new long[] { 133, 133, 134 }, result.Values.Order());
        var permuted = players.Take(3).Select((player, index) => new PaidHeartsParticipant
            { UserId = player.UserId, Seat = (PlayerSeat)(2 - index) }).Append(players[3]).ToArray();
        var again = PaidHeartsSettlement.Split("server-match", 400, permuted, scores);
        Assert.All(result, pair => Assert.Equal(pair.Value, again[pair.Key]));
    }

    [Fact]
    public void Four_way_tie_returns_equal_entries_without_fee()
    {
        var players = Enum.GetValues<PlayerSeat>().Select(seat => new PaidHeartsParticipant { Seat = seat, UserId = seat.ToString() });
        var result = PaidHeartsSettlement.Split("match", 404, players, new(100, 100, 100, 100));
        Assert.All(result, pair => Assert.Equal(101, pair.Value));
    }

    [Fact]
    public void Shooting_the_moon_uses_adjusted_points_for_chip_winner()
    {
        var round = HeartsRules.ApplyShootingTheMoon([
            new(PlayerSeat.North, 26), new(PlayerSeat.East, 0), new(PlayerSeat.South, 0), new(PlayerSeat.West, 0)]);
        var score = new HeartsMatchScore(0, 0, 0, 0).Add(round);
        var players = Enum.GetValues<PlayerSeat>().Select(seat => new PaidHeartsParticipant { Seat = seat, UserId = seat.ToString() });
        var result = PaidHeartsSettlement.Split("match", 400, players, score);
        Assert.Equal(400, result["North"]);
        Assert.Single(result);
    }

    [Fact]
    public void Paid_entry_request_has_no_seed_difficulty_or_target_override()
    {
        Assert.Equal(["EntryCredits"], typeof(JoinPaidHeartsRequest).GetProperties().Select(property => property.Name));
    }
}
