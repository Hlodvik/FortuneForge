using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PiratesFortune.Server;

internal sealed record PiratesNativeProgressEvidence(string OperationId, PiratesRoundBefore Before, PiratesRoundBefore After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, IReadOnlyList<PiratesGemGrant> GemAwards);

internal sealed record PiratesStoredGemEvidence(string RuleVersion, IReadOnlyDictionary<string, long> CountsBefore,
    IReadOnlyDictionary<string, long> WagerCentsBefore, IReadOnlyDictionary<string, long> CountsAfter,
    IReadOnlyDictionary<string, long> WagerCentsAfter, int FreeSpinsAwarded, string? FeatureMode, long FreeSpinWagerPoints)
{
    internal static PiratesStoredGemEvidence From(PiratesPreparedRound round)
    {
        var unit = round.Outcome.PointValueInCents;
        var progress = round.Progress;
        return new(PiratesGemBonus.RuleVersion,
            Map(round.Before.SealCounts), Wagers(round.Before.SealWagerPoints),
            Map(progress.After.SealCounts), Wagers(progress.After.SealWagerPoints),
            progress.CollectionFreeSpinsAwarded, progress.AwardedFeatureMode, progress.CollectionFreeSpinWagerPoints);

        static Dictionary<string, long> Map(IReadOnlyDictionary<string, long> counts) =>
            PiratesGemBonus.Modes.ToDictionary(mode => mode, mode => counts.GetValueOrDefault(mode), StringComparer.Ordinal);

        Dictionary<string, long> Wagers(IReadOnlyDictionary<string, long> points) =>
            PiratesGemBonus.Modes.ToDictionary(mode => mode,
                mode => PiratesRoundPolicy.ToCents(points.GetValueOrDefault(mode), unit), StringComparer.Ordinal);
    }

    internal bool Matches(PiratesStoredGemEvidence other) =>
        RuleVersion == other.RuleVersion && FreeSpinsAwarded == other.FreeSpinsAwarded &&
        FeatureMode == other.FeatureMode && FreeSpinWagerPoints == other.FreeSpinWagerPoints &&
        SameMap(CountsBefore, other.CountsBefore) && SameMap(WagerCentsBefore, other.WagerCentsBefore) &&
        SameMap(CountsAfter, other.CountsAfter) && SameMap(WagerCentsAfter, other.WagerCentsAfter);

    private static bool SameMap(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && pair.Value == value);
}

public static partial class PiratesSpinEvidence
{
    /// <summary>Identifies the protected package proof while preserving the historical replay path.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("piratesDraws", out _) ||
        data.TryGetProperty("piratesProgress", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays Pirates’ Fortune’s recorded draws and verifies the game and signed wallet progression.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var legacyAudit = Read<ClassicSlotRecordedAudit>(data, "engineAudit");
            var audit = legacyAudit.ToEngineAudit();
            var result = Read<PiratesSpinResponse>(data, "result");
            var settled = Read<SpinPayout>(data, "settledPayout");
            var source = Read<PiratesNativeProgressEvidence>(data, "piratesProgress");
            var gems = Read<PiratesStoredGemEvidence>(data, "piratesGemEvidence");
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : PiratesRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = PiratesRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            if (summary.GameId != PiratesSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
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
                    GameWalletSettlementIdentity.CreateId(PiratesFortuneEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !PiratesRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = PiratesRoundStateCodec.Write(source.Before);
            var request = new PiratesRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new PiratesRoundProgress(source.After, gems.FreeSpinsAwarded, gems.FeatureMode,
                gems.FreeSpinWagerPoints, source.GemAwards);
            var engine = new PiratesRoundEngineResult(result.ToOutcome(audit), data.GetProperty("piratesDraws").GetBytesFromBase64());
            var round = new PiratesPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            if (!PiratesStoredGemEvidence.From(round).Matches(gems)) throw InvalidEvidence();
            PiratesRoundPolicy.Validate(round);
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-pirates-native-proof-mismatch", "error",
                "Pirates’ Fortune’s reels, doubloons, gems, bonus source, or signed wallet evidence do not match the paid outcome.")];
        }
    }

    private static T Read<T>(JsonElement data, string key) where T : class =>
        data.GetProperty(key).Deserialize<T>(RoundAuditCapture.Json) ?? throw InvalidEvidence();

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Pirates’ Fortune round cannot be replayed.");
}
