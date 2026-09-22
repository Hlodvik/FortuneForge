using System.Security.Cryptography;
using FortuneForge.Games.Keno;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Numbers.Keno;

public sealed record KenoTicketRequest(IReadOnlyList<int>? Numbers);
public sealed record CreateKenoRoundRequest(KenoTicketRequest? Ticket);
public sealed record KenoStatusResponse(bool Available, decimal Balance, string Mode);
public sealed record KenoTicketResponse(IReadOnlyList<int> Numbers);
public sealed record KenoDrawResponse(IReadOnlyList<int> Numbers);
public sealed record KenoRoundResponse(string RoundId, decimal Balance, string Phase, KenoTicketResponse Ticket, KenoDrawResponse Draw, int HitCount, string Outcome);
public sealed record KenoErrorResponse(string Code, string Message);

internal sealed record KenoStoreRound(string RoundId, string UserId, KenoTicket Ticket, KenoDraw Draw, int HitCount);
internal sealed record KenoStoreResult(KenoStoreRound Round, long BalanceCents);
internal sealed class KenoRoundNotFoundException() : Exception("Keno round not found.");
internal sealed class KenoRoundConflictException(string message) : Exception(message);

internal interface IKenoStore
{
    Task<KenoStoreResult> StartAsync(string userId, string idempotencyKey, KenoTicket ticket, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<KenoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
}

internal sealed class KenoService(IKenoStore store, TimeProvider timeProvider, Func<KenoDraw>? createDraw = null)
{
    private readonly Func<KenoDraw> createDraw = createDraw ?? CreateDraw;

    public async Task<KenoRoundResponse> StartAsync(string userId, CreateKenoRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateKey(idempotencyKey);
        var ticket = new KenoTicket(request.Ticket?.Numbers ?? throw new ArgumentException("Choose from one through ten Keno numbers.", nameof(request)));
        return ToResponse(await store.StartAsync(userId, idempotencyKey, ticket, createDraw(), timeProvider.GetUtcNow(), cancellationToken));
    }

    public async Task<KenoRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new KenoRoundNotFoundException());
    }

    internal static KenoRoundResponse ToResponse(KenoStoreResult result)
    {
        var round = result.Round;
        return new KenoRoundResponse(round.RoundId, RandMoney.CentsToRand(result.BalanceCents), "completed", new KenoTicketResponse(round.Ticket.Numbers), new KenoDrawResponse(round.Draw.Numbers), round.HitCount, $"{round.HitCount} {(round.HitCount == 1 ? "hit" : "hits")}");
    }

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
