using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.DesertTreasures.Server;

internal sealed record DesertTreasuresNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints, long MeterBalanceBeforeReset, long EnergyAddedToMeter, bool EnergyMultiplierApplied);

public static partial class DesertTreasuresSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("desertTreasuresDraws", out _) ||
        data.TryGetProperty("desertTreasuresProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Desert Treasures’ recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<DesertTreasuresSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<DesertTreasuresNativeProgressEvidence>(data, "desertTreasuresProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : DesertTreasuresRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = DesertTreasuresRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != DesertTreasuresSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean() ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(DesertTreasuresEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !DesertTreasuresRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = DesertTreasuresRoundStateCodec.Storage.Write(source.Before);
            var request = new DesertTreasuresRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var energy = new ClassicSlotEnergySettlement(settled, source.After.Energy, source.MeterBalanceBeforeReset,
                source.EnergyAddedToMeter, source.EnergyMultiplierApplied, data.GetProperty("payoutMultiplier").GetDecimal());
            var progress = new DesertTreasuresRoundProgress(source.After, energy, source.CollectionFreeSpinsAwarded,
                source.AwardedFeatureMode, source.CollectionFreeSpinWagerPoints);
            var engine = new DesertTreasuresRoundEngineResult(result.ToOutcome(audit), data.GetProperty("desertTreasuresDraws").GetBytesFromBase64());
            var round = new DesertTreasuresPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            DesertTreasuresRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-desert-treasures-native-proof-mismatch", "error",
                "Desert Treasures’ reels, collector, energy, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Desert Treasures round cannot be replayed.");
}
