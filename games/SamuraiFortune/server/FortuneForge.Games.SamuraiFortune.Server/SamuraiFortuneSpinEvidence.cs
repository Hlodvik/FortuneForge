using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.SamuraiFortune.Server;

internal sealed record SamuraiFortuneNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints, long MeterBalanceBeforeReset, long EnergyAddedToMeter, bool EnergyMultiplierApplied);

public static partial class SamuraiFortuneSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("samuraiFortuneDraws", out _) ||
        data.TryGetProperty("samuraiFortuneProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Samurai Fortune’ recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<SamuraiFortuneSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<SamuraiFortuneNativeProgressEvidence>(data, "samuraiFortuneProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : SamuraiFortuneRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = SamuraiFortuneRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != SamuraiFortuneSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean() ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(SamuraiFortuneEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !SamuraiFortuneRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = SamuraiFortuneRoundStateCodec.Storage.Write(source.Before);
            var request = new SamuraiFortuneRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var energy = new ClassicSlotEnergySettlement(settled, source.After.Energy, source.MeterBalanceBeforeReset,
                source.EnergyAddedToMeter, source.EnergyMultiplierApplied, data.GetProperty("payoutMultiplier").GetDecimal());
            var progress = new SamuraiFortuneRoundProgress(source.After, energy, source.CollectionFreeSpinsAwarded,
                source.AwardedFeatureMode, source.CollectionFreeSpinWagerPoints);
            var engine = new SamuraiFortuneRoundEngineResult(result.ToOutcome(audit), data.GetProperty("samuraiFortuneDraws").GetBytesFromBase64());
            var round = new SamuraiFortunePreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            SamuraiFortuneRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-samurai-fortune-native-proof-mismatch", "error",
                "Samurai Fortune’ reels, collector, energy, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Samurai Fortune round cannot be replayed.");
}
