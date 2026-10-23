using FortuneForge.Games.AndarBahar.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5202");
builder.Configuration["Features:AndarBaharEnabled"] = "true";
builder.Services.AddAndarBaharServer(options =>
{
    options.ForcePractice = true;
    options.PracticeStatePath = Path.Combine(builder.Environment.ContentRootPath, ".practice", "andar-bahar.json");
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5182", "http://localhost:5182")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
app.UseCors();
app.MapGet("/", () => Results.Ok(new { game = "FortuneForge Andar Bahar", api = "/api/games/andar-bahar", mode = "DEVELOPMENT PRACTICE ONLY", client = "http://127.0.0.1:5182" }));
app.MapAndarBahar((context, cancellationToken) =>
{
    // A per-browser, HTTP-only local account separates practice balances. This cookie is never a production identity.
    const string cookieName = "ff-andar-bahar-practice";
    if (!context.Request.Cookies.TryGetValue(cookieName, out var account) || !Guid.TryParseExact(account, "N", out _))
    {
        account = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(cookieName, account, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/", MaxAge = TimeSpan.FromDays(30),
        });
    }
    return Task.FromResult<AndarBaharAccount?>(new($"local-practice-{account}", 10_000m));
});
app.Run();

public partial class Program;
