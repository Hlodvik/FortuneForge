using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm.Server;

internal sealed record RainbowNativeProgressEvidence(string OperationId, RainbowRoundBefore Before, RainbowRoundBefore After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int CollectionFreeSpinsAwarded);

public static partial class RainbowSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("rainbowDraws", out _) ||
        data.TryGetProperty("rainbowProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Rainbow Realm's recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<RainbowSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<RainbowNativeProgressEvidence>(data, "rainbowProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : RainbowRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = RainbowRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != RainbowSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean() ||
                audit.EnergyBalanceBefore != 0 || data.GetProperty("payoutMultiplier").GetDecimal() != 1m ||
                JsonSerializer.Serialize(settled, RoundAuditCapture.Json) != JsonSerializer.Serialize(result.Payout, RoundAuditCapture.Json) ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(RainbowRealmEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !RainbowRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = RainbowRoundStateCodec.Write(source.Before);
            var request = new RainbowRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new RainbowRoundProgress(source.After, source.CollectionFreeSpinsAwarded);
            var engine = new RainbowRoundEngineResult(result.ToOutcome(audit), data.GetProperty("rainbowDraws").GetBytesFromBase64());
            var round = new RainbowPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            RainbowRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-rainbow-native-proof-mismatch", "error",
                "Rainbow Realm's reels, charm harvest, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Rainbow Realm round cannot be replayed.");
}
