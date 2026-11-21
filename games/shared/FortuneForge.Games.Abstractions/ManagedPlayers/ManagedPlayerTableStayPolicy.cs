using System.Security.Cryptography;

namespace FortuneForge.Games.Abstractions.ManagedPlayers;

public static class ManagedPlayerTableStayPolicy
{
    public const int MinimumRounds = 4;
    public const int MaximumRounds = 20;

    public static int DepartureRound(
        string gameId,
        string tableId,
        string profileId,
        int joinedRound)
    {
        if (joinedRound < 0) throw new ArgumentOutOfRangeException(nameof(joinedRound));
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"managed-table-stay\n{gameId}\n{tableId}\n{profileId}\n{joinedRound}"));
        var rounds = MinimumRounds + BitConverter.ToUInt16(hash, 0) %
            (MaximumRounds - MinimumRounds + 1);
        return checked(joinedRound + rounds);
    }

    public static bool ShouldLeave(int currentRound, int departureRound) =>
        currentRound >= departureRound;
}
