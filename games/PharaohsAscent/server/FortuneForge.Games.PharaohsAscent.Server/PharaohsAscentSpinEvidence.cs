using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.PharaohsAscent.Server;

internal sealed record PharaohsAscentNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints, long MeterBalanceBeforeReset, long EnergyAddedToMeter, bool EnergyMultiplierApplied);

public static partial class PharaohsAscentSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("pharaohsAscentDraws", out _) ||
        data.TryGetProperty("pharaohsAscentProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Pharaoh’s Ascent’ recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<PharaohsAscentSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<PharaohsAscentNativeProgressEvidence>(data, "pharaohsAscentProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : PharaohsAscentRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = PharaohsAscentRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != PharaohsAscentSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean() ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !PharaohsAscentRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = PharaohsAscentRoundStateCodec.Storage.Write(source.Before);
            var request = new PharaohsAscentRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var energy = new ClassicSlotEnergySettlement(settled, source.After.Energy, source.MeterBalanceBeforeReset,
                source.EnergyAddedToMeter, source.EnergyMultiplierApplied, data.GetProperty("payoutMultiplier").GetDecimal());
            var progress = new ClassicSlotActiveRoundProgress(source.After, energy, source.CollectionFreeSpinsAwarded,
                source.AwardedFeatureMode, source.CollectionFreeSpinWagerPoints);
            var engine = new PharaohsAscentRoundEngineResult(result.ToOutcome(audit), data.GetProperty("pharaohsAscentDraws").GetBytesFromBase64());
            var round = new PharaohsAscentPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            PharaohsAscentRoundPolicy.Validate(round);
            if (audit.EngineVersion != PharaohsAscentEngine.LegacyEngineVersion &&
                (JsonSerializer.Serialize(result.SpinFeature, RoundAuditCapture.Json) !=
                    JsonSerializer.Serialize(PharaohsAscentFeature.From(round), RoundAuditCapture.Json) ||
                JsonSerializer.Serialize(result.GameFeatureState, RoundAuditCapture.Json) !=
                    JsonSerializer.Serialize(PharaohsAscentRoundStateReply.From(source.After,
                        includeTomb: PharaohsAscentEngine.UsesSealedTombFunding(audit.EngineVersion),
                        includeFundingRequirement: audit.EngineVersion == PharaohsAscentEngine.EngineVersion).GameFeatureState, RoundAuditCapture.Json)))
                throw InvalidEvidence();
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-pharaohs-ascent-native-proof-mismatch", "error",
                "Pharaoh’s Ascent’ reels, collector, energy, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Pharaoh’s Ascent round cannot be replayed.");
}
