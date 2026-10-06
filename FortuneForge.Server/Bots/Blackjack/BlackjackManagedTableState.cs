using FortuneForge.Games.Blackjack;

namespace FortuneForge.Server.Bots.Blackjack;

internal static class BlackjackManagedTablePolicy
{
    public const int MinimumStartOccupancy = 3;
    public const int MaximumOccupiedSeats = BlackjackTableEngine.Capacity - 1;
}
