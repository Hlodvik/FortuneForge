var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5190");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5191", "http://localhost:5191")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Liar's Dice",
    integration = "Connect this rules package to an application-owned multiplayer host.",
}));

app.MapGet("/api/games/liars-dice/status", () => Results.Ok(new
{
    available = false,
    startingDicePerPlayer = 5,
    mode = "application-host-required",
}));

app.Run();
