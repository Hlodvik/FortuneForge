namespace FortuneForge.Games.PackagedGame.Server;

/// <summary>Returns the user ID established by the worker's trusted request authentication.</summary>
internal delegate string? GameWorkerAccountResolver(HttpContext context);
