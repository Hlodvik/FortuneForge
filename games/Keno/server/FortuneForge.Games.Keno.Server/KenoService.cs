using System.Security.Cryptography;

namespace FortuneForge.Games.Keno.Server;

public sealed class KenoService(IKenoStore store, TimeProvider timeProvider, Func<KenoDraw>? createDraw = null)
{
    private readonly Func<KenoDraw> createDraw = createDraw ?? CreateDraw;

    public async Task<KenoRoundResponse> StartAsync(string userId, CreateKenoRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateKey(idempotencyKey);
        var ticket = new KenoTicket(request.Ticket?.Numbers ?? throw new ArgumentException("Choose from one through ten Keno numbers.", nameof(request)));
        var wagerCents = KenoMoney.ToWagerCents(request.Wager);
        return ToResponse(await store.StartAsync(userId, idempotencyKey, ticket, wagerCents, createDraw(), timeProvider.GetUtcNow(), cancellationToken));
    }

    public async Task<KenoRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new KenoRoundNotFoundException());
    }

    public static KenoRoundResponse ToResponse(KenoStoreResult result)
    {
        var round = result.Round;
        var wager = KenoMoney.ToRand(round.WagerCents);
        var payout = KenoMoney.ToRand(round.PayoutCents);
        return new KenoRoundResponse(
            round.RoundId,
            KenoMoney.ToRand(result.BalanceCents),
            "completed",
            new KenoTicketResponse(round.Ticket.Numbers),
            new KenoDrawResponse(round.Draw.Numbers),
            round.HitCount,
            wager,
            payout,
            payout - wager,
            round.PayoutCents > 0 ? $"R{payout:0.00} return" : "No win");
    }

    public static IReadOnlyList<KenoPrizeTierResponse> Paytable() => StandardKenoPaytable.Tiers
        .Select(tier => new KenoPrizeTierResponse(tier.Spots, tier.Hits, tier.Multiplier))
        .ToArray();

    private static KenoDraw CreateDraw()
    {
        var numbers = Enumerable.Range(1, KenoTicket.MaximumNumber).ToArray();
        for (var index = 0; index < KenoDraw.DrawCount; index++)
        {
            var replacement = RandomNumberGenerator.GetInt32(index, numbers.Length);
            (numbers[index], numbers[replacement]) = (numbers[replacement], numbers[index]);
        }
        return new KenoDraw(numbers.Take(KenoDraw.DrawCount));
    }

    private static void ValidateKey(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value)); }
    private static void ValidateRoundId(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("The Keno round identifier is invalid.", nameof(value)); }
}
