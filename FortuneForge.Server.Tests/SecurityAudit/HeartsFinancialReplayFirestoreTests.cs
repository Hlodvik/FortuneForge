using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class HeartsFinancialReplayFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Paid_match_reconciles_source_funding_revenue_and_each_wallet_ledger()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-hearts-replay-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None
        }.Build();
        var now = DateTime.SpecifyKind(new DateTime(2026, 10, 26), DateTimeKind.Utc);
        var state = FinishedState();
        var match = new PaidHeartsMatch
        {
            Id = new string('c', 64), Status = "result", Version = 58, EntryCents = 100,
            CreatedAtUtc = now, UpdatedAtUtc = now.AddMinutes(1), CompletedAtUtc = now.AddMinutes(1), State = state,
            Players = Enum.GetValues<PlayerSeat>().Select((seat, index) => new PaidHeartsParticipant
            {
                UserId = $"financial-player-{index}", EntryId = $"seat-{index}",
                DisplayName = $"Financial player {index}", Seat = seat, IsAccountBacked = true
            }).ToList()
        };
        var shares = PaidHeartsSettlement.Split(match.Id, match.PotCents, match.Players, state.Score);
        foreach (var player in match.Players) player.PayoutCents = shares.GetValueOrDefault(player.UserId);
        match.Winners = match.Players.Where(player => shares.ContainsKey(player.UserId)).Select(player => player.Seat).ToList();
        var capture = PaidHeartsRoundAudit.Capture(match, false, now.AddMinutes(1));
        await db.Collection("heartsPaidMatches").Document(match.Id).SetAsync(new Dictionary<string, object>
        {
            ["matchJson"] = JsonSerializer.Serialize(match, RoundAuditCapture.Json),
            ["status"] = "result", ["entryCents"] = match.EntryCents
        });
        await db.Collection("heartsPaidFunding").Document(match.Id).SetAsync(new Dictionary<string, object>
        {
            ["matchId"] = match.Id, ["entryCents"] = 100L, ["humanEntryCents"] = 400L,
            ["houseFundedEntryCents"] = 0L, ["initialHumanEntryCents"] = 400L,
            ["initialHouseFundedEntryCents"] = 0L, ["takeoverCount"] = 0L,
            ["grossPoolCents"] = 400L
        });
        await db.Collection("heartsPaidRevenue").Document(match.Id).SetAsync(new Dictionary<string, object>
        {
            ["matchId"] = match.Id, ["humanEntryCents"] = 400L, ["houseFundedEntryCents"] = 0L,
            ["initialHumanEntryCents"] = 400L, ["houseStakeReclassifiedCents"] = 0L,
            ["grossPoolCents"] = 400L, ["humanPayoutCents"] = 400L, ["botPrizeCents"] = 0L,
            ["forfeitedPoolCents"] = 0L, ["houseNetCents"] = 0L,
            ["financialClassification"] = "house-funded-hearts-pool-v1"
        });
        foreach (var player in match.Players)
        {
            await SetLedger($"{match.Id}-entry-{player.EntryId}", player.UserId, -100, "hearts-entry");
            if (player.PayoutCents > 0)
                await SetLedger($"{match.Id}-payout-{PaidHeartsIds.Hash(player.UserId)}",
                    player.UserId, player.PayoutCents, "hearts-match-payout");
        }
        Assert.Empty(await HeartsEvidenceReplayAudit.InspectFinancialAsync(db, capture.Summary, capture.PayloadJson, default));

        var first = match.Players[0];
        await db.Collection("balanceTransactions").Document($"{match.Id}-entry-{first.EntryId}")
            .UpdateAsync(new Dictionary<string, object> { ["amountCents"] = -50L });
        Assert.Contains(await HeartsEvidenceReplayAudit.InspectFinancialAsync(db, capture.Summary, capture.PayloadJson, default),
            finding => finding.Code == "hearts-paid-wallet-ledger-mismatch");
        await db.Collection("balanceTransactions").Document($"{match.Id}-entry-{first.EntryId}")
            .UpdateAsync("amountCents", -100L);
        await db.Collection("balanceTransactions").Document($"{match.Id}-entry-{first.EntryId}")
            .UpdateAsync("amount", -0.5d);
        Assert.Contains(await HeartsEvidenceReplayAudit.InspectFinancialAsync(db, capture.Summary, capture.PayloadJson, default),
            finding => finding.Code == "hearts-paid-wallet-ledger-mismatch");
        await db.Collection("balanceTransactions").Document($"{match.Id}-entry-{first.EntryId}")
            .UpdateAsync("amount", -1d);
        await SetLedger($"{match.Id}-payout-extra", first.UserId, 1, "hearts-match-payout");
        Assert.Contains(await HeartsEvidenceReplayAudit.InspectFinancialAsync(db, capture.Summary, capture.PayloadJson, default),
            finding => finding.Code == "hearts-paid-wallet-ledger-extra");

        async Task SetLedger(string id, string userId, long amount, string type)
        {
            await db.Collection("balanceTransactions").Document(id).SetAsync(new Dictionary<string, object>
            {
                ["transactionId"] = id, ["userId"] = userId, ["currencyId"] = "slotsCredits",
                ["type"] = type, ["amountCents"] = amount, ["amount"] = amount / 100d,
                ["balanceAfterCents"] = 10_000L + amount,
                ["balanceAfter"] = (10_000L + amount) / 100d
            });
        }
    }

    private static HeartsMatchState FinishedState()
    {
        var state = HeartsMatchEngine.Start(416, 1);
        foreach (var seat in Enum.GetValues<PlayerSeat>())
            state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat,
                state.Round.HandFor(seat).Take(3).ToArray())).State;
        while (state.Round.Phase != HeartsPhase.Complete)
        {
            var seat = state.Round.Turn;
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(seat,
                HeartsEngine.LegalCards(state.Round, seat)[0])).State;
        }
        return state;
    }
}
