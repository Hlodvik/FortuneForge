using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry.Server;

public static class GoldFoundrySpinEvidence
{
    /// <summary>Verifies the sealed native report and wallet identity. Earlier evidence keeps its existing replay path.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!data.TryGetProperty("engineOutcomeSha256", out var digest)) return [];

            var audit = data.GetProperty("engineAudit").Deserialize<GoldFoundryEngineAudit>(RoundAuditCapture.Json) ?? throw Invalid();
            var result = data.GetProperty("result").Deserialize<GoldFoundrySpinResponse>(RoundAuditCapture.Json) ?? throw Invalid();
            var settled = data.GetProperty("settledPayout").Deserialize<SpinPayout>(RoundAuditCapture.Json) ?? throw Invalid();
            var crucible = data.GetProperty("crucible");
            var savedSettlement = crucible.GetProperty("settlement");
            var recorded = savedSettlement.Deserialize<GoldFoundryCrucibleSettlement>(RoundAuditCapture.Json) ?? throw Invalid();
            var outcome = result.ToOutcome(audit);
            var wagerCents = GoldFoundryRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var expected = GoldFoundryCrucibleRules.Settle(audit.EnergyBalanceBefore, result.EnergyAwarded,
                crucible.GetProperty("wageredBeforeCents").GetInt64(), wagerCents);
            var payoutCents = checked(GoldFoundryRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents) + expected.PayoutCents);
            if (summary.GameId != GoldFoundrySpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                summary.StartedAtUtc > audit.GeneratedAtUtc || summary.CompletedAtUtc < audit.GeneratedAtUtc ||
                result.ChargedWagerCents != wagerCents || data.GetProperty("isFreeSpin").GetBoolean() ||
                participant.WagerCents != wagerCents || participant.PayoutCents != payoutCents ||
                participant.NetCents != payoutCents - wagerCents || recorded != expected ||
                savedSettlement.GetProperty("pourValueCents").GetInt64() != expected.PourValueCents ||
                digest.GetString() != GoldFoundryRoundPolicy.OutcomeDigest(outcome) ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(result.GameId, participant.ActorId, summary.RoundId) ||
                GoldFoundryOutcomeReplay.Check(outcome, settled).Count != 0)
                throw Invalid();
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            return [new("slot-foundry-native-proof-mismatch", "error",
                "The Gold Foundry reel, Crucible, or wallet evidence does not match the paid outcome.")];
        }
    }

    private static InvalidOperationException Invalid() => new("The Gold Foundry evidence does not match its cent settlement.");
}
