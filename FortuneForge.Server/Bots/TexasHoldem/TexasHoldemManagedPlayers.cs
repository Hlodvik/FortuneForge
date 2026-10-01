using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Server.Bots.TexasHoldem;

internal static class TexasHoldemManagedPlayers
{
    private const string SkillKey = "managed-skill";

    public static IReadOnlyList<ManagedPlayerProfile> CreateLocalProfiles(int count, DateTime nowUtc) =>
        Enumerable.Range(0, Math.Max(0, count))
            .Select(index => new ManagedPlayerProfile(
                $"managed-local-holdem-{Guid.NewGuid():N}",
                new[] { "Maya", "Noah", "Avery", "Lena", "Theo" }[index % 5],
                ManagedPlayerSkillLevels.Poor + index % 3,
                new HashSet<string>([ManagedPlayerGames.TexasHoldem], StringComparer.OrdinalIgnoreCase),
                nowUtc))
            .ToArray();

    public static IReadOnlyList<CreditHoldemSeatAssignment> BuildSeats(
        IReadOnlyList<CreditHoldemTicket> accountTickets,
        IReadOnlyList<ManagedPlayerProfile> managedProfiles,
        IReadOnlyDictionary<string, long> balances,
        ulong seed,
        CreditHoldemTableRule rule)
    {
        var seats = accountTickets.Select((ticket, seat) => new CreditHoldemSeatAssignment(
            ticket.UserId,
            ticket.PublicSeatId,
            ticket.DisplayName,
            true,
            seat,
            CreditHoldemMoney.StackFromBalance(balances.GetValueOrDefault(ticket.UserId), rule.MaximumStackCents)))
            .ToList();
        var required = Math.Max(0, CreditHoldemMoney.MinimumStartPlayers - seats.Count);
        if (managedProfiles.Count < required)
            throw new InvalidOperationException("The managed-player queuer did not supply enough Hold'em profiles.");

        var accountAverage = checked((int)Math.Round(seats.Average(value => value.Stack)));
        for (var index = 0; index < required; index++)
        {
            var profile = managedProfiles[index];
            var seat = Enumerable.Range(0, CreditHoldemMoney.MaximumSeats)
                .First(value => seats.All(existing => existing.Seat != value));
            var variance = StableRange(seed, profile.UserId, -10, 11);
            var stack = Math.Clamp(
                checked(accountAverage + (int)Math.Round(accountAverage * (variance / 100m))),
                rule.BigBlindCents,
                rule.MaximumStackCents);
            seats.Add(new CreditHoldemSeatAssignment(
                profile.UserId,
                $"seat_{Guid.NewGuid():N}",
                profile.PlayerName,
                false,
                seat,
                stack,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SkillKey] = profile.SkillLevel.ToString(CultureInfo.InvariantCulture)
                }));
        }
        return seats.OrderBy(value => value.Seat).ToArray();
    }

    public static bool IsManaged(CreditHoldemPlayer player) => !player.IsAccountBacked;

    public static int Skill(CreditHoldemPlayer player) =>
        player.HostMetadata.TryGetValue(SkillKey, out var stored) &&
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var skill)
            ? skill
            : ManagedPlayerSkillLevels.Average;

    public static bool AdvanceIfDue(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (match.Status != "active") return false;
        var player = match.Players.Single(value => value.Seat == match.ActiveSeat);
        if (!IsManaged(player)) return CreditHoldemEngine.AdvanceExpiredTurn(match, nowUtc);
        if (nowUtc < PrivateActionDueAt(match, player)) return false;
        var decision = TexasHoldemManagedActionPolicy.Choose(match, player, Skill(player));
        _ = CreditHoldemEngine.ApplyAction(match, player.ActorId, decision.Action, decision.RaiseTo, nowUtc);
        return true;
    }

    internal static DateTime PrivateActionDueAt(CreditHoldemMatch match, CreditHoldemPlayer player)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"holdem-action\n{player.ActorId}\n{match.MatchId}\n{match.HandNumber}\n{match.Version}"));
        var delay = 650 + BitConverter.ToUInt16(digest, 0) % 1_151;
        return match.UpdatedAtUtc.AddMilliseconds(delay);
    }

    private static int StableRange(ulong seed, string actorId, int minimum, int maximumExclusive)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{actorId}"));
        return minimum + BitConverter.ToUInt16(digest, 0) % (maximumExclusive - minimum);
    }
}
