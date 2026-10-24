using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Server.Bots.TexasHoldem;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemEscrowEngineTests
{
    private static readonly DateTime Start = new(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void JoinAndPersistedLegacyHandsRemainCompatibleWhenBuyInFieldsAreAbsent()
    {
        var request = JsonSerializer.Deserialize<JoinCreditHoldemQueueRequest>("{\"expectedVersion\":0}", JsonOptions)!;
        Assert.Null(request.BuyInCredits);
        Assert.Equal("standard", request.TableRuleId);
        var explicitBuyIn = JsonSerializer.Deserialize<JoinCreditHoldemQueueRequest>(
            "{\"expectedVersion\":0,\"buyInCredits\":12.34}", JsonOptions)!;
        Assert.Equal(12.34m, explicitBuyIn.BuyInCredits);

        var legacy = Deal(escrow: false);
        var document = JsonSerializer.SerializeToNode(legacy, JsonOptions)!.AsObject();
        document.Remove("usesBuyInEscrow");
        document.Remove("escrowCashedOutTicketIds");
        foreach (var player in document["players"]!.AsArray()) player!.AsObject().Remove("escrowTicketId");
        var restored = JsonSerializer.Deserialize<CreditHoldemMatch>(document.ToJsonString(), JsonOptions)!;
        Assert.False(restored.UsesBuyInEscrow);
        Assert.Empty(restored.EscrowCashedOutTicketIds);
        Assert.All(restored.Players, player => Assert.Null(player.EscrowTicketId));

        var ticket = Ticket("legacy", "waiting", 0);
        var ticketDocument = JsonSerializer.SerializeToNode(ticket, JsonOptions)!.AsObject();
        ticketDocument.Remove("buyInCents");
        Assert.Equal(0, JsonSerializer.Deserialize<CreditHoldemTicket>(ticketDocument.ToJsonString(), JsonOptions)!.BuyInCents);
    }

    [Fact]
    public void NewBuyInPartitionAndPublicLimitsAreSeparateFromExistingTables()
    {
        Assert.Equal("open-table-v4-buyin-standard", CreditHoldemMoney.OpenPartition);
        foreach (var rule in CreditHoldemTableRules.All)
        {
            Assert.Equal($"open-table-v4-buyin-{rule.Id}", CreditHoldemTableRules.Partition(rule.Id));
            Assert.Equal(CreditHoldemMoney.ToCredits(rule.BigBlindCents), rule.Public.MinimumBuyInCredits);
            Assert.Equal(CreditHoldemMoney.ToCredits(rule.MaximumStackCents), rule.Public.MaximumTableStackCredits);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(10_001)]
    public void InitialEscrowDealRejectsOutOfRangeFundedStacksInsteadOfClampingOrMinting(int buyInCents)
    {
        var seats = Seats(1000);
        seats[0] = seats[0] with { Stack = buyInCents };
        Assert.Throws<ArgumentException>(() => CreditHoldemEngine.Deal(
            "invalid-buyin", seats, CreditHoldemMoney.OpenPartition, 42, Start,
            usesBuyInEscrow: true));
    }

    [Fact]
    public void EscrowSettlementUsesActualHouseFundedPotAwardAndIsIdempotent()
    {
        var match = Deal(stack: 1000);
        ArrangeShowdown(match, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));
        var beforeSettlement = match.Players.Select(player => player.Stack).ToArray();
        var first = CreditHoldemEngine.ApplyFinancialSettlement(match);
        var second = CreditHoldemEngine.ApplyFinancialSettlement(match);

        Assert.Equal(3000, match.Players.Sum(player => player.Stack));
        Assert.Equal(300, match.Players[0].WonHandChips);
        Assert.Equal(1200, match.Players[0].Stack);
        Assert.Equal(100, first.HumanCommittedCents);
        Assert.Equal(300, first.HumanPayoutsCents["actor-0"]);
        Assert.Equal(300, first.HumanPayoutCents);
        Assert.Equal(-200, first.HouseNetCents);
        Assert.Equal(300, match.Players[0].AccountPayoutCents);
        Assert.Equal(first.HumanCommittedCents, second.HumanCommittedCents);
        Assert.Equal(first.HumanPayoutsCents, second.HumanPayoutsCents);
        Assert.Equal(first.HouseNetCents, second.HouseNetCents);
        Assert.Equal(beforeSettlement, match.Players.Select(player => player.Stack).ToArray());
    }

    [Fact]
    public void EscrowSidePotsConserveChipsAndUseOnlyAlreadyComputedEligibleAwards()
    {
        var match = Deal(stack: 1000, accountSeats: [0, 2]);
        ArrangeShowdown(match, [100, 200, 300]);
        // Player 0 wins the main pot; the house wins the next side pot;
        // player 2 receives only their unmatched 100-cent contribution back.
        match.Players[1].HoleCards.Clear();
        match.Players[1].HoleCards.AddRange(["K|spades", "K|hearts"]);
        match.Players[2].HoleCards.Clear();
        match.Players[2].HoleCards.AddRange(["Q|spades", "Q|hearts"]);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));
        var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match);

        Assert.Equal([300, 200, 100], match.Players.Select(player => player.WonHandChips).ToArray());
        Assert.Equal([1200, 1000, 800], match.Players.Select(player => player.Stack).ToArray());
        Assert.Equal(3000, match.Players.Sum(player => player.Stack));
        Assert.Equal(400, settlement.HumanCommittedCents);
        Assert.Equal(300, settlement.HumanPayoutsCents["actor-0"]);
        Assert.Equal(100, settlement.HumanPayoutsCents["actor-2"]);
        Assert.Equal(400, settlement.HumanPayoutCents);
        Assert.Equal(0, settlement.HouseNetCents);
    }

    [Fact]
    public void LegacySettlementRetainsHumanFundedOnlyPayouts()
    {
        var match = Deal(stack: 1000, escrow: false);
        ArrangeShowdown(match, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));
        var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match);

        Assert.False(match.UsesBuyInEscrow);
        Assert.Equal(300, match.Players[0].WonHandChips);
        Assert.Equal(100, settlement.HumanCommittedCents);
        Assert.Equal(100, settlement.HumanPayoutsCents["actor-0"]);
        Assert.Equal(0, settlement.HouseNetCents);
    }

    [Fact]
    public void NextEscrowHandRetainsWinningsAboveBuyInCapAndNeverReloadsAnyPlayerFromWallet()
    {
        var prior = Deal(stack: 10_000);
        ArrangeShowdown(prior, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(prior);
        var finalStacks = prior.Players.ToDictionary(player => player.ActorId, player => player.Stack);
        Assert.Equal(10_200, finalStacks["actor-0"]);
        prior.EscrowCashedOutTicketIds.Add("old-reservation");
        var next = CreditHoldemEngine.StartNextHand(prior,
            new Dictionary<string, long> { ["actor-0"] = 0, ["actor-1"] = 1_000_000 }, 43, 1, Start.AddSeconds(2));

        Assert.NotNull(next);
        Assert.True(next.UsesBuyInEscrow);
        Assert.Equal(prior.MatchId, next.MatchId);
        Assert.Equal(prior.HandNumber + 1, next.HandNumber);
        Assert.Equal(30_000, next.Players.Sum(player => player.StartingStack));
        foreach (var player in next.Players)
        {
            Assert.Equal(finalStacks[player.ActorId], player.StartingStack);
            Assert.Equal(finalStacks[player.ActorId], player.Stack + player.CommittedHand);
            Assert.Equal(prior.Players.Single(old => old.ActorId == player.ActorId).EscrowTicketId, player.EscrowTicketId);
        }
        Assert.Contains("old-reservation", next.EscrowCashedOutTicketIds);
        Assert.NotSame(prior.EscrowCashedOutTicketIds, next.EscrowCashedOutTicketIds);
        next.EscrowCashedOutTicketIds.Add("new-reservation");
        Assert.DoesNotContain("new-reservation", prior.EscrowCashedOutTicketIds);
    }

    [Fact]
    public void LegacyNextHandKeepsItsPreviousWalletReloadAndSyntheticStartingStackBehavior()
    {
        var prior = Deal(stack: 1000, escrow: false);
        ArrangeShowdown(prior, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(prior);
        var next = CreditHoldemEngine.StartNextHand(prior,
            new Dictionary<string, long> { ["actor-0"] = 40_000 }, 43, 1, Start.AddSeconds(2));

        Assert.NotNull(next);
        Assert.False(next.UsesBuyInEscrow);
        Assert.Equal(10_000, next.Players[0].StartingStack);
        Assert.All(next.Players.Where(player => !player.IsAccountBacked), player => Assert.Equal(1000, player.StartingStack));
    }

    [Fact]
    public void PendingFundedReturnUsesItsOwnReservationDespiteEmptyWalletAndPreviouslyCashedOutSeat()
    {
        var prior = Deal(stack: 1000, count: 4);
        ArrangeShowdown(prior, [100, 100, 100, 100]);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(prior);
        prior.LeavingActorIds.Add("actor-0");
        prior.EscrowCashedOutTicketIds.Add("reservation-0");
        prior.Players[3].Stack = 0;
        prior.PendingTakeovers.Add(Ticket("new-reservation", "actor-0", 2000));
        prior.PendingTakeovers.Add(Ticket("reservation-0", "already-refunded", 2000));
        var next = CreditHoldemEngine.StartNextHand(prior, new Dictionary<string, long>(), 43, 1, Start.AddSeconds(2));

        Assert.NotNull(next);
        Assert.Equal(3, next.Players.Count);
        var returned = Assert.Single(next.Players, player => player.IsAccountBacked);
        Assert.Equal("actor-0", returned.ActorId);
        Assert.Equal("new-reservation", returned.EscrowTicketId);
        Assert.Equal(2000, returned.StartingStack);
        Assert.DoesNotContain(next.Players, player => player.ActorId == "actor-3" || player.ActorId == "already-refunded");
        Assert.Empty(next.LeavingActorIds);
        Assert.Contains("reservation-0", next.EscrowCashedOutTicketIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(10_001)]
    public void EscrowPendingTakeoverCannotUseWalletOrOutOfRangeBuyInToCreateChips(int buyInCents)
    {
        var prior = Deal(stack: 1000);
        ArrangeShowdown(prior, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(prior);
        prior.PendingTakeovers.Add(Ticket("not-funded", "waiting", buyInCents));
        var next = CreditHoldemEngine.StartNextHand(prior,
            new Dictionary<string, long> { ["waiting"] = 1_000_000 }, 43, 1, Start.AddSeconds(2));

        Assert.NotNull(next);
        Assert.DoesNotContain(next.Players, player => player.ActorId == "waiting");
        Assert.Equal(3, next.Players.Count);
    }

    [Fact]
    public void FundedSeatConstructionUsesReservationRatherThanRemainingWallet()
    {
        var ticket = Ticket("funded", "account", 3000);
        var seats = TexasHoldemManagedPlayers.BuildSeats([ticket],
            TexasHoldemManagedPlayers.CreateLocalProfiles(2, Start), new Dictionary<string, long>(),
            42, CreditHoldemTableRules.Resolve("standard"));
        var account = Assert.Single(seats, seat => seat.IsAccountBacked);
        Assert.Equal(3000, account.Stack);
        Assert.Equal(ticket.TicketId, account.EscrowTicketId);
        Assert.All(seats.Where(seat => !seat.IsAccountBacked), player => Assert.InRange(player.Stack, 2700, 3300));
    }

    [Fact]
    public void BustedEscrowManagedSeatIsRemovedAtHandBoundaryNotWhileAllIn()
    {
        var match = Deal(stack: 1000);
        match.Players[1].Stack = 0;
        match.Players[1].Status = "all-in";
        Assert.Empty(TexasHoldemManagedPlayers.DepartingAfterCurrentHand(match));
        match.Status = "completed";
        Assert.Equal(["actor-1"], TexasHoldemManagedPlayers.RemoveDepartingPlayers(match));
        Assert.DoesNotContain(match.Players, player => player.ActorId == "actor-1");
    }

    [Fact]
    public void BustedAccountCannotAutomaticallyRebuyFromItsWallet()
    {
        var prior = Deal(stack: 1000);
        ArrangeShowdown(prior, [100, 100, 100]);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(prior);
        prior.Players[0].Stack = 99;

        var next = CreditHoldemEngine.StartNextHand(prior,
            new Dictionary<string, long> { ["actor-0"] = 1_000_000 }, 43, 1, Start.AddSeconds(2));

        Assert.Null(next);
        Assert.Equal(99, prior.Players[0].Stack);
        Assert.Equal("reservation-0", prior.Players[0].EscrowTicketId);
    }

    [Fact]
    public void PublicTableDoesNotExposePrivateEscrowReservationOrCashoutKeys()
    {
        var match = Deal();
        match.EscrowCashedOutTicketIds.Add("private-already-paid-reservation");
        var json = JsonSerializer.Serialize(CreditHoldemProjection.Table(match, "actor-0", Start), JsonOptions);

        Assert.DoesNotContain("\"usesBuyInEscrow\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"escrowTicketId\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"escrowCashedOutTicketIds\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reservation-0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-already-paid-reservation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("actor-0", json, StringComparison.Ordinal);
    }

    private static CreditHoldemTicket Ticket(string id, string actor, int buyInCents) => new(
        id, actor, $"seat-{id}", "Waiting player", CreditHoldemMoney.OpenPartition,
        "queued", 1, Start, Start, null, BuyInCents: buyInCents);

    private static CreditHoldemSeatAssignment[] Seats(int stack, int count = 3, int[]? accountSeats = null) =>
        Enumerable.Range(0, count).Select(index => new CreditHoldemSeatAssignment(
            $"actor-{index}", $"seat-{index}", $"Player {index}", (accountSeats ?? [0]).Contains(index), index, stack,
            EscrowTicketId: (accountSeats ?? [0]).Contains(index) ? $"reservation-{index}" : null)).ToArray();

    private static CreditHoldemMatch Deal(int stack = 1000, bool escrow = true, int count = 3, int[]? accountSeats = null) =>
        CreditHoldemEngine.Deal("escrow-match", Seats(stack, count, accountSeats),
            CreditHoldemMoney.OpenPartition, 42, Start, usesBuyInEscrow: escrow);

    private static void ArrangeShowdown(CreditHoldemMatch match, int[] contributions)
    {
        string[][] holeCards =
        [
            ["A|spades", "A|hearts"], ["K|spades", "3|hearts"],
            ["Q|spades", "4|hearts"], ["10|spades", "6|hearts"], ["8|diamonds", "2|spades"]
        ];
        match.Community.Clear();
        match.Community.AddRange(["2|clubs", "5|diamonds", "7|hearts", "9|spades", "J|clubs"]);
        for (var index = 0; index < match.Players.Count; index++)
        {
            var player = match.Players[index];
            player.HoleCards.Clear();
            player.HoleCards.AddRange(holeCards[index]);
            player.CommittedHand = contributions[index];
            player.CommittedRound = contributions[index];
            player.Stack = player.StartingStack - contributions[index];
            player.Status = "active";
        }
    }
}
