using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using FortuneForge.Games.Keno.Web;

namespace FortuneForge.Games.Tests.Keno;

public sealed class KenoSampleHostEndpointTests
{
    [Fact]
    public async Task CreateRoundReturnsAValidTwentyNumberDrawAndHitCount()
    {
        await using var app = CreateApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/games/keno/rounds", new
        {
            ticket = new { numbers = new[] { 3, 7, 15 } },
        });
        var round = await response.Content.ReadFromJsonAsync<KenoRoundResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(round);
        Assert.Equal(new[] { 3, 7, 15 }, round.Ticket.Numbers);
        Assert.Equal(20, round.Draw.Numbers.Count);
        Assert.Equal(20, round.Draw.Numbers.Distinct().Count());
        Assert.All(round.Draw.Numbers, number => Assert.InRange(number, 1, 80));
        Assert.Equal(round.Ticket.Numbers.Intersect(round.Draw.Numbers).Count(), round.HitCount);
        Assert.Null(round.Outcome);
    }

    [Fact]
    public async Task CreateRoundMapsAnInvalidTicketToTheStableBadRequestShape()
    {
        await using var app = CreateApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/games/keno/rounds", new
        {
            ticket = new { numbers = new[] { 1, 1 } },
        });
        var error = await response.Content.ReadFromJsonAsync<KenoErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("keno-invalid-ticket", error!.Code);
        Assert.Equal("Choose from one through ten distinct Keno numbers from 1 through 80.", error.Message);
    }

    private static WebApplication CreateApp()
    {
        var builder = KenoWebApplication.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        return KenoWebApplication.Build(builder, new Random(12345));
    }
}
