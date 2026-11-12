using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => HeartsModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction(HeartsCompute.StartRoute, (HeartsComputeStartRequest request) =>
            HeartsMatchEngine.Start(request.Seed ?? CreateSeed(), request.TargetScore), isRuleViolation: IsRuleViolation);
        app.MapComputeAction(HeartsCompute.PassRoute, (HeartsComputePassRequest request) =>
        {
            ValidateSeat(request.Seat);
            ArgumentNullException.ThrowIfNull(request.Cards);
            return HeartsMatchEngine.Apply(request.PrivateState, new PassHeartsCards(request.Seat, request.Cards));
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction(HeartsCompute.PlayRoute, (HeartsComputePlayRequest request) =>
        {
            ValidateSeat(request.Seat);
            return HeartsMatchEngine.Apply(request.PrivateState, new PlayHeartsCard(request.Seat, request.Card));
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction(HeartsCompute.NextRoundRoute, (HeartsComputeNextRoundRequest request) =>
            HeartsMatchEngine.StartNextRound(request.PrivateState, request.Seed ?? CreateSeed()), isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/matches/legal-cards", (LegalCardsRequest request) =>
        {
            ValidateSeat(request.Seat);
            ArgumentNullException.ThrowIfNull(request.PrivateState);
            return HeartsEngine.LegalCards(request.PrivateState.Round, request.Seat);
        }, isRuleViolation: IsRuleViolation);
    }

    private static uint CreateSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    private static bool IsRuleViolation(Exception exception) => exception is HeartsRuleException;

    private static void ValidateSeat(PlayerSeat seat)
    {
        if (!Enum.IsDefined(seat))
            throw new ArgumentOutOfRangeException(nameof(seat));
    }

    private sealed record LegalCardsRequest(HeartsMatchState PrivateState, PlayerSeat Seat);
}
