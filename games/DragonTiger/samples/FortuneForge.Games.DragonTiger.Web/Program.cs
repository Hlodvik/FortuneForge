using FortuneForge.Games.DragonTiger.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5198");
builder.Configuration["Features:DragonTigerEnabled"] = "true";
builder.Services.AddDragonTigerServer(options => options.ForcePractice = true);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5180", "http://localhost:5180", "http://127.0.0.1:5178", "http://localhost:5178")
    .AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
app.MapGet("/", () => Results.Ok(new { game = "FortuneForge Dragon Tiger", api = "/api/games/dragon-tiger", mode = "local-practice", client = "http://127.0.0.1:5180" }));
app.MapDragonTiger((context, cancellationToken) => Task.FromResult<DragonTigerAccount?>(new("local-dragon-tiger-player", 10_000m)));
app.Run();

public partial class Program;
