using System.Text.Json;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Server;

// Only the revealed prefix is persisted. A fresh deck is used for every round.
internal sealed record PersistedAndarBaharRound(int SchemaVersion, string RoundId, string UserId, string IdempotencyKey,
    string Side, long StakeCents, string[] RevealedCards, string Winner, long ProfitCents, long TotalReturnCents,
    long BalanceAfterWagerCents, long BalanceAfterSettlementCents, DateTimeOffset CreatedAtUtc);

internal static class AndarBaharPersistence
{
    public static PersistedAndarBaharRound Encode(AndarBaharStoredRound stored)
    {
        var settlement = AndarBaharRules.Settle(stored.Side, stored.StakeCents, stored.Round);
        return new(1, stored.RoundId, stored.UserId, stored.IdempotencyKey, AndarBaharService.Name(stored.Side), stored.StakeCents,
            new[] { stored.Round.Joker }.Concat(stored.Round.Deals.Select(deal => deal.Card)).Select(CardCode.Format).ToArray(),
            AndarBaharService.Name(stored.Round.Winner), settlement.ProfitCents, settlement.TotalReturnCents,
            stored.BalanceAfterWagerCents, stored.BalanceAfterSettlementCents, stored.CreatedAtUtc);
    }
    public static AndarBaharStoredRound Decode(PersistedAndarBaharRound data)
    {
        try
        {
            AndarBaharService.ValidateUser(data.UserId);
            AndarBaharService.ValidateKey(data.IdempotencyKey);
            if (data.SchemaVersion != 1 || data.RoundId != AndarBaharService.LookupKey(data.UserId, data.IdempotencyKey)) throw Corrupt();
            var side = data.Side switch { "andar" => AndarBaharSide.Andar, "bahar" => AndarBaharSide.Bahar, _ => throw Corrupt() };
            AndarBaharMoney.Validate(side, data.StakeCents);
            if (data.RevealedCards is null || data.RevealedCards.Length is < 2 or > 50) throw Corrupt();
            var prefix = data.RevealedCards.Select(CardCode.Parse).ToArray();
            if (prefix.Distinct().Count() != prefix.Length) throw Corrupt();
            var reconstructed = prefix.Concat(StandardDeck.Create().Where(card => !prefix.Contains(card))).ToArray();
            var round = AndarBaharRules.Deal(reconstructed);
            var settlement = AndarBaharRules.Settle(side, data.StakeCents, round);
            if (round.Deals.Count + 1 != prefix.Length || AndarBaharService.Name(round.Winner) != data.Winner ||
                settlement.ProfitCents != data.ProfitCents || settlement.TotalReturnCents != data.TotalReturnCents ||
                data.BalanceAfterWagerCents < 0 || checked(data.BalanceAfterWagerCents + data.TotalReturnCents) != data.BalanceAfterSettlementCents)
                throw Corrupt();
            return new(data.RoundId, data.UserId, data.IdempotencyKey, side, data.StakeCents, round,
                data.BalanceAfterWagerCents, data.BalanceAfterSettlementCents, data.CreatedAtUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or NullReferenceException)
        { throw new InvalidOperationException("The persisted Andar Bahar settlement is corrupt.", exception); }
    }
    public static string Serialize(AndarBaharStoredRound stored) => JsonSerializer.Serialize(Encode(stored));
    public static AndarBaharStoredRound Deserialize(string json)
    {
        try { return Decode(JsonSerializer.Deserialize<PersistedAndarBaharRound>(json) ?? throw Corrupt()); }
        catch (JsonException exception) { throw new InvalidOperationException("The persisted Andar Bahar record is corrupt.", exception); }
    }
    private static InvalidOperationException Corrupt() => new("The persisted Andar Bahar settlement is corrupt.");
}
