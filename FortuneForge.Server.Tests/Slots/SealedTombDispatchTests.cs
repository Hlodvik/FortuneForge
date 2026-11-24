using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PharaohsAscent;
using FortuneForge.Games.PharaohsAscent.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SealedTombDispatchTests
{
    [Fact]
    public void NonReelSettlementUsesItsGameOwnedReplayBeforeTheGenericSlotAuditor()
    {
        const string account = "sealed-tomb-dispatch-unit";
        var before = new ClassicSlotRoundState(0, 0, null, 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>())
        {
            NativeStateJson = SealedTombState.Serialize(SealedTombState.Empty with
                { Tickets = [new("sealed-tomb-dispatch-ticket", 100)] }),
        };
        var entered = PharaohsAscentRoundPolicy.Prepare(account,
            new("sealed-tomb-enter-dispatch", 0, false) { TombAction = new("enter", 0) },
            before, DateTime.UtcNow, null, new ZeroDraws());
        var collect = PharaohsAscentRoundPolicy.Prepare(account,
            new("sealed-tomb-collect-dispatch", 0, false) { TombAction = new("collect", 1) },
            entered.Progress.After, DateTime.UtcNow, null, new NoDraws());
        var id = collect.TombEvidence!.EventId.ToString("N");
        var wallet = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, account, id),
            null, id + "-payout", 100_000, 100_000, 112_500, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGamesRemaining: 0);
        var report = SealedTombEvidence.Capture(collect, wallet, DateTime.UtcNow);
        Assert.True(SealedTombEvidence.IsNative(report.Audit.Summary));
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(report.Audit.Summary, report.Audit.PayloadJson));
        var tampered = JsonNode.Parse(report.Audit.PayloadJson)!;
        tampered["data"]!["wallet"]!["balanceAfterCents"] = 125_000;
        Assert.Equal("pharaoh-sealed-tomb-proof-mismatch", Assert.Single(
            SlotEvidenceReplayAudit.Inspect(report.Audit.Summary, tampered.ToJsonString())).Code);
    }

    private sealed class ZeroDraws : IGameRandomSource { public int Next(int maximumExclusive) => 0; }
    private sealed class NoDraws : IGameRandomSource { public int Next(int maximumExclusive) => throw new InvalidOperationException("A collect action cannot draw an outcome."); }
}
