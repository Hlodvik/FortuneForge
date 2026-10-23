using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Tests;

public sealed class HeartsGameplayRegressionTests
{
    [Theory]
    [InlineData(HeartsPassDirection.Left, 1)]
    [InlineData(HeartsPassDirection.Right, 3)]
    [InlineData(HeartsPassDirection.Across, 2)]
    public void Passing_delivers_exactly_the_selected_cards_to_the_correct_recipient(HeartsPassDirection direction, int offset)
    {
        var state = HeartsEngine.Start(1931, direction);
        var original = state.Hands.ToDictionary(hand => hand.Seat, hand => hand.Cards.ToArray());
        var passes = original.ToDictionary(pair => pair.Key, pair => pair.Value.Take(3).ToArray());
        foreach (var seat in Enum.GetValues<PlayerSeat>())
            state = HeartsEngine.Apply(state, new PassHeartsCards(seat, passes[seat])).State;

        foreach (var seat in Enum.GetValues<PlayerSeat>())
        {
            var sender = seat.Advance(4 - offset);
            var expected = original[seat].Except(passes[seat]).Concat(passes[sender]).ToHashSet();
            Assert.Equal(13, state.HandFor(seat).Count);
            Assert.True(expected.SetEquals(state.HandFor(seat)));
        }
        Assert.Equal(HeartsPhase.Playing, state.Phase);
        Assert.Equal(state.Hands.Single(hand => hand.Cards.Contains(HeartsRules.OpeningCard)).Seat, state.Turn);
        Assert.Equal([HeartsRules.OpeningCard], HeartsEngine.LegalCards(state, state.Turn));
    }

    [Fact]
    public void Hold_deals_without_passing_and_rejects_a_pass_without_changing_hands()
    {
        var state = HeartsEngine.Start(1931, HeartsPassDirection.Hold);
        var original = state.Hands.SelectMany(hand => hand.Cards).ToArray();
        Assert.Equal(HeartsPhase.Playing, state.Phase);
        Assert.Empty(state.SubmittedPasses);
        Assert.Throws<HeartsRuleException>(() => HeartsEngine.Apply(state,
            new PassHeartsCards(PlayerSeat.North, state.HandFor(PlayerSeat.North).Take(3).ToArray())));
        Assert.Equal(original, state.Hands.SelectMany(hand => hand.Cards));
    }

    [Fact]
    public void Completed_rounds_rotate_left_right_across_hold_then_left()
    {
        var state = HeartsMatchEngine.Start(77, targetScore: 10_000);
        HeartsPassDirection[] expected = [HeartsPassDirection.Left, HeartsPassDirection.Right,
            HeartsPassDirection.Across, HeartsPassDirection.Hold, HeartsPassDirection.Left];
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(index + 1, state.RoundNumber);
            Assert.Equal(expected[index], state.PassDirection);
            Assert.Equal(expected[index], state.Round.PassDirection);
            Assert.Equal(expected[index] == HeartsPassDirection.Hold ? HeartsPhase.Playing : HeartsPhase.Passing, state.Phase);
            if (index == expected.Length - 1) break;
            state = CompleteMatchRound(state);
            Assert.Null(state.Winner);
            state = HeartsMatchEngine.StartNextRound(state, (uint)(100 + index));
        }
    }

    [Theory]
    [InlineData(CardRank.Queen, CardSuit.Spades)]
    [InlineData(CardRank.Two, CardSuit.Hearts)]
    public void First_trick_all_point_hand_has_a_legal_fallback_when_void_in_clubs(CardRank rank, CardSuit suit)
    {
        PlayingCard[] hand = [new(CardRank.Queen, CardSuit.Spades), new(CardRank.Two, CardSuit.Hearts),
            new(CardRank.Three, CardSuit.Hearts)];
        var state = PartialState(PlayerSeat.East, hand,
            new TrickState(PlayerSeat.North, [new(PlayerSeat.North, HeartsRules.OpeningCard)]));
        Assert.Equal(hand, HeartsEngine.LegalCards(state, PlayerSeat.East));
        var selected = new PlayingCard(rank, suit);
        var next = HeartsEngine.Apply(state, new PlayHeartsCard(PlayerSeat.East, selected)).State;
        Assert.DoesNotContain(selected, next.HandFor(PlayerSeat.East));
        Assert.Equal(suit == CardSuit.Hearts, next.HeartsBroken);
    }

    [Fact]
    public void Hearts_only_hand_may_lead_before_hearts_are_broken()
    {
        PlayingCard[] hand = [new(CardRank.Two, CardSuit.Hearts), new(CardRank.King, CardSuit.Hearts)];
        var state = PartialState(PlayerSeat.East, hand, new TrickState(PlayerSeat.East, []), completedTricks: 1);
        Assert.False(state.HeartsBroken);
        Assert.Equal(hand, HeartsEngine.LegalCards(state, PlayerSeat.East));
        var next = HeartsEngine.Apply(state, new PlayHeartsCard(PlayerSeat.East, hand[1])).State;
        Assert.True(next.HeartsBroken);
        Assert.Equal(hand[1], Assert.Single(next.CurrentTrick.Plays).Card);
    }

    [Theory]
    [InlineData(1u, HeartsPassDirection.Left)]
    [InlineData(77u, HeartsPassDirection.Right)]
    [InlineData(123u, HeartsPassDirection.Across)]
    [InlineData(1931u, HeartsPassDirection.Hold)]
    [InlineData(1942u, HeartsPassDirection.Left)]
    public void Seeded_rounds_conserve_every_card_and_score_the_actual_tricks(uint seed, HeartsPassDirection direction)
    {
        var state = HeartsEngine.Start(seed, direction);
        var deck = state.Hands.SelectMany(hand => hand.Cards).ToHashSet();
        if (state.Phase == HeartsPhase.Passing)
            foreach (var seat in Enum.GetValues<PlayerSeat>())
                state = HeartsEngine.Apply(state, new PassHeartsCards(seat, state.HandFor(seat).Take(3).ToArray())).State;
        AssertConservation(state, deck);
        while (state.Phase == HeartsPhase.Playing)
        {
            var legal = HeartsEngine.LegalCards(state, state.Turn);
            Assert.NotEmpty(legal);
            state = HeartsEngine.Apply(state, new PlayHeartsCard(state.Turn, legal[0])).State;
            AssertConservation(state, deck);
        }

        Assert.Equal(13, state.CompletedTricks.Count);
        Assert.All(state.Hands, hand => Assert.Empty(hand.Cards));
        var actualPoints = Enum.GetValues<PlayerSeat>().Select(seat => new HeartsScore(seat,
            state.CompletedTricks.Where(trick => trick.Winner == seat)
                .Sum(trick => trick.Plays.Sum(play => HeartsRules.PointValue(play.Card))))).ToArray();
        Assert.Equal(26, actualPoints.Sum(score => score.Points));
        Assert.Equal(HeartsRules.ApplyShootingTheMoon(actualPoints), state.Scores);
        Assert.Equal(actualPoints.Any(score => score.Points == 26) ? 78 : 26, state.Scores.Sum(score => score.Points));
    }

    private static void AssertConservation(HeartsState state, HashSet<PlayingCard> expected)
    {
        var all = state.Hands.SelectMany(hand => hand.Cards)
            .Concat(state.CompletedTricks.SelectMany(trick => trick.Plays.Select(play => play.Card)))
            .Concat(state.CurrentTrick.Plays.Select(play => play.Card)).ToArray();
        Assert.Equal(52, all.Length);
        Assert.Equal(52, all.Distinct().Count());
        Assert.True(expected.SetEquals(all));
    }

    private static HeartsMatchState CompleteMatchRound(HeartsMatchState state)
    {
        if (state.Phase == HeartsPhase.Passing)
            foreach (var seat in Enum.GetValues<PlayerSeat>())
                state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat, state.Round.HandFor(seat).Take(3).ToArray())).State;
        while (state.Phase == HeartsPhase.Playing)
            state = HeartsMatchEngine.Apply(state,
                new PlayHeartsCard(state.Turn, HeartsEngine.LegalCards(state.Round, state.Turn)[0])).State;
        return state;
    }

    private static HeartsState PartialState(PlayerSeat seat, IReadOnlyList<PlayingCard> hand, TrickState trick, int completedTricks = 0) =>
        new(1, seat, HeartsPhase.Playing,
            Enum.GetValues<PlayerSeat>().Select(candidate => new PlayerHand(candidate, candidate == seat ? hand : [])).ToArray(),
            trick, Enumerable.Range(1, completedTricks).Select(number => new CompletedTrick(number, PlayerSeat.North, PlayerSeat.North, [])).ToArray(),
            Enum.GetValues<PlayerSeat>().Select(candidate => new HeartsScore(candidate, 0)).ToArray(), false);
}
