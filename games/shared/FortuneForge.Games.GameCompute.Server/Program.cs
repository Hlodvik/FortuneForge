using FortuneForge.Games.GameCompute.Server;
using FortuneForge.Games.Hosting;

var descriptor = GameComputeModule.Descriptor;
var app = GameServiceHost.Create(args, descriptor.Id, descriptor.PackageVersion);

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/internal") && !GameComputeEndpoints.IsLocalServiceCall(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next(context);
});

GameComputeModule.Map(app);
app.Run();
