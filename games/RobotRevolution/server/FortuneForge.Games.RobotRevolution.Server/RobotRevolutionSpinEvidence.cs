using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution.Server;

internal sealed record RobotRevolutionNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId);

public static partial class RobotRevolutionSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("robotRevolutionDraws", out _) ||
        data.TryGetProperty("robotRevolutionProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Fifth Sun's recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<RobotRevolutionSpinResponse>(data, "result");
            var feature = RobotRevolutionEngine.DescribeFeature(result.ToOutcome(audit));
            if (JsonSerializer.Serialize(feature, RoundAuditCapture.Json) != JsonSerializer.Serialize(result.SpinFeature, RoundAuditCapture.Json))
                throw InvalidEvidence();
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<RobotRevolutionNativeProgressEvidence>(data, "robotRevolutionProgress");
            if (result.PaidMode != FifthSunPaidModeState.Read(source.After)) throw InvalidEvidence();
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : RobotRevolutionRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = RobotRevolutionRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != RobotRevolutionSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean() ||
                data.GetProperty("payoutMultiplier").GetDecimal() != 1m ||
                JsonSerializer.Serialize(settled, RoundAuditCapture.Json) != JsonSerializer.Serialize(result.Payout, RoundAuditCapture.Json) ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !RobotRevolutionRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = RobotRevolutionRoundStateCodec.Storage.Write(source.Before);
            var request = new RobotRevolutionRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new RobotRevolutionRoundProgress(source.After);
            var engine = new RobotRevolutionRoundEngineResult(result.ToOutcome(audit), data.GetProperty("robotRevolutionDraws").GetBytesFromBase64());
            var round = new RobotRevolutionPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            RobotRevolutionRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-robot-revolution-native-proof-mismatch", "error",
                "Fifth Sun's reels, Sunwheel guardian alignment, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Fifth Sun round cannot be replayed.");
}
