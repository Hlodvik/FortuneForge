var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5188");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5178", "http://localhost:5178")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Hearts",
    integration = "Connect this rules package to an application-owned multiplayer host.",
}));

app.MapGet("/api/games/hearts/status", () => Results.Ok(new
{
    available = false,
    defaultTargetScore = 100,
    mode = "application-host-required",
}));

app.Run();
