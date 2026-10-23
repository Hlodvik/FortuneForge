using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Server.Games.Hearts;

internal static class PaidHeartsProjection
{
    public static PaidHeartsTableResponse Table(PaidHeartsMatch table, string userId)
    {
        var viewer = table.Players.Single(player => player.UserId == userId && player.IsAccountBacked);
        var state = table.State ?? throw new InvalidOperationException("This table has not dealt a hand.");
        var round = state.Round;
        var legal = table.Status == "match" && !viewer.HasLeft && round.Phase == HeartsPhase.Playing && round.Turn == viewer.Seat
            ? HeartsEngine.LegalCards(round, viewer.Seat) : [];
        var score = round.Scores.ToDictionary(value => value.Seat, value => value.Points);
        var match = new HeartsMatchResponse(
            Guid.Parse(table.Id), 100, state.RoundNumber, round.Phase.ToString().ToLowerInvariant(),
            state.PassDirection.ToString().ToLowerInvariant(), Seat(round.Turn), Seat(viewer.Seat),
            table.Status == "match" && !viewer.HasLeft && round.Phase == HeartsPhase.Playing && round.Turn == viewer.Seat,
            table.Status == "match" && table.ScheduledSeat is { } scheduled &&
                table.Players.Any(player => player.Seat == scheduled && (!player.IsAccountBacked || player.HasLeft)),
            round.HeartsBroken, round.SubmittedPasses.Select(pass => Seat(pass.Seat)).ToArray(),
            table.Players.OrderBy(player => player.Seat).Select(player => new HeartsPlayerResponse(
                Seat(player.Seat), DisplayName(player), round.HandFor(player.Seat).Count,
                score[player.Seat], state.Score.For(player.Seat),
                round.SubmittedPasses.Any(pass => pass.Seat == player.Seat))).ToArray(),
            round.HandFor(viewer.Seat).Select(Card).ToArray(), legal.Select(Card).ToArray(),
            new HeartsTrickResponse(Seat(round.CurrentTrick.Leader), round.CurrentTrick.Plays.Select(play =>
                new HeartsTrickPlayResponse(Seat(play.Seat), Card(play.Card))).ToArray()),
            table.RecentTrick is { } recent ? new HeartsRecentTrickResponse(
                recent.Number, Seat(recent.Leader), Seat(recent.Winner), recent.Plays.Sum(play => HeartsRules.PointValue(play.Card)),
                recent.Plays.Select(play => new HeartsTrickPlayResponse(Seat(play.Seat), Card(play.Card))).ToArray()) : null,
            round.CompletedTricks.Select(trick => new HeartsCompletedTrickResponse(
                trick.Number, Seat(trick.Leader), Seat(trick.Winner), trick.Plays.Sum(play => HeartsRules.PointValue(play.Card)))).ToArray(),
            new HeartsScoreResponse(state.Score.North, state.Score.East, state.Score.South, state.Score.West),
            new HeartsScoreResponse(score[PlayerSeat.North], score[PlayerSeat.East], score[PlayerSeat.South], score[PlayerSeat.West]),
            "standard", table.Status == "result" && table.Winners.Count > 0 ? Seat(table.Winners[0]) : null, table.Message);
        return new PaidHeartsTableResponse(match, table.EntryCents / 100m, table.PotCents / 100m,
            viewer.PayoutCents / 100m, table.Winners.Select(Seat).ToArray(),
            table.Players.OrderBy(player => player.Seat).Select(player => new PaidHeartsPlayerResponse(
                Seat(player.Seat), DisplayName(player), player.UserId == userId, player.HasLeft,
                table.EntryCents / 100m, player.PayoutCents / 100m)).ToArray(),
            table.Status == "match" && table.ScheduledSeat == viewer.Seat && !viewer.HasLeft
                ? table.NextActionAtUtc : null, table.CompletedAtUtc);
    }

    public static string Seat(PlayerSeat seat) => seat.ToString().ToLowerInvariant();
    private static string DisplayName(PaidHeartsParticipant player) =>
        player.IsAccountBacked ? player.DisplayName : $"House {player.Seat}";
    private static HeartsCardResponse Card(PlayingCard card) => new(card.Code,
        card.Rank.ToString().ToLowerInvariant(), card.Suit.ToString().ToLowerInvariant(),
        $"{card.Code.Split('|')[0]}{card.Suit switch { CardSuit.Clubs => "♣", CardSuit.Diamonds => "♦", CardSuit.Hearts => "♥", _ => "♠" }}");
}
