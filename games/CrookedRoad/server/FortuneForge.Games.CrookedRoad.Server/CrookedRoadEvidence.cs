using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.CrookedRoad.Server;

public sealed record CrookedRoadDraw(int Bound, int Value);
public sealed class CrookedRoadRandom : IGameRandomSource
{
    public int Next(int maximumExclusive) => RandomNumberGenerator.GetInt32(maximumExclusive);
}

internal sealed class CrookedRoadTape(IGameRandomSource? source, IReadOnlyList<CrookedRoadDraw>? recorded = null) : IGameRandomSource
{
    private readonly List<CrookedRoadDraw> captured = [];
    private int cursor;
    public int Next(int maximumExclusive)
    {
        if (maximumExclusive is < 1 or > 10_000) throw new InvalidOperationException("Invalid Crooked Road draw bound.");
        if (source is not null)
        {
            var value = source.Next(maximumExclusive);
            if ((uint)value >= (uint)maximumExclusive) throw new InvalidOperationException("Invalid Crooked Road random index.");
            captured.Add(new(maximumExclusive, value));
            return value;
        }
        if (recorded is null || cursor >= recorded.Count || recorded[cursor].Bound != maximumExclusive ||
            (uint)recorded[cursor].Value >= (uint)maximumExclusive) throw new InvalidOperationException("Crooked Road draws cannot replay.");
        return recorded[cursor++].Value;
    }
    public CrookedRoadDraw[] Finish()
    {
        if (source is null && cursor != recorded!.Count) throw new InvalidOperationException("Unused Crooked Road replay draws.");
        return source is null ? recorded!.ToArray() : captured.ToArray();
    }
}

public static class CrookedRoadEvidence
{
    internal static string Hash<T>(T value) => GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(value, RoundAuditCapture.Json));
    internal static void Validate(CrookedRoadPreparedRound round)
    {
        if (round.AdmissionId == Guid.Empty || round.SpinId == Guid.Empty || string.IsNullOrWhiteSpace(round.AccountId) ||
            round.Outcome.GeneratedAtUtc.Kind != DateTimeKind.Utc || round.StartedAtUtc.Kind != DateTimeKind.Utc ||
            round.Outcome.GeneratedAtUtc < round.StartedAtUtc || round.Request.UseSpecialBoost ||
            round.StateBefore.LegacyRemaining != 0 || round.StateBefore.NativeRemaining != 0 || round.StateBefore.Carry is not null)
            throw new InvalidOperationException("Invalid Crooked Road preparation.");
        var before = CrookedRoadState.Parse(round.StateBefore.NativeStateJson);
        var tape = new CrookedRoadTape(null, round.Draws);
        var replay = CrookedRoadGame.Spin(before, round.Request.WagerPoints, round.Request.UseFreeSpin,
            round.SpinId, round.GeneratedAtUtc, tape);
        _ = tape.Finish();
        if (Hash(replay) != round.OutcomeSha256 || Hash(round.Outcome) != round.OutcomeSha256 ||
            !round.StateAfter.Matches(round.StateBefore with { NativeStateJson = CrookedRoadState.Serialize(replay.After) }) ||
            round.WagerCents != (round.Request.UseFreeSpin ? 0 : round.Request.WagerPoints) || round.PayoutCents != replay.Payout.TotalPoints)
            throw new InvalidOperationException("Crooked Road preparation differs from its recorded outcome.");
    }

    public static IReadOnlyList<RoundAuditFinding> Inspect(JsonElement data)
    {
        try
        {
            var round = data.GetProperty("prepared").Deserialize<CrookedRoadPreparedRound>(RoundAuditCapture.Json)
                ?? throw new InvalidOperationException("Missing Crooked Road evidence.");
            Validate(round);
            return [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or
            KeyNotFoundException or OverflowException or NullReferenceException)
        {
            return [new("crooked-road-native-proof-mismatch", "error", "Crooked Road's captured draws, funded stake or feature state do not replay.")];
        }
    }

    public static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, JsonElement data)
    {
        var engineFindings = Inspect(data);
        if (engineFindings.Count != 0) return engineFindings;
        try
        {
            var round = data.GetProperty("prepared").Deserialize<CrookedRoadPreparedRound>(RoundAuditCapture.Json)!;
            var wallet = data.GetProperty("wallet").Deserialize<FortuneForge.Games.Abstractions.Wallet.GameWalletSettlementReceipt>(RoundAuditCapture.Json)!;
            var transactionIds = GameWalletTransactionIds.Create(round.SpinId.ToString("N"), wallet.SettlementId,
                CrookedRoadRoundStore.WalletOptions.TransactionIdLayout);
            if (summary.GameId != CrookedRoadPaidRouteContract.AuditGameId || summary.RoundId != round.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } participant] || participant.ActorId != round.AccountId ||
                participant.WagerCents != round.WagerCents || participant.PayoutCents != round.PayoutCents ||
                participant.NetCents != checked(round.PayoutCents - round.WagerCents) ||
                summary.StartedAtUtc.Ticks / 10 != round.StartedAtUtc.Ticks / 10 ||
                summary.CompletedAtUtc < round.GeneratedAtUtc || wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames ||
                wallet.LegacyFreeGamesCountMismatch || wallet.LegacyFreeGamesRemaining is not (null or 0) ||
                wallet.SettlementId != FortuneForge.Games.Abstractions.Wallet.GameWalletSettlementIdentity.CreateId(CrookedRoadRules.GameId,
                    round.AccountId, round.SpinId.ToString("N")) || data.GetProperty("walletSettlementId").GetString() != wallet.SettlementId ||
                wallet.WagerTransactionId != (round.WagerCents > 0 ? transactionIds.WagerId : null) ||
                wallet.PayoutTransactionId != (round.PayoutCents > 0 ? transactionIds.PayoutId : null) ||
                wallet.LegacyFreeGameTransactionId is not null || wallet.BalanceBeforeCents < 0 ||
                wallet.BalanceAfterWagerCents != checked(wallet.BalanceBeforeCents - round.WagerCents) ||
                wallet.BalanceAfterCents != checked(wallet.BalanceAfterWagerCents + round.PayoutCents))
                throw new InvalidOperationException("Crooked Road settlement differs from its protected evidence.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or
            KeyNotFoundException or OverflowException or NullReferenceException)
        { return [new("crooked-road-settlement-proof-mismatch", "error", "Crooked Road's wallet receipt and participant amounts differ from the replayed outcome.")]; }
    }
}
