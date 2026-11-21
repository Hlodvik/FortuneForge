using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault.Server;

internal sealed record MidnightVaultNativeProgressEvidence(string OperationId, long LegacyRemainingBefore, bool SpendsLegacy,
    long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int SpecialPointsBefore, int SpecialPointsAfter,
    long EnergyAfter, int CollectionFreeSpinsAwarded);

public static class MidnightVaultSpinEvidence
{
    /// <summary>Rechecks a native paid report. Earlier envelopes keep their existing archive reader.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!data.TryGetProperty("engineOutcomeSha256", out var digest))
            {
                if (data.TryGetProperty("midnightProgress", out _)) throw Invalid();
                return [];
            }

            var audit = Read<MidnightVaultEngineAudit>(data, "engineAudit");
            var result = Read<MidnightVaultSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var bonus = Read<MidnightVaultBonusEvidence>(data, "midnightBonusEvidence");
            var source = Read<MidnightVaultNativeProgressEvidence>(data, "midnightProgress");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : MidnightVaultRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = MidnightVaultRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != MidnightVaultSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(audit.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != checked(payoutCents - wagerCents) ||
                data.GetProperty("admittedMathModelId").GetString() != result.PaytableId ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != audit.FreeSpinFeatureMode ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(result.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining is not { } legacyRemainingAfter ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !MidnightVaultRoundRequest.IsValidOperationId(source.OperationId) ||
                JsonSerializer.Serialize(settled, RoundAuditCapture.Json) != JsonSerializer.Serialize(result.Payout, RoundAuditCapture.Json))
                throw Invalid();

            var before = new MidnightVaultRoundBefore(bonus.RoundsBeforeAdmission, source.LegacyRemainingBefore,
                source.SpecialPointsBefore, audit.EnergyBalanceBefore, audit.PityMissesBefore,
                bonus.CollectionCountsBefore, bonus.CollectionWagerPointsBefore);
            _ = MidnightVaultRoundStateCodec.Write(before);
            var after = new MidnightVaultRoundBefore(bonus.RoundsAfterSettlement, legacyRemainingAfter,
                source.SpecialPointsAfter, source.EnergyAfter, result.ConsecutiveFiveMisses,
                bonus.CollectionCountsAfter, bonus.CollectionWagerPointsAfter);
            var request = new MidnightVaultRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new MidnightVaultRoundProgress(after, bonus, source.CollectionFreeSpinsAwarded);
            var round = new MidnightVaultPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, before, result.ToOutcome(audit), progress, digest.GetString() ?? throw Invalid(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            MidnightVaultRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-midnight-native-proof-mismatch", "error",
                "The Midnight Vault reels, bonus source and progression, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw Invalid();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException Invalid() => new("The Midnight Vault archived round cannot be replayed.");
}
