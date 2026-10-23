using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Server.Bots.Hearts;

/// <summary>
/// Clearly labelled, table-local house actors. These identities never occupy a
/// shared profile lease, accrue profile downtime, or receive an account balance.
/// </summary>
internal static class HeartsHousePlayers
{
    public static HeartsManagedPlayer Create(
        string assignmentId,
        PlayerSeat seat,
        int skillLevel = ManagedPlayerSkillLevels.Average)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);
        if (!Enum.IsDefined(seat)) throw new ArgumentOutOfRangeException(nameof(seat));
        ManagedPlayerSkillLevels.Validate(skillLevel);
        return new HeartsManagedPlayer(
            $"hearts-house:{assignmentId}:{seat.ToString().ToLowerInvariant()}",
            $"House {seat}", skillLevel);
    }
}
