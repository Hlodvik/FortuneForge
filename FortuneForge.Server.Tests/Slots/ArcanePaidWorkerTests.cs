// Edited: 2026-11-18T18:15:15-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcanePaidWorkerTests
{
    private const string UserId = "local-arcane-worker-test";
    private const string GameId = ArcaneArchivesEngine.GameId;
    private const long WagerPoints = 12;

    [Fact]
    public void OnlyTheRarePotionEngineAcceptsTheVariantInWorkerRequests()
    {
        var arcane = new PaidArcaneSpinOptions(ArcaneArchivesEngine.PreviousBalancedPotionEngineVersion, false,
            new PaidArcanePotion(1, "MANDRAKE_ROOT", 10) { VariantId = ArcanePotions.AstralConvergence });
        var request = new PaidSlotOutcomeRequest(Guid.NewGuid(), GameId, UserId, WagerPoints,
            false, false, 0, null, 0, null);
        var resolver = new ArcanePaidSlotOutcomes(new SeededRandom());
        Assert.Throws<ArgumentException>(() => resolver.Resolve(arcane.WriteTo(request)));
        var legacy = resolver.Resolve((arcane with { Potion = null }).WriteTo(request));
        Assert.Equal(ArcaneArchivesEngine.PreviousBalancedPotionEngineVersion,
            legacy.Outcome.GetProperty("audit").GetProperty("engineVersion").GetString());
        var current = resolver.Resolve((arcane with { EngineVersion = ArcaneArchivesEngine.EngineVersion }).WriteTo(request));
        Assert.Equal(ArcaneArchivesEngine.EngineVersion,
            current.Outcome.GetProperty("audit").GetProperty("engineVersion").GetString());
    }

    [Fact]
    public void UnversionedHostRequestStillReceivesTheLiveLightningModel()
    {
        var request = new PaidSlotOutcomeRequest(Guid.NewGuid(), GameId, UserId, WagerPoints,
            false, false, 0, null, 0, null);
        var legacy = new ArcanePaidSlotOutcomes(new SeededRandom());
        var response = Enumerable.Range(0, 1000).Select(_ => legacy.Resolve(request))
            .First(response => response.Outcome.GetProperty("payout").GetProperty("paylines").EnumerateArray()
                .Any(line => line.GetProperty("paylineId").GetInt32() == ArcaneLightningCollector.PaylineId));
        Assert.Equal(PaidSlotOutcomeProtocol.ClassicSlotKind, response.OutcomeKind);
        Assert.Equal(request.AdmissionId, response.AdmissionId);
        var outcome = response.Outcome.Deserialize<ClassicSlotSpinOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(outcome);
        Assert.Equal("arcane-lightning-collector-v1", outcome.Audit.EngineVersion);
        Assert.Equal("arcane-archives-lightning-reels-v1", outcome.ReelSetId);
        var lightning = ArcaneLightningCollector.Calculate(outcome.Reels, WagerPoints);
        Assert.NotNull(lightning);
        var line = Assert.Single(outcome.Payout.Paylines, line => line.PaylineId == ArcaneLightningCollector.PaylineId);
        Assert.Equal(lightning.TotalPoints, line.AmountPoints);
    }

    private sealed class SeededRandom : IGameRandomSource
    {
        private readonly Random random = new(20261118);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
