using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddOptions<SlotsOptions>()
    .Bind(builder.Configuration.GetSection(SlotsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SlotsOptions>, SlotsOptionsValidator>();
builder.Services.AddSingleton<ISlotsDefinitionProvider, OptionsSlotsDefinitionProvider>();
builder.Services.AddSingleton<IRandomIndexSource, CryptoRandomIndexSource>();
builder.Services.AddSingleton<IReelGenerator, CryptoReelGenerator>();
builder.Services.AddSingleton<ICombinationEvaluator, CombinationEvaluator>();
builder.Services.AddSingleton<IPayoutCalculator, PayoutCalculator>();
builder.Services.AddSingleton<SpinService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
