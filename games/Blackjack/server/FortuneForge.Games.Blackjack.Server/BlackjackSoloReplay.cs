namespace FortuneForge.Server.Cards.Blackjack;

/// <summary>Reconstructs legal solo actions from the saved deck, cards and committed wager.</summary>
public static class BlackjackSoloReplay
{
    public const string Version = "blackjack-solo-deck-replay-v1";

    public static bool Verify(BlackjackGame game)
    {
        try
        {
            if (game.WagerCents <= 0 || game.CreatedAtUtc.Kind != DateTimeKind.Utc ||
                game.UpdatedAtUtc.Kind != DateTimeKind.Utc || game.UpdatedAtUtc < game.CreatedAtUtc ||
                game.PlayerCards.Count < 2 || game.DealerCards.Count < 2) return false;
            var replay = BlackjackRules.Deal(game.GameId, game.UserId, game.WagerCents, game.Deck, game.CreatedAtUtc);
            if (game.TotalWagerCents == checked(game.WagerCents * 2))
                replay = BlackjackRules.ApplyAction(replay, BlackjackActions.Double, game.UpdatedAtUtc);
            else
            {
                if (game.TotalWagerCents != game.WagerCents) return false;
                for (var index = 2; index < game.PlayerCards.Count; index++)
                    replay = BlackjackRules.ApplyAction(replay, BlackjackActions.Hit, game.UpdatedAtUtc);
                if (game.Status == BlackjackStatuses.Completed && replay.Status == BlackjackStatuses.Active)
                    replay = BlackjackRules.ApplyAction(replay, BlackjackActions.Stand, game.UpdatedAtUtc);
            }
            return replay.Status == game.Status && replay.Outcome == game.Outcome && replay.Version == game.Version &&
                replay.TotalWagerCents == game.TotalWagerCents && replay.PayoutCents == game.PayoutCents &&
                replay.NextCardIndex == game.NextCardIndex && replay.PlayerCards.SequenceEqual(game.PlayerCards) &&
                replay.DealerCards.SequenceEqual(game.DealerCards);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException or BlackjackConflictException)
        { return false; }
    }
}
