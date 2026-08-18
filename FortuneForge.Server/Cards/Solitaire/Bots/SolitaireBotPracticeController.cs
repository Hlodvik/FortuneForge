using FortuneForge.Server.Cards.Bots;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Cards.Solitaire.Bots;

[ApiController]
[Route("api/cards/solitaire/bot-practice")]
public sealed class SolitaireBotPracticeController(IServiceProvider services) : ControllerBase
{
    private SolitaireBotPracticeService Service =>
        services.GetRequiredService<SolitaireBotPracticeService>();

    [HttpPost("queue")]
    public ActionResult Join(
        CardBotJoinRequest request,
        [FromHeader(Name = "X-Practice-Session-Id")] string? sessionId)
    {
        try
        {
            var id = sessionId ?? string.Empty;
            return Ok(Service.Join(id, PracticeName(id), request, DateTime.UtcNow));
        }
        catch (Exception exception) { return CardBotHttp.FromException(this, exception); }
    }

    [HttpGet("session")]
    public ActionResult Session([FromHeader(Name = "X-Practice-Session-Id")] string? sessionId)
    {
        try { return Ok(Service.Get(sessionId ?? string.Empty, DateTime.UtcNow)); }
        catch (Exception exception) { return CardBotHttp.FromException(this, exception); }
    }

    [HttpPost("matches/{matchId}/commands")]
    public ActionResult Command(
        string matchId,
        CardBotCommandRequest request,
        [FromHeader(Name = "X-Practice-Session-Id")] string? sessionId)
    {
        try { return Ok(Service.Command(sessionId ?? string.Empty, matchId, request, DateTime.UtcNow)); }
        catch (Exception exception) { return CardBotHttp.FromException(this, exception); }
    }

    private static string PracticeName(string value) =>
        $"Player{Math.Abs(StringComparer.Ordinal.GetHashCode(value)) % 10_000:0000}";
}
