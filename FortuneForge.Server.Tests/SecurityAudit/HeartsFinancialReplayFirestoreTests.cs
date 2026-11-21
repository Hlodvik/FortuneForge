using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Accounts.Wallet;
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
        var (db, match, capture, _) = await SeedTerminalAsync();
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

    [Fact]
    public async Task Protected_match_reconciles_frozen_package_batches_and_detects_evidence_or_wallet_tampering()
    {
        var (db, match, capture, now) = await SeedTerminalAsync();
        var wallet = new GameWalletBatchFirestoreStore(db);
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new List<RoundAuditWalletOperation>();
        foreach (var player in match.Players)
        {
            await db.Collection("userBalances").Document($"{player.UserId}_slotsCredits").SetAsync(new Dictionary<string, object>
            {
                ["userId"] = player.UserId, ["currencyId"] = "slotsCredits", ["available"] = 1L,
                ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L,
            });
            var debit = new PaidHeartsMoney(player.UserId, -match.EntryCents, $"{match.Id}-entry-{player.EntryId}",
                "hearts-entry", match.Id, now);
            var admission = Plan(player.UserId, now, [debit], PaidHeartsFinancialSnapshot.Empty);
            var (request, receipt) = await SettleAsync(admission);
            entries.Add(new(FirestorePaidHeartsStore.WalletJournalCollection, debit.JournalId, debit.AccountId,
                debit.Kind, match.Id, debit.Cents, RoundAuditFinancialProof.SignedWallet, request.OperationId, receipt.BatchId));
            operations.Add(new(FirestorePaidHeartsStore.PreparedCollection, request.OperationId, request.OutcomeSha256,
                [debit.JournalId], new Dictionary<string, GameWalletSettlementReceipt>(), request, receipt));
        }

        var payoutActor = match.Players.First(player => player.PayoutCents == 0).UserId;
        var payouts = match.Players.Where(player => player.PayoutCents > 0).Select(player => new PaidHeartsMoney(
            player.UserId, player.PayoutCents, $"{match.Id}-payout-{PaidHeartsIds.Hash(player.UserId)}",
            "hearts-match-payout", match.Id, now.AddMinutes(1))).ToArray();
        var terminal = Plan(payoutActor, now.AddMinutes(1), payouts, new(entries.ToArray(), operations.ToArray(), []));
        var (_, payoutReceipt) = await SettleAsync(terminal);
        var protectedCapture = PaidHeartsFinancialProof.Protect(capture, terminal, payoutReceipt, match.Id);

        Assert.Contains(payoutReceipt.Accounts, account => account.AccountId == payoutActor &&
            account.WagerTransactionId is null && account.PayoutTransactionId is null);
        Assert.Empty((await db.Collection(FirestorePaidHeartsStore.WalletJournalCollection).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(FirestorePaidHeartsStore.PreparedCollection).GetSnapshotAsync()).Documents);
        Assert.Empty(await InspectAsync(protectedCapture));

        entries[0] = entries[0] with { AmountCents = -99L };
        var tamperedPlan = terminal with
        {
            Evidence = terminal.Evidence with { Entries = entries.ToArray() },
            FrozenSha256 = terminal.Request().OutcomeSha256,
        };
        var tamperedCapture = PaidHeartsFinancialProof.Protect(capture, tamperedPlan, payoutReceipt, match.Id);
        Assert.Contains(await InspectAsync(tamperedCapture),
            finding => finding.Code == "hearts-paid-protected-wallet-mismatch");

        var wager = operations[0].BatchReceipt!.Accounts.Single().WagerTransactionId!;
        await db.Collection("balanceTransactions").Document(wager).UpdateAsync("amountCents", -99L);
        Assert.Contains(await InspectAsync(protectedCapture),
            finding => finding.Code == "hearts-paid-protected-wallet-mismatch");
        Assert.Equal(-99L, (await db.Collection("balanceTransactions").Document(wager).GetSnapshotAsync())
            .GetValue<long>("amountCents"));

        static PaidHeartsPlan Plan(string actor, DateTime timestamp, PaidHeartsMoney[] money, PaidHeartsFinancialSnapshot prior) =>
            new(Guid.NewGuid().ToString("N"), actor, timestamp, [], [], money, [], [],
                [new($"hearts-paid-test/{actor}", actor, "{}")], prior, null);

        async Task<(GameWalletBatchRequest Request, GameWalletBatchReceipt Receipt)> SettleAsync(PaidHeartsPlan plan)
        {
            var request = plan.Request();
            var body = JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson);
            var receipt = await wallet.SettleAsync(HeartsBackend.GameId, plan.ActorId, request,
                Convert.ToHexString(SHA256.HashData(body)), new DateTimeOffset(plan.PreparedAtUtc), default);
            Assert.False(receipt.BatchDeclined);
            Assert.False(receipt.WasAlreadySettled);
            return (request, receipt);
        }

        Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(RoundAuditCapture evidence) =>
            HeartsEvidenceReplayAudit.InspectFinancialAsync(db, evidence.Summary, evidence.PayloadJson, default);
    }

    private async Task<(FirestoreDb Database, PaidHeartsMatch Match, RoundAuditCapture Capture, DateTime Now)> SeedTerminalAsync()
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
        return (db, match, capture, now);
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
