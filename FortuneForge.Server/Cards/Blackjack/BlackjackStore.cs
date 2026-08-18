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

internal sealed class DemoBlackjackService
{
    private const long DemoStartingBalanceCents = 1_000_000;
    private const int MaximumSessions = 5_000;
    private const int MaximumGamesPerSession = 50;
    private readonly Lock sync = new();
    private readonly Dictionary<string, DemoSession> sessions = new(StringComparer.Ordinal);

    public BlackjackGameResponse Start(
        string sessionId,
        BlackjackStartRequest request,
        string idempotencyKey)
    {
        ValidateSessionId(sessionId);
        BlackjackService.ValidateIdempotencyKey(idempotencyKey);
        var wagerCents = BlackjackMoney.ToWagerCents(request.Wager);
        var sessionKey = FirestoreBlackjackStore.CreateLookupKey(sessionId);
        var gameId = FirestoreBlackjackStore.CreateLookupKey($"demo\n{sessionKey}\n{idempotencyKey}");
        lock (sync)
        {
            var session = Session(sessionKey);
            if (session.Games.TryGetValue(gameId, out var existing))
            {
                if (existing.WagerCents != wagerCents)
                {
                    throw new BlackjackConflictException(
                        "This Idempotency-Key was already used with a different wager.");
                }
                return Response(session, existing);
            }
            if (session.BalanceCents < wagerCents)
            {
                throw new BlackjackInsufficientCreditsException(session.BalanceCents, wagerCents);
            }

            if (session.Games.Count >= MaximumGamesPerSession)
            {
                var oldestCompleted = session.Games.Values
                    .Where(game => game.Status == BlackjackStatuses.Completed)
                    .MinBy(game => game.UpdatedAtUtc);
                if (oldestCompleted is null)
                {
                    throw new BlackjackConflictException(
                        "Finish an active demo hand before opening another table.");
                }
                session.Games.Remove(oldestCompleted.GameId);
            }

            session.BalanceCents -= wagerCents;
            var game = BlackjackRules.Deal(
                gameId,
                sessionKey,
                wagerCents,
                BlackjackRules.CreateShuffledDeck(),
                DateTime.UtcNow);
            if (game.Status == BlackjackStatuses.Completed)
            {
                session.BalanceCents += game.PayoutCents;
            }
            session.Games.Add(gameId, game);
            return Response(session, game);
        }
    }

    public BlackjackGameResponse Get(string sessionId, string gameId)
    {
        ValidateSessionId(sessionId);
        var sessionKey = FirestoreBlackjackStore.CreateLookupKey(sessionId);
        lock (sync)
        {
            var session = Session(sessionKey);
            return session.Games.TryGetValue(gameId, out var game)
                ? Response(session, game)
                : throw new BlackjackNotFoundException();
        }
    }

    public BlackjackGameResponse Act(
        string sessionId,
        string gameId,
        BlackjackActionRequest request,
        string idempotencyKey)
    {
        ValidateSessionId(sessionId);
        BlackjackService.ValidateIdempotencyKey(idempotencyKey);
        var sessionKey = FirestoreBlackjackStore.CreateLookupKey(sessionId);
        lock (sync)
        {
            var session = Session(sessionKey);
            if (!session.Games.TryGetValue(gameId, out var game))
            {
                throw new BlackjackNotFoundException();
            }
            var normalizedAction = request.Action.Trim().ToLowerInvariant();
            if (session.Actions.TryGetValue(idempotencyKey, out var priorAction))
            {
                if (!string.Equals(priorAction.GameId, gameId, StringComparison.Ordinal) ||
                    !string.Equals(priorAction.Action, normalizedAction, StringComparison.Ordinal))
                {
                    throw new BlackjackConflictException(
                        "This Idempotency-Key was already used with a different Blackjack action.");
                }
                return Response(session, game);
            }
            if (request.ExpectedVersion != game.Version)
            {
                throw new BlackjackConflictException(
                    "The Blackjack hand changed. Reload it before choosing another action.");
            }
            var isDouble = normalizedAction == BlackjackActions.Double;
            if (isDouble && session.BalanceCents < game.WagerCents)
            {
                throw new BlackjackInsufficientCreditsException(session.BalanceCents, game.WagerCents);
            }
            if (isDouble)
            {
                session.BalanceCents -= game.WagerCents;
            }
            var updated = BlackjackRules.ApplyAction(game, normalizedAction, DateTime.UtcNow);
            if (updated.Status == BlackjackStatuses.Completed)
            {
                session.BalanceCents += updated.PayoutCents;
            }
            session.Games[gameId] = updated;
            session.Actions.Add(idempotencyKey, (gameId, normalizedAction));
            return Response(session, updated);
        }
    }

    private DemoSession Session(string sessionKey)
    {
        if (!sessions.TryGetValue(sessionKey, out var session))
        {
            if (sessions.Count >= MaximumSessions)
            {
                var oldest = sessions.MinBy(pair => pair.Value.LastSeenUtc);
                sessions.Remove(oldest.Key);
            }
            session = new DemoSession();
            sessions.Add(sessionKey, session);
        }
        session.LastSeenUtc = DateTime.UtcNow;
        return session;
    }

    private static BlackjackGameResponse Response(DemoSession session, BlackjackGame game) =>
        BlackjackService.ToResponse(
            new BlackjackStoreResult(game, session.BalanceCents),
            revealDealer: game.Status == BlackjackStatuses.Completed);

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length is < 16 or > 128)
        {
            throw new ArgumentException(
                "X-Demo-Session-Id must contain 16 to 128 characters.",
                nameof(sessionId));
        }
    }

    private sealed class DemoSession
    {
        public long BalanceCents { get; set; } = DemoStartingBalanceCents;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, BlackjackGame> Games { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (string GameId, string Action)> Actions { get; } =
            new(StringComparer.Ordinal);
    }
}
