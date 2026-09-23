using System.Text.Json.Serialization;

namespace FortuneForge.Games.Solitaire;

public static class SolitaireCommandTypes
{
    public const string Draw = "draw";
    public const string Flip = "flip";
    public const string Move = "move";
    public const string Undo = "undo";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Submit = "submit";
    public const string IntegrityFailure = "integrity-failure";
    public const string AcknowledgeWarning = "acknowledge-warning";
}

public sealed record SolitairePileReference(string Zone, int Index);

public sealed record SolitaireCommandRequest(
    string Type,
    int ExpectedVersion,
    SolitairePileReference? From,
    int? StartIndex,
    SolitairePileReference? To,
    int? Column);

public sealed record SolitaireCardResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Suit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rank,
    bool IsFaceUp);

public sealed record SolitaireGameResponse(
    IReadOnlyList<SolitaireCardResponse> Stock,
    IReadOnlyList<SolitaireCardResponse> Waste,
    IReadOnlyList<IReadOnlyList<SolitaireCardResponse>> Foundations,
    IReadOnlyList<IReadOnlyList<SolitaireCardResponse>> Tableau,
    int DrawCount,
    int Score,
    int Moves,
    string Message);

public sealed record SolitaireCard(string Id, string Suit, int Rank, bool FaceUp);

public sealed record SolitaireGameState(
    IReadOnlyList<SolitaireCard> Stock,
    IReadOnlyList<SolitaireCard> Waste,
    IReadOnlyList<IReadOnlyList<SolitaireCard>> Foundations,
    IReadOnlyList<IReadOnlyList<SolitaireCard>> Tableau,
    int Score,
    int Moves,
    uint Seed,
    string Message)
{
    public int DrawCount { get; init; } = 3;
}

public sealed class SolitaireIllegalMoveException(string message) : Exception(message);

public static class SolitaireRules
{
    public static int ValidateDrawCount(int drawCount) => drawCount is 1 or 3
        ? drawCount
        : throw new ArgumentOutOfRangeException(nameof(drawCount), "Solitaire supports draw 1 or draw 3.");
}
