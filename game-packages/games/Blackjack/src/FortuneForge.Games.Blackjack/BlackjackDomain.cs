namespace FortuneForge.Games.Blackjack;

public static class BlackjackActions
{
    public const string Hit = "hit";
    public const string Stand = "stand";
    public const string Double = "double";
    public const string Split = "split";
    public const string Surrender = "surrender";
    public const string Insurance = "insurance";
    public const string DeclineInsurance = "decline-insurance";
}

public static class BlackjackStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
}

public static class BlackjackOutcomes
{
    public const string PlayerBlackjack = "player-blackjack";
    public const string DealerBlackjack = "dealer-blackjack";
    public const string PlayerBust = "player-bust";
    public const string PlayerWin = "player-win";
    public const string DealerWin = "dealer-win";
    public const string Push = "push";
    public const string PlayerSurrender = "player-surrender";
}

public sealed record BlackjackGame(
    string GameId,
    string UserId,
    long WagerCents,
    long TotalWagerCents,
    long PayoutCents,
    string Status,
    string? Outcome,
    IReadOnlyList<string> Deck,
    int NextCardIndex,
    IReadOnlyList<string> PlayerCards,
    IReadOnlyList<string> DealerCards,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BlackjackHandValue(int Score, bool Soft, bool Blackjack, bool Bust);

public sealed class BlackjackConflictException(string message) : Exception(message);
