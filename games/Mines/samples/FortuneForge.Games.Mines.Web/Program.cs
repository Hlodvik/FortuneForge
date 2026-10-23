using FortuneForge.Games.Mines.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5201");
builder.Configuration["Features:MinesEnabled"] = "true";
builder.Configuration["Features:MinesPaidEnabled"] = "false";
string[] origins = ["http://127.0.0.1:5181", "http://localhost:5181"];
builder.Services.AddMinesServer(options =>
{
    options.ForcePractice = true;
    options.PracticeOrigins = origins;
    options.PracticeLedgerPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "mines-practice.json");
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
app.UseCors();
app.MapGet("/", () => Results.Ok(new { game = "FortuneForge Mines", api = "/api/games/mines", mode = "development-practice", startingBalanceCents = 100_000, client = "http://127.0.0.1:5181" }));
app.MapMines((context, _) =>
{
    const string cookieName = "ff-mines-practice";
    var identity = context.Request.Cookies[cookieName];
    if (!Guid.TryParseExact(identity, "N", out var player))
    {
        player = Guid.NewGuid();
        context.Response.Cookies.Append(cookieName, player.ToString("N"), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(30), IsEssential = true, Path = "/",
        });
    }
    return Task.FromResult<MinesAccount?>(new("local-mines-" + player.ToString("N")));
});
app.Run();
public partial class Program;
