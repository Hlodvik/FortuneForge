using System.Text.Json.Serialization;

namespace FortuneForge.Server.Cards.Bots;

/// <summary>
/// Frozen server/client contract for the first card-bot practice milestone.
/// Additive changes may retain v1; renamed/removed fields require v2.
/// </summary>
public static class CardBotContract
{
    public const string Version = "cards.bot.v2";
    public const int CommandVersion = 1;
}

public static class CardBotGames
{
    public const string Blackjack = "blackjack";
    public const string Solitaire = "solitaire";
    public const string TexasHoldem = "texas-holdem";
}

public sealed record CardBotSeatDto(
    string SeatId,
    string DisplayName,
    int Seat,
    string Status);

public sealed record CardBotQueueDto(
    string QueueId,
    string Game,
    int RequiredPlayers,
    IReadOnlyList<CardBotSeatDto> Seats);

public sealed record CardBotJoinRequest(
    int PlayerCount,
    int Difficulty,
    string IdempotencyKey);

public sealed record CardBotCommandRequest(
    string Type,
    int ExpectedVersion,
    string IdempotencyKey,
    IReadOnlyDictionary<string, string>? Arguments = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> SafeArguments { get; } =
        Arguments ?? new Dictionary<string, string>();
}

internal sealed record CardBotDomainCommand(
    string ContractVersion,
    int CommandVersion,
    string Game,
    string MatchId,
    string ActorSeatId,
    string Type,
    int ExpectedVersion,
    string IdempotencyKey,
    IReadOnlyDictionary<string, string> Arguments);

internal sealed record CardBotDomainEvent(
    string ContractVersion,
    string Game,
    string MatchId,
    int Version,
    string Type,
    string ActorSeatId,
    DateTime OccurredAtUtc,
    IReadOnlyDictionary<string, string> PublicData);

public sealed record CardBotPublicEventDto(
    string ContractVersion,
    string Game,
    string MatchId,
    int Version,
    string Type,
    string ActorSeatId,
    string ActorDisplayName,
    DateTime OccurredAtUtc,
    IReadOnlyDictionary<string, string> PublicData);

public interface ICardBotGameRunner
{
    string Game { get; }
    Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

internal static class CardBotContractValidation
{
    public static void ValidateJoin(CardBotJoinRequest request, int minimum, int maximum)
    {
        if (request.PlayerCount < minimum || request.PlayerCount > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PlayerCount),
                $"PlayerCount must be from {minimum} through {maximum}.");
        }
        CardBotSkillLevels.Validate(request.Difficulty);
        ValidateIdempotencyKey(request.IdempotencyKey);
    }

    public static void ValidateCommand(CardBotCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new ArgumentException("A command type is required.", nameof(request.Type));
        }
        if (request.ExpectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ExpectedVersion),
                "ExpectedVersion must be positive.");
        }
        ValidateIdempotencyKey(request.IdempotencyKey);
    }

    public static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length is < 16 or > 128 ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "IdempotencyKey must contain 16 to 128 letters, digits, hyphens, or underscores.",
                nameof(value));
        }
    }
}

internal sealed class CardBotFeatureDisabledException(string game)
    : Exception($"{game} bot practice is disabled.");

internal static class CardBotHttp
{
    public static Microsoft.AspNetCore.Mvc.ActionResult FromException(
        Microsoft.AspNetCore.Mvc.ControllerBase controller,
        Exception exception) => exception switch
    {
        CardBotFeatureDisabledException => controller.StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new { code = "card-bots-disabled", error = exception.Message }),
        KeyNotFoundException => controller.NotFound(new { error = exception.Message }),
        UnauthorizedAccessException => controller.Unauthorized(new { error = exception.Message }),
        InvalidOperationException => controller.Conflict(new { code = "card-bot-state-conflict", error = exception.Message }),
        ArgumentException => controller.BadRequest(new { error = exception.Message }),
        _ => controller.Problem(
            "The card-bot practice service could not complete the request.",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}
