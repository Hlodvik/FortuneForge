using FortuneForge.Games.GameCompute.Server;
using FortuneForge.Games.Hosting;

var descriptor = GameComputeModule.Descriptor;
var app = GameServiceHost.Create(args, descriptor.Id, descriptor.PackageVersion);
app.UseGameWorkerAuthentication(descriptor.Id);
GameComputeModule.Map(app);
app.Run();
