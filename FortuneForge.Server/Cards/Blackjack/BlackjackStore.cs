namespace FortuneForge.Server.Cards.Blackjack;

internal interface IBlackjackStore
{
    Task<BlackjackStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        long wagerCents,
        IReadOnlyList<string> shuffledDeck,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<BlackjackStoreResult?> GetAsync(
        string userId,
        string gameId,
        CancellationToken cancellationToken);

    Task<BlackjackStoreResult> ActAsync(
        string userId,
        string gameId,
        string idempotencyKey,
        int expectedVersion,
        string action,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class BlackjackService(IBlackjackStore store)
{
    public async Task<BlackjackGameResponse> StartAsync(
        string userId,
        BlackjackStartRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        var wagerCents = BlackjackMoney.ToWagerCents(request.Wager);
        var result = await store.StartAsync(
            userId,
            idempotencyKey,
            wagerCents,
            BlackjackRules.CreateShuffledDeck(),
            DateTime.UtcNow,
            cancellationToken);
        return ToResponse(result, revealDealer: result.Game.Status == BlackjackStatuses.Completed);
    }

    public async Task<BlackjackGameResponse> GetAsync(
        string userId,
        string gameId,
        CancellationToken cancellationToken)
    {
        ValidateGameId(gameId);
        var result = await store.GetAsync(userId, gameId, cancellationToken)
            ?? throw new BlackjackNotFoundException();
        return ToResponse(result, revealDealer: result.Game.Status == BlackjackStatuses.Completed);
    }

    public async Task<BlackjackGameResponse> ActAsync(
        string userId,
        string gameId,
        BlackjackActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateGameId(gameId);
        ValidateIdempotencyKey(idempotencyKey);
        if (request.ExpectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ExpectedVersion),
                "The Blackjack game version must be positive.");
        }
        if (string.IsNullOrWhiteSpace(request.Action))
        {
            throw new ArgumentException("Choose hit, stand, or double.", nameof(request.Action));
        }
        var action = request.Action.Trim().ToLowerInvariant();

        var result = await store.ActAsync(
            userId,
            gameId,
            idempotencyKey,
            request.ExpectedVersion,
            action,
            DateTime.UtcNow,
            cancellationToken);
        return ToResponse(result, revealDealer: result.Game.Status == BlackjackStatuses.Completed);
    }

    internal static BlackjackGameResponse ToResponse(
        BlackjackStoreResult result,
        bool revealDealer)
    {
        var game = result.Game;
        var playerValue = BlackjackRules.Score(game.PlayerCards);
        var dealerCards = revealDealer
            ? game.DealerCards.Select(ToCardResponse).ToArray()
            : game.DealerCards.Select((card, index) =>
                index == 1
                    ? new BlackjackCardResponse(null, null, true)
                    : ToCardResponse(card)).ToArray();
        var dealerValue = revealDealer ? BlackjackRules.Score(game.DealerCards) : null;
        var active = game.Status == BlackjackStatuses.Active;
        return new BlackjackGameResponse(
            game.GameId,
            game.Status,
            game.Outcome,
            MessageFor(game.Outcome, active),
            BlackjackMoney.ToRand(game.WagerCents),
            BlackjackMoney.ToRand(game.TotalWagerCents),
            BlackjackMoney.ToRand(game.PayoutCents),
            BlackjackMoney.ToRand(result.BalanceCents),
            new BlackjackHandResponse(
                game.PlayerCards.Select(ToCardResponse).ToArray(),
                playerValue.Score,
                playerValue.Soft,
                playerValue.Blackjack,
                playerValue.Bust),
            new BlackjackHandResponse(
                dealerCards,
                dealerValue?.Score,
                dealerValue?.Soft ?? false,
                dealerValue?.Blackjack ?? false,
                dealerValue?.Bust ?? false),
            active,
            active,
            active && game.PlayerCards.Count == 2,
            game.Version,
            game.CreatedAtUtc,
            game.UpdatedAtUtc);
    }

    private static BlackjackCardResponse ToCardResponse(string code)
    {
        var card = BlackjackRules.ParseCard(code);
        return new BlackjackCardResponse(card.Rank, card.Suit, false);
    }

    private static string MessageFor(string? outcome, bool active) =>
        active ? "Choose hit, stand, or double." : outcome switch
        {
            BlackjackOutcomes.PlayerBlackjack => "Blackjack! Paid 3 to 2.",
            BlackjackOutcomes.DealerBlackjack => "Dealer has blackjack.",
            BlackjackOutcomes.PlayerBust => "Bust. The dealer wins.",
            BlackjackOutcomes.PlayerWin => "You beat the dealer.",
            BlackjackOutcomes.DealerWin => "The dealer wins.",
            BlackjackOutcomes.Push => "Push. Your wager was returned.",
            _ => "The hand is complete."
        };

    internal static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length is < 16 or > 128 ||
            idempotencyKey.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.",
                nameof(idempotencyKey));
        }
    }

    private static void ValidateGameId(string gameId)
    {
        if (gameId.Length != 64 || gameId.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("The Blackjack game identifier is invalid.", nameof(gameId));
        }
    }
}
