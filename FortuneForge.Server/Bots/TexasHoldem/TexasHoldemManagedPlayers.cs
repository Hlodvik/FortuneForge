using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Server.Bots.TexasHoldem;

internal static class TexasHoldemManagedPlayers
{
    private const string SkillKey = "managed-skill";
    private const string DepartureHandKey = "managed-departure-hand";

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
        CreditHoldemTableRule rule,
        string? tableId = null)
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
                Metadata(profile, tableId ?? accountTickets[0].PartitionKey, 1)));
        }
        return seats.OrderBy(value => value.Seat).ToArray();
    }

    public static bool IsManaged(CreditHoldemPlayer player) => !player.IsAccountBacked;

    public static int Skill(CreditHoldemPlayer player) =>
        player.HostMetadata.TryGetValue(SkillKey, out var stored) &&
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var skill)
            ? skill
            : ManagedPlayerSkillLevels.Average;

    public static IReadOnlyList<CreditHoldemPlayer> DepartingAfterCurrentHand(
        CreditHoldemMatch match) =>
        match.Players.Where(player => IsManaged(player) &&
            DepartureHand(player) is { } departure &&
            ManagedPlayerTableStayPolicy.ShouldLeave(match.HandNumber, departure)).ToArray();

    public static IReadOnlyList<string> RemoveDepartingPlayers(CreditHoldemMatch match)
    {
        var departing = DepartingAfterCurrentHand(match);
        foreach (var player in departing)
            match.Players.Remove(player);
        return departing.Select(player => player.ActorId).ToArray();
    }

    public static void AddPlayers(
        CreditHoldemMatch match,
        IReadOnlyList<ManagedPlayerProfile> arrivals)
    {
        if (arrivals.Count == 0) return;
        var rule = CreditHoldemTableRules.Resolve(match.TableRuleId);
        foreach (var profile in arrivals)
        {
            if (match.Players.Count >= CreditHoldemMoney.MaximumSeats - 1) break;
            var seat = Enumerable.Range(0, CreditHoldemMoney.MaximumSeats)
                .First(value => match.Players.All(existing => existing.Seat != value));
            var averageStack = match.Players.Count == 0
                ? rule.MaximumStackCents
                : checked((int)Math.Round(match.Players.Average(player => player.Stack)));
            var variance = StableRange((ulong)match.HandNumber, profile.UserId, -10, 11);
            var stack = Math.Clamp(
                checked(averageStack + (int)Math.Round(averageStack * (variance / 100m))),
                rule.BigBlindCents,
                rule.MaximumStackCents);
            match.Players.Add(new CreditHoldemPlayer
            {
                ActorId = profile.UserId,
                PublicSeatId = $"seat_{Guid.NewGuid():N}",
                DisplayName = profile.PlayerName,
                IsAccountBacked = false,
                HostMetadata = Metadata(profile, match.MatchId, checked(match.HandNumber + 1)),
                Seat = seat,
                StartingStack = stack,
                Stack = stack,
                HoleCards = []
            });
        }
    }

    public static bool AdvanceIfDue(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (match.Status != "active") return false;
        var player = match.Players.Single(value => value.Seat == match.ActiveSeat);
        if (!IsManaged(player)) return false;
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

    private static Dictionary<string, string> Metadata(
        ManagedPlayerProfile profile,
        string tableId,
        int joinedHand) =>
        new(StringComparer.Ordinal)
        {
            [SkillKey] = profile.SkillLevel.ToString(CultureInfo.InvariantCulture),
            [DepartureHandKey] = ManagedPlayerTableStayPolicy.DepartureRound(
                ManagedPlayerGames.TexasHoldem,
                tableId,
                profile.UserId,
                joinedHand).ToString(CultureInfo.InvariantCulture)
        };

    private static int? DepartureHand(CreditHoldemPlayer player) =>
        player.HostMetadata.TryGetValue(DepartureHandKey, out var stored) &&
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int StableRange(ulong seed, string actorId, int minimum, int maximumExclusive)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{actorId}"));
        return minimum + BitConverter.ToUInt16(digest, 0) % (maximumExclusive - minimum);
    }
}
