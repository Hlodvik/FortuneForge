using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Server.Games.Hearts;

public sealed record JoinPaidHeartsRequest(decimal EntryCredits = 1);
public sealed record PaidHeartsVersionRequest(int ExpectedVersion);
public sealed record PaidHeartsPassRequest(IReadOnlyList<string> Cards, int ExpectedVersion);
public sealed record PaidHeartsCardRequest(string Card, int ExpectedVersion);
public sealed record PaidHeartsSessionResponse(
    string Kind, int Version, decimal BalanceCredits, IReadOnlyList<decimal> EntryOptions,
    PaidHeartsQueueResponse? Queue, PaidHeartsTableResponse? Table, string? Notice = null);
public sealed record PaidHeartsQueueResponse(
    string MatchId, decimal EntryCredits, decimal PotCredits, DateTime JoinedAtUtc,
    IReadOnlyList<PaidHeartsQueuePlayerResponse> Players, string Message);
public sealed record PaidHeartsQueuePlayerResponse(string Seat, string DisplayName, bool IsCurrentPlayer);
public sealed record PaidHeartsTableResponse(
    HeartsMatchResponse Match, decimal EntryCredits, decimal PotCredits, decimal PayoutCredits,
    IReadOnlyList<string> WinnerSeats, IReadOnlyList<PaidHeartsPlayerResponse> Players,
    DateTime? ActionDeadlineAtUtc, DateTime? CompletedAtUtc);
public sealed record PaidHeartsPlayerResponse(
    string Seat, string DisplayName, bool IsCurrentPlayer, bool HasLeft,
    decimal EntryCredits, decimal PayoutCredits);
public sealed record PaidHeartsHistoryResponse(IReadOnlyList<PaidHeartsTableResponse> Items);

internal sealed class PaidHeartsOptions
{
    public long[] EntryOptionsCents { get; set; } = [100, 200, 500, 1000, 2500];
    public bool AllowManagedPlayers { get; set; } = true;
    public int HumanQueueSeconds { get; set; } = 5;
    public int ActionTimeoutSeconds { get; set; } = 45;

    public void Validate()
    {
        if (EntryOptionsCents.Length == 0 || EntryOptionsCents.Any(cents => cents < 100 || cents > 100_000) ||
            EntryOptionsCents.Distinct().Count() != EntryOptionsCents.Length)
            throw new InvalidOperationException("Paid Hearts entries must be distinct amounts between R1 and R1,000.");
        if (HumanQueueSeconds is < 1 or > 60 || ActionTimeoutSeconds is < 15 or > 120)
            throw new InvalidOperationException("Paid Hearts queue and action deadlines are invalid.");
    }
}

internal sealed class PaidHeartsParticipant
{
    public string UserId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public PlayerSeat Seat { get; set; }
    public bool IsAccountBacked { get; set; }
    public string? ManagedAssignmentId { get; set; }
    public bool HasLeft { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public int SkillLevel { get; set; } = 3;
    public ulong DecisionSeed { get; set; }
    public ulong TimingSeed { get; set; }
    public int DecisionVersion { get; set; }
    public int TimingVersion { get; set; }
    public int MissedTurns { get; set; }
    public long PayoutCents { get; set; }
}

internal sealed class PaidHeartsMatch
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "queue";
    public int Version { get; set; } = 1;
    public long EntryCents { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime NextAdmissionAtUtc { get; set; }
    public DateTime? NextActionAtUtc { get; set; }
    public DateTime? LeaseRenewalAtUtc { get; set; }
    public PlayerSeat? ScheduledSeat { get; set; }
    public string? AdmissionClaim { get; set; }
    public DateTime? AdmissionClaimExpiresAtUtc { get; set; }
    public bool ReleasePending { get; set; }
    public List<PaidHeartsParticipant> Players { get; set; } = [];
    public HeartsMatchState? State { get; set; }
    public CompletedTrick? RecentTrick { get; set; }
    public List<PlayerSeat> Winners { get; set; } = [];
    public string Message { get; set; } = "Looking for players.";
    public long PotCents => checked(Players.Count * EntryCents);
}

internal static class PaidHeartsSettlement
{
    // Stable identity hashing, rather than seat order, assigns indivisible cents.
    // The match identifier is server generated before any hands are visible.
    public static IReadOnlyDictionary<string, long> Split(
        string matchId, long potCents, IEnumerable<PaidHeartsParticipant> eligible,
        HeartsMatchScore scores)
    {
        var players = eligible.ToArray();
        if (players.Length == 0) return new Dictionary<string, long>();
        var lowest = players.Min(player => scores.For(player.Seat));
        var winners = players.Where(player => scores.For(player.Seat) == lowest)
            .OrderBy(player => PaidHeartsIds.Hash($"tie\n{matchId}\n{player.UserId}"), StringComparer.Ordinal)
            .ToArray();
        return winners.Select((winner, index) => (winner.UserId,
                Amount: checked(potCents / winners.Length + (index < potCents % winners.Length ? 1 : 0))))
            .ToDictionary(value => value.UserId, value => value.Amount, StringComparer.Ordinal);
    }
}

internal static class PaidHeartsIds
{
    public static string Hash(string input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    public static uint Seed() => BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
    public static ulong PrivateSeed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
}

internal sealed class PaidHeartsConflictException(string message) : InvalidOperationException(message);
internal sealed class PaidHeartsInsufficientFundsException() : InvalidOperationException("Your balance is too low for this entry.");
