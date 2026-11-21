using System.Collections;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.CasinoWar;

internal sealed record CasinoWarSavedRound(string RoundId, string UserId, long PrimaryStakeCents,
    long TieStakeCents, string[] Deck, string? Decision, string StartKey, string? DecisionKey, DateTimeOffset CreatedAt)
{
    internal CasinoWarStoreRound Restore()
    {
        var cards = Deck.Select(CardCode.Parse).ToArray();
        if (cards.Length != CasinoWarRoundEngine.WarCardsRequired)
            throw new InvalidOperationException("The Casino War deck is corrupt.");
        var opening = CasinoWarRoundEngine.Start(cards, CasinoWarMoney.ToRand(PrimaryStakeCents));
        var tie = TieStakeCents == 0 ? null : CasinoWarTieBetPaytable.Settle(opening.Opening, CasinoWarMoney.ToRand(TieStakeCents));
        return new(RoundId, UserId, Decision is null ? opening
            : CasinoWarRoundEngine.Decide(opening, CasinoWarService.ParseDecision(Decision)), tie, cards);
    }
}

/// <summary>The single codec for original deck/decision documents and package-owned rounds.</summary>
internal static class CasinoWarRoundCodec
{
    internal const string Rounds = "casinoWarRounds";
    internal const string Events = "casinoWarRoundEvents";
    internal const string BackendOwner = "casino-war-package-v1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string value) => GameSecurityEventDocuments.Hash(value);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new InvalidOperationException("The saved Casino War document is empty.");

    internal static CasinoWarSavedRound Read(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || Text(snapshot, "roundId") is not { Length: 64 } id || id != snapshot.Id ||
            Text(snapshot, "userId") is not { Length: > 0 } user || Number(snapshot, "schemaVersion") != 1 ||
            Text(snapshot, "startIdempotencyKey") is not { Length: > 0 } startKey ||
            !snapshot.TryGetValue<Timestamp>("createdAt", out var createdAt))
            throw new InvalidOperationException("The Casino War round identity is corrupt.");
        var primary = CasinoWarMoney.PrimaryCents(CasinoWarMoney.ToRand(Number(snapshot, "primaryStakeCents")));
        var tie = CasinoWarMoney.TieCents(CasinoWarMoney.ToRand(Number(snapshot, "tieStakeCents")));
        var phase = Text(snapshot, "phase");
        var saved = new CasinoWarSavedRound(id, user, primary, tie, Strings(snapshot, "deck"),
            Text(snapshot, "decision"), startKey, Text(snapshot, "decisionIdempotencyKey"), createdAt.ToDateTimeOffset());
        var round = saved.Restore().Round;
        if (phase != Phase(round.Phase) || saved.Decision is not null &&
            (string.IsNullOrWhiteSpace(saved.DecisionKey) || Number(snapshot, "primaryReturnCents") != CasinoWarMoney.ToCents(round.Settlement!.TotalReturn) ||
             Text(snapshot, "primaryOutcome") != CasinoWarService.RoundOutcomeName(round.Settlement.Outcome)))
            throw new InvalidOperationException("The Casino War settlement does not reproduce its saved cards and decision.");
        return saved;
    }

    internal static Dictionary<string, object> Write(CasinoWarSavedRound saved, DateTimeOffset now)
    {
        var round = saved.Restore().Round;
        var fields = new Dictionary<string, object>
        {
            ["roundId"] = saved.RoundId, ["userId"] = saved.UserId,
            ["primaryStakeCents"] = saved.PrimaryStakeCents, ["tieStakeCents"] = saved.TieStakeCents,
            ["deck"] = saved.Deck, ["phase"] = Phase(round.Phase), ["startIdempotencyKey"] = saved.StartKey,
            ["createdAt"] = Timestamp.FromDateTime(saved.CreatedAt.UtcDateTime),
            ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
            ["backendOwner"] = BackendOwner,
        };
        if (saved.Decision is not null)
        {
            fields["decision"] = saved.Decision;
            fields["decisionIdempotencyKey"] = saved.DecisionKey!;
            fields["primaryReturnCents"] = CasinoWarMoney.ToCents(round.Settlement!.TotalReturn);
            fields["primaryOutcome"] = CasinoWarService.RoundOutcomeName(round.Settlement.Outcome);
        }
        return fields;
    }

    internal static Dictionary<string, object> Event(CasinoWarPreparedAction prepared, DateTimeOffset now) => new()
    {
        ["roundId"] = prepared.RoundId, ["userId"] = prepared.AccountId, ["event"] = prepared.Kind,
        ["idempotencyKey"] = prepared.Key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
        ["schemaVersion"] = 1L, ["walletOperationId"] = prepared.WalletOperationId,
    };
    internal static string Phase(CasinoWarRoundPhase phase) => phase switch
    {
        CasinoWarRoundPhase.AwaitingTieDecision => "awaiting-tie-decision",
        CasinoWarRoundPhase.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
    internal static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : null;
    internal static long Number(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static string[] Strings(DocumentSnapshot snapshot, string field) =>
        snapshot.ToDictionary().GetValueOrDefault(field) is IEnumerable values
            ? values.Cast<object?>().Select(value => value as string ?? string.Empty).ToArray() : [];
}
