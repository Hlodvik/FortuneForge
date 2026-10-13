using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Server.Bots.ThreeCardPoker;

internal static class ThreeCardPokerManagedTablePolicy
{
    public const int MinimumStartOccupancy = 3;
    public const int MaximumOccupiedSeats = ThreeCardPokerTableEngine.Capacity - 1;
}
