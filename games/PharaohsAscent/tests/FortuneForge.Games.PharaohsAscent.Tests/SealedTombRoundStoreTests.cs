using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PharaohsAscent.LocalHarness;
using FortuneForge.Games.PharaohsAscent.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class SealedTombRoundStoreTests
{
    private const string Account = "sealed-tomb-paid-store-unit";

    [PharaohsAscentStoreEmulatorFact]
    public async Task FundedExpeditionRestoresChoicesAndCreditsADroppedFinalSettlementExactlyOnce()
    {
        var database = new FirestoreDbBuilder { ProjectId = "demo-tomb-paid-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var wallet = new LocalTestWallet(Account);
        var clock = new TestClock();
        PharaohsAscentRoundStateReply? state = null;
        for (var spin = 0; spin < 6; spin++)
        {
            var result = await Service(database, wallet, clock, new ScarabDraws()).SpinAsync(Account,
                new($"tomb-funding-spin-{spin:D3}", 100, false), true, default);
            state = result.Features!;
            clock.Advance();
        }
        var ticket = Assert.Single(state!.GameFeatureState.Tomb!.Tickets);
        Assert.Equal(100, ticket.FundedWagerPoints);
        Assert.Equal(0, state.GameFeatureState.Tomb.Funding.Sigils);
        Assert.Equal(6, wallet.UniqueSettlements);
        var baseline = wallet.BalanceCents;

        state = await Service(database, wallet, clock, new ZeroDraws()).ActAsync(Account, "tomb-enter-operation-001",
            new("enter", state.GameFeatureState.Tomb.Revision), default);
        Assert.True(state.FeatureActionRequired);
        Assert.Equal(baseline, wallet.BalanceCents);
        var restored = await Service(database, wallet, clock, new NoDraws()).ReadStateAsync(Account, default);
        Assert.Equal(state.GameFeatureState.Tomb!.ActiveRun!.Choices,
            restored.GameFeatureState.Tomb!.ActiveRun!.Choices);
        clock.Advance();
        await Assert.ThrowsAsync<SealedTombConflictException>(() => Service(database, wallet, clock, new NoDraws()).SpinAsync(Account,
            new("tomb-blocked-cash-spin-001", 100, false), true, default));
        Assert.Equal(7, wallet.UniqueSettlements);

        for (var key = 0; key < 3; key++)
        {
            clock.Advance();
            var tomb = state.GameFeatureState.Tomb!;
            state = await Service(database, wallet, clock, new ZeroDraws()).ActAsync(Account, $"tomb-key-operation-{key:D3}",
                new("open-room", tomb.Revision, tomb.ActiveRun!.Choices.Single(choice => choice.Kind == "key").Id), default);
        }
        Assert.Equal("finale", state.GameFeatureState.Tomb!.ActiveRun!.Phase);
        Assert.Equal(baseline, wallet.BalanceCents);
        clock.Advance();
        var finale = new SealedTombAction("open-sarcophagus", state.GameFeatureState.Tomb.Revision);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(database, wallet, clock, new HighDraws())
            .ActAsync(Account, "tomb-final-operation-001", finale, default));
        Assert.Equal(baseline + 70_000, wallet.BalanceCents);
        var pending = await Service(database, wallet, clock, new NoDraws()).ReadStateAsync(Account, default);
        Assert.Null(pending.PendingOperation);
        Assert.Equal(finale, pending.GameFeatureState.Tomb!.PendingAction);
        Assert.True(pending.FeatureActionRequired);
        await Assert.ThrowsAsync<SealedTombConflictException>(() => Service(database, wallet, clock, new NoDraws())
            .ActAsync(Account, "tomb-wrong-pending-action", new("collect", finale.ExpectedRevision), default));

        var settled = await Service(database, wallet, clock, new NoDraws()).ActAsync(Account,
            "tomb-final-fresh-http-header", finale, default);
        Assert.Null(settled.GameFeatureState.Tomb!.ActiveRun);
        Assert.Null(settled.GameFeatureState.Tomb.PendingAction);
        Assert.Equal(2_800, settled.GameFeatureState.Tomb.LastRun!.PayoutPoints);
        Assert.True(settled.FeatureActionRequired);
        Assert.Equal(baseline + 70_000, wallet.BalanceCents);
        Assert.Equal(11, wallet.UniqueSettlements);
        await Service(database, wallet, clock, new NoDraws()).ActAsync(Account, "tomb-final-operation-001", finale, default);
        Assert.Equal(11, wallet.UniqueSettlements);
        Assert.All(wallet.Requests.Skip(6), request => Assert.Equal(0, request.WagerCents));
        Assert.Equal(7, (await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Count);
        Assert.Equal(7, (await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Count);
        Assert.Equal(5, (await database.Collection(SealedTombEvidence.ResultCollection).GetSnapshotAsync()).Count);

        clock.Advance();
        var dismissed = await Service(database, wallet, clock, new NoDraws()).ActAsync(Account, "tomb-dismiss-operation-001",
            new("dismiss", settled.GameFeatureState.Tomb.Revision), default);
        Assert.False(dismissed.FeatureActionRequired);
        Assert.Null(dismissed.GameFeatureState.Tomb!.LastRun);
        Assert.Equal(baseline + 70_000, wallet.BalanceCents);

        var otherWallet = new LocalTestWallet("sealed-tomb-other-account");
        var other = await Service(database, otherWallet, clock, new NoDraws()).ReadStateAsync("sealed-tomb-other-account", default);
        Assert.Empty(other.GameFeatureState.Tomb!.Tickets);
        Assert.Null(other.GameFeatureState.Tomb.ActiveRun);
    }

    [Fact]
    public void CompletedActionProofReplaysItsFundedMinimumAndRejectsChangedDrawsOrWallet()
    {
        var before = new FortuneForge.Games.Slots.Persistence.ClassicSlotRoundState(0, 0, null, 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>())
        { NativeStateJson = SealedTombState.Serialize(SealedTombRulesTests.Ticket(13)) };
        var entered = PharaohsAscentRoundPolicy.Prepare(Account,
            new("tomb-proof-enter-operation", 0, false) { TombAction = new("enter", 0) }, before, DateTime.UtcNow, null, new ZeroDraws());
        var collect = PharaohsAscentRoundPolicy.Prepare(Account,
            new("tomb-proof-collect-operation", 0, false) { TombAction = new("collect", 1) }, entered.Progress.After,
            DateTime.UtcNow, null, new NoDraws());
        Assert.Null(collect.Engine);
        Assert.Equal(0, collect.WagerCents);
        Assert.Equal(65 * 25, collect.PayoutCents);
        var id = collect.TombEvidence!.EventId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, Account, id),
            null, id + "-payout", 100_000, 100_000, 101_625, DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
        var report = SealedTombEvidence.Capture(collect, receipt, DateTime.UtcNow);
        using var proof = System.Text.Json.JsonDocument.Parse(report.Audit.PayloadJson);
        Assert.Empty(SealedTombEvidence.Inspect(report.Audit.Summary, proof.RootElement.GetProperty("data")));
        var changed = System.Text.Json.Nodes.JsonNode.Parse(report.Audit.PayloadJson)!;
        changed["data"]!["prepared"]!["tombEvidence"]!["payoutPoints"] = 999;
        using var invalid = System.Text.Json.JsonDocument.Parse(changed.ToJsonString());
        Assert.Single(SealedTombEvidence.Inspect(report.Audit.Summary, invalid.RootElement.GetProperty("data")));
        var wrongTape = collect with { TombEvidence = collect.TombEvidence with { Draws = [new(100, 0)] } };
        Assert.Throws<GameWalletUnavailableException>(() => PharaohsAscentRoundPolicy.Validate(wrongTape));
    }

    private static PharaohsAscentRoundService Service(FirestoreDb database, LocalTestWallet wallet, TimeProvider clock, IGameRandomSource random) =>
        new(new PharaohsAscentRoundStore(database, random, clock), wallet, wallet);
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow.AddHours(-1);
        public override DateTimeOffset GetUtcNow() => current;
        internal void Advance() => current = current.AddSeconds(1);
    }
    private sealed class ZeroDraws : IGameRandomSource { public int Next(int maximumExclusive) => 0; }
    private sealed class HighDraws : IGameRandomSource { public int Next(int maximumExclusive) => maximumExclusive - 1; }
    private sealed class NoDraws : IGameRandomSource { public int Next(int maximumExclusive) => throw new InvalidOperationException("A restored action must not draw new choices or prizes."); }
    private sealed class ScarabDraws : IGameRandomSource
    {
        private int calls;
        public int Next(int maximumExclusive) => calls++ < 5 || maximumExclusive != 100 ? 0 : 99;
    }
}
