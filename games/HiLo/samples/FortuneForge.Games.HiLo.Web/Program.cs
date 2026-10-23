using System.Security.Cryptography;
using FortuneForge.Games.HiLo.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5203");
builder.Configuration["Features:HiLoEnabled"] = "true";
string[] origins = ["http://127.0.0.1:5183", "http://localhost:5183"];
builder.Services.AddHiLoServer(options =>
{
    options.ForcePractice = true;
    options.AllowedOrigins = origins;
    options.PracticeStorePath = Path.Combine(builder.Environment.ContentRootPath, ".data", "hi-lo-practice.json");
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
app.UseCors();
app.MapGet("/", () => Results.Ok(new { game = "FortuneForge Hi-Lo", mode = "ISOLATED DEVELOPMENT PRACTICE — NO CASH VALUE", api = "/api/games/hi-lo/session", client = "http://127.0.0.1:5183" }));
app.MapHiLo((context, cancellationToken) =>
{
    const string cookie = "FortuneForge.HiLo.Practice";
    var session = context.Request.Cookies[cookie];
    if (session is null || session.Length != 64 || session.Any(c => !char.IsAsciiHexDigit(c)))
    {
        session = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(cookie, session, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, IsEssential = true,
            MaxAge = TimeSpan.FromDays(30), Path = "/api/games/hi-lo",
        });
    }
    return Task.FromResult<HiLoAccount?>(new("practice-" + HiLoService.Hash(session)));
});
app.Run();
public partial class Program;
