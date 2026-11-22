using System.Text.Json;

namespace FortuneForge.Games.Hosting;

public static class GameBackendPackageCommand
{
    public static bool TryDescribe(string[] args, IGameBackendPackage package)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(package);
        if (args is not ["--describe-backend"]) return false;

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            runtimeGameId = package.GameId,
            moduleType = package.GetType().FullName ?? throw new InvalidOperationException("A backend package requires a named module type.")
        }));
        return true;
    }
}
