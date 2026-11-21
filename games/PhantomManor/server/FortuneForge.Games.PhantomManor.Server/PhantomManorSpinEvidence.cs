using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PhantomManor.Server;

internal sealed record PhantomManorNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId);

public static partial class PhantomManorSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("phantomManorDraws", out _) ||
        data.TryGetProperty("phantomManorProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Phantom Manor's recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<PhantomManorSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<PhantomManorNativeProgressEvidence>(data, "phantomManorProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : PhantomManorRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = PhantomManorRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != PhantomManorSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
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
                    GameWalletSettlementIdentity.CreateId(PhantomManorEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !PhantomManorRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = PhantomManorRoundStateCodec.Storage.Write(source.Before);
            var request = new PhantomManorRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new PhantomManorRoundProgress(source.After);
            var engine = new PhantomManorRoundEngineResult(result.ToOutcome(audit), data.GetProperty("phantomManorDraws").GetBytesFromBase64());
            var round = new PhantomManorPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            PhantomManorRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-phantom-manor-native-proof-mismatch", "error",
                "Phantom Manor's reels, direct-value collector, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Phantom Manor round cannot be replayed.");
}
