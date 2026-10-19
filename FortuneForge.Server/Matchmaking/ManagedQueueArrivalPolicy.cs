using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Matchmaking;

/// <summary>
/// Stable, shared timing for managed profiles joining a human's queue after the
/// human-only matchmaking window has closed. This policy does not decide whether
/// managed profiles should be queued or which profiles to use.
/// </summary>
internal static class ManagedQueueArrivalPolicy
{
    public static TimeSpan NextGap(string gameId, string cohortId, int arrivalOrdinal)
    {
        ValidateIds(gameId, cohortId);
        if (arrivalOrdinal < 1)
            throw new ArgumentOutOfRangeException(nameof(arrivalOrdinal));

        var offsetMilliseconds = Fingerprint(gameId, cohortId, "arrival", arrivalOrdinal) % 1_201;
        return TimeSpan.FromMilliseconds(2_200 + offsetMilliseconds);
    }

    public static TimeSpan FinalPause(string gameId, string cohortId)
    {
        ValidateIds(gameId, cohortId);

        var offsetMilliseconds = Fingerprint(gameId, cohortId, "settle", 0) % 601;
        return TimeSpan.FromMilliseconds(2_000 + offsetMilliseconds);
    }

    private static void ValidateIds(string gameId, string cohortId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cohortId);
    }

    private static uint Fingerprint(string gameId, string cohortId, string phase, int ordinal)
    {
        // String.GetHashCode is randomized per process, so it cannot coordinate
        // queue arrival times across server instances. Length prefixes keep ids
        // with separators or overlapping text distinct.
        var input = string.Concat(
            gameId.Length.ToString(CultureInfo.InvariantCulture), ":", gameId,
            cohortId.Length.ToString(CultureInfo.InvariantCulture), ":", cohortId,
            phase.Length.ToString(CultureInfo.InvariantCulture), ":", phase,
            ordinal.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }
}
