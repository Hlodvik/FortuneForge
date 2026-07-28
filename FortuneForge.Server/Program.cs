using FortuneForge.Server.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddFortuneForge();

var app = builder.Build();
app.UseFortuneForgePipeline();

app.Run();
