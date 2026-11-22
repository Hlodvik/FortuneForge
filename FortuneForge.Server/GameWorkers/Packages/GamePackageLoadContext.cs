using System.Reflection;
using System.Runtime.Loader;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Server.GameWorkers.Packages;

/// <summary>Retains each game's selected dependencies without merging package publish directories.</summary>
internal sealed class GamePackageLoadContext(string entry) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(entry);
    private static readonly Dictionary<string, Assembly> Contracts = new[]
    {
        typeof(GameWorkerAuthentication).Assembly, typeof(GameSecurityContext).Assembly
    }.ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is null) return null;
        if (Contracts.TryGetValue(name.Name, out var contract)) return contract;
        // Framework/platform dependencies share the host. Game implementations always
        // resolve from the selected package, even if a legacy host also references them.
        if (!name.Name.StartsWith("FortuneForge.Games.", StringComparison.Ordinal))
        {
            try { return Default.LoadFromAssemblyName(name); }
            catch (FileNotFoundException) { }
        }
        var path = resolver.ResolveAssemblyToPath(name);
        if (path is null && name.Name.StartsWith("FortuneForge.Games.", StringComparison.Ordinal))
            throw new FileNotFoundException($"The selected game package lacks a required dependency: {name.Name}.");
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
