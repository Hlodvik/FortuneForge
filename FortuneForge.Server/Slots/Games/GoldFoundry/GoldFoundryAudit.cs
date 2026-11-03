using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

internal static class GoldFoundryAudit
{
    internal static RoundAuditFinding? CheckModel(SlotSpinEngineAudit audit)
    {
        var isFoundry = GoldFoundrySlotDefinitions.IsModel(audit.BaseGame);
        if (isFoundry != (audit.EngineVersion == GoldFoundryReelCollector.EngineVersion))
            return new("slot-foundry-collector-version-mismatch", "error",
                "The collector rule is not bound to its captured Foundry model.");
        if (audit.CrucibleRuleVersion != (isFoundry ? GoldFoundryCrucible.RuleVersion : null))
            return new("slot-crucible-rule-mismatch", "error",
                "The saved crucible rule does not match this game's engine outcome.");
        return null;
    }

    internal static RoundAuditFinding? CheckReelWindow(SpinResult result, string? activeFeatureMode,
        IReadOnlyList<IReadOnlyList<string>>? rawGeneratedReels)
    {
        if (activeFeatureMode is not null ||
            rawGeneratedReels is not null && (rawGeneratedReels.Count != result.Reels.Count ||
                rawGeneratedReels.Where((reel, index) => !reel.SequenceEqual(result.Reels[index])).Any()))
            return new("slot-foundry-reel-mismatch", "error",
                "The Foundry window must match its authored strips.");
        return null;
    }

    internal static IReadOnlyList<RoundAuditFinding> InspectDemo(DemoSpinRequest request,
        SpinResult engine, SpinResult response, SlotSpinEngineAudit audit)
    {
        var findings = new List<RoundAuditFinding>();
        if (request.UseFreeSpin || audit.IsDerivedFreeSpin || audit.FreeSpinFeatureMode is not null ||
            audit.EnergyBalanceBefore != request.EnergyBalance)
            findings.Add(new("slot-demo-engine-input-mismatch", "error",
                "The saved demo engine inputs differ from the accepted request."));

        var expected = GoldFoundryCrucible.SettleDemo(engine, request.EnergyBalance, request.CrucibleWageredCents);
        if (response.IsFreeSpin || response.FreeSpinsAwarded != engine.FreeSpinsAwarded ||
            response.CollectionFreeSpinsAwarded != 0 || response.FreeSpinsRemaining != 0 ||
            response.FreeSpinWagerPoints is not null || response.FreeSpinFeatureMode is not null ||
            response.EnergyMultiplierApplied || response.PayoutMultiplier != 1m ||
            response.SpecialPointsBalance != 0 || response.SealCollections.Count != 0 ||
            response.EnergyBalance != expected.EnergyBalance ||
            response.CrucibleWageredCents != expected.CrucibleWageredCents ||
            response.CruciblePourValueCents != expected.CruciblePourValueCents ||
            response.CruciblePoured != expected.CruciblePoured ||
            response.CruciblePayoutCents != expected.CruciblePayoutCents)
            findings.Add(new("slot-demo-progression-mismatch", "error",
                "The saved demo crucible state does not replay from its request and engine outcome."));
        return findings;
    }

    internal static IReadOnlyList<RoundAuditFinding> InspectPaidSettlement(
        JsonElement data, SpinResult result, SlotSpinEngineAudit audit, SpinPayout settled,
        RoundAuditSummary summary)
    {
        var evidence = data.GetProperty("crucible");
        var wagerBefore = evidence.GetProperty("wageredBeforeCents").GetInt64();
        var recorded = evidence.GetProperty("settlement").Deserialize<GoldFoundryCrucibleSettlement>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing Gold Foundry crucible settlement.");
        var isFree = data.GetProperty("isFreeSpin").GetBoolean();
        var wagerCents = RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var expected = GoldFoundryCrucible.Settle(audit.EnergyBalanceBefore, result.EnergyAwarded, wagerBefore,
            wagerCents);
        var payoutCents = checked(RandMoney.PointsToCents(settled.TotalPoints, result.PointValueInCents) + expected.PayoutCents);
        if (isFree || result.IsFreeSpin || audit.IsDerivedFreeSpin || audit.FreeSpinFeatureMode is not null ||
            expected != recorded ||
            summary.Participants.Count != 1 || summary.Participants[0].WagerCents != wagerCents ||
            summary.Participants[0].PayoutCents != payoutCents ||
            summary.Participants[0].NetCents != payoutCents - wagerCents)
            return [new("slot-crucible-financial-mismatch", "error",
                "The Gold Foundry wager cycle and payout do not replay from the accepted spin.")];
        return [];
    }

}
