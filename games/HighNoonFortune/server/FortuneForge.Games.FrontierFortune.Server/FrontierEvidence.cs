using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.FrontierFortune.Server;

public static class FrontierEvidence
{
    public static bool IsNative(RoundAuditSummary summary) =>
        summary.AuditorVersion is "frontier-replay-v1" or "frontier-replay-v2" || summary.EvidenceKind is "frontier-fortune-v1" or "frontier-fortune-v2";

    public static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, JsonElement data)
    {
        List<RoundAuditFinding> findings = [];
        try
        {
            var version = data.GetProperty("contractVersion").GetInt32();
            if (version is not (1 or 2) || summary.GameId != FrontierPaidRouteContract.AuditGameId || summary.AuditorVersion != $"frontier-replay-v{version}" ||
                summary.EvidenceKind != $"frontier-fortune-v{version}" ||
                data.GetProperty("kind").GetString() != "frontier-fortune")
                throw new InvalidOperationException("The Frontier evidence identity or version is invalid.");
            var round = data.GetProperty("round").Deserialize<FrontierPreparedRound>(FrontierRules.Json)
                ?? throw new InvalidOperationException("The prepared round is absent.");
            var receipt = data.GetProperty("wallet").Deserialize<GameWalletSettlementReceipt>(FrontierRules.Json)
                ?? throw new InvalidOperationException("The wallet proof is absent.");
            FrontierRoundPolicy.Validate(round);
            if ((round.Outlaw is null ? 1 : 2) != version) throw new InvalidOperationException("Mixed Frontier evidence versions.");
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, FrontierFortuneEngine.GameId, round.AccountId,
                    FrontierRoundIdentity.WalletRequest(round)) || receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames ||
                    receipt.LegacyFreeGamesCountMismatch)
                throw new InvalidOperationException("The Frontier wallet proof does not match its prepared award.");
            if (summary.RoundId != round.AdmissionId || summary.TableId != round.AdmissionId || summary.RoundNumber != 1 ||
                summary.StartedAtUtc.Ticks / 10 != round.StartedAtUtc.Ticks / 10 || summary.CompletedAtUtc < summary.StartedAtUtc ||
                summary.CompletedAtUtc.Kind != DateTimeKind.Utc || receipt.SettledAtUtc.UtcDateTime.Ticks / 10 < round.StartedAtUtc.Ticks / 10 ||
                receipt.SettledAtUtc.UtcDateTime.Ticks / 10 > summary.CompletedAtUtc.Ticks / 10 || summary.Participants.Count != 1)
                throw new InvalidOperationException("The Frontier summary does not identify its exact round.");
            var participant = summary.Participants[0];
            if (!participant.IsAccountBacked || participant.ActorId != round.AccountId ||
                participant.WagerCents != round.WagerCents || participant.PayoutCents != round.PayoutCents ||
                participant.NetCents != round.PayoutCents - round.WagerCents)
                throw new InvalidOperationException("The Frontier participant totals do not match its wallet proof.");
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or KeyNotFoundException or OverflowException or NullReferenceException)
        {
            findings.Add(new("frontier-evidence-invalid", "error", error.Message));
        }
        return findings;
    }
}
