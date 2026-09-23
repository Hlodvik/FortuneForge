using System.Text.Json;
using FortuneForge.Games.Keno;

namespace FortuneForge.Games.Keno.Web;

public static class KenoWebApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:5190");
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var drawRandom = random ?? Random.Shared;
        var app = builder.Build();

        app.MapGet("/", () => Results.Ok(new
        {
            game = "Fortune Forge Keno",
            api = "/api/games/keno",
            mode = "sample-domain-rounds",
        }));

        var api = app.MapGroup("/api/games/keno");
        api.MapPost("/rounds", async (HttpRequest request) =>
        {
            KenoRoundRequest? input;
            try
            {
                input = await JsonSerializer.DeserializeAsync<KenoRoundRequest>(
                    request.Body,
                    JsonOptions,
                    cancellationToken: request.HttpContext.RequestAborted);
            }
            catch (JsonException)
            {
                return InvalidTicket();
            }

            if (input?.Ticket?.Numbers is null) return InvalidTicket();

            KenoTicket ticket;
            try
            {
                ticket = new KenoTicket(input.Ticket.Numbers);
            }
            catch (ArgumentException)
            {
                return InvalidTicket();
            }

            var draw = KenoRoundEngine.Draw(drawRandom);
            var round = KenoRoundEngine.Play(ticket, draw);
            return Results.Ok(new KenoRoundResponse(
                new KenoTicketResponse(ticket.Numbers),
                new KenoDrawResponse(draw.Numbers),
                round.Result.HitCount,
                Outcome: null));
        });

        return app;
    }

    private static IResult InvalidTicket() => Results.BadRequest(new KenoErrorResponse(
        "keno-invalid-ticket",
        "Choose from one through ten distinct Keno numbers from 1 through 80."));
}

public sealed record KenoRoundRequest(KenoTicketRequest? Ticket);
public sealed record KenoTicketRequest(IReadOnlyList<int>? Numbers);
public sealed record KenoTicketResponse(IReadOnlyList<int> Numbers);
public sealed record KenoDrawResponse(IReadOnlyList<int> Numbers);
public sealed record KenoRoundResponse(KenoTicketResponse Ticket, KenoDrawResponse Draw, int HitCount, string? Outcome);
public sealed record KenoErrorResponse(string Code, string Message);
