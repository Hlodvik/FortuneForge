using FortuneForge.Games.Hosting;
using FortuneForge.Games.ClassicSlot.Server;

var package = new GameBackendPackage();
if (GameBackendPackageCommand.TryDescribe(args, package)) return;
var app = package.Create(new WebApplicationOptions { Args = args }, _ => { });
app.Run();
