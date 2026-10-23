using System.Text.Json;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo.Server;

/// <summary>Isolated durable practice currency. Exclusive lock file serializes multiple processes; a flushed atomic replace commits state and operation response together.</summary>
public sealed class DurablePracticeHiLoStore(string path, long startingBalanceCents = 1_000_000) : IHiLoStore
{
    private readonly string path = Path.GetFullPath(path);
    public bool Practice => true;
    public Task<HiLoSessionResponse> GetSessionAsync(string userId, CancellationToken cancellationToken = default) =>
        Locked(database => HiLoMutations.Snapshot(Player(database, userId).State, true), false, cancellationToken);
    public Task<HiLoSessionResponse> ExecuteAsync(string userId, HiLoCommand command, Func<PlayingCard> draw,
        DateTimeOffset now, CancellationToken cancellationToken = default, bool allowNewMutations = true) => Locked(database =>
        {
            HiLoService.ValidateCommand(command);
            var player = Player(database, userId);
            var fingerprint = HiLoService.Fingerprint(command);
            if (player.Operations.TryGetValue(command.OperationId, out var previous))
            {
                if (previous.Fingerprint != fingerprint) throw new HiLoException("operation_conflict", "This operationId was already used for a different request.");
                return previous.Response;
            }
            var mutation = HiLoMutations.Apply(player.State, command, draw, true, allowNewMutations);
            player.State = mutation.State;
            player.Operations[command.OperationId] = new(fingerprint, mutation.Response);
            if (mutation.LedgerType is not null) player.Ledger.Add(new(command.OperationId, mutation.LedgerType, mutation.WalletDeltaCents, mutation.State.BalanceCents, now));
            return mutation.Response;
        }, true, cancellationToken);
    private PlayerData Player(Database database, string userId)
    {
        var key = HiLoService.Hash(userId);
        if (!database.Players.TryGetValue(key, out var player)) database.Players[key] = player = new() { State = new(startingBalanceCents, null) };
        return player;
    }
    private async Task<T> Locked<T>(Func<Database, T> action, bool write, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var gate = await Acquire(cancellationToken);
        var database = File.Exists(path) ? JsonSerializer.Deserialize<Database>(await File.ReadAllTextAsync(path, cancellationToken))
            ?? throw new InvalidOperationException("The practice Hi-Lo database is corrupt.") : new Database();
        if (database.SchemaVersion != 1) throw new InvalidOperationException("Unsupported practice Hi-Lo database version.");
        foreach (var player in database.Players.Values)
        {
            if (player.State.BalanceCents < 0) throw new InvalidOperationException("The persisted practice wallet is corrupt.");
            if (player.State.Round is { } round) HiLoFirestoreStore.ValidateRound(round);
        }
        var response = action(database);
        if (!write) return response;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, database, cancellationToken: cancellationToken);
                await file.FlushAsync(cancellationToken); file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return response;
    }
    private async Task<FileStream> Acquire(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(15, cancellationToken); }
        }
    }
    private sealed class Database
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, PlayerData> Players { get; set; } = [];
    }
    private sealed class PlayerData
    {
        public HiLoStoredState State { get; set; } = new(0, null);
        public Dictionary<string, HiLoOperation> Operations { get; set; } = [];
        public List<PracticeLedgerEntry> Ledger { get; set; } = [];
    }
    private sealed record PracticeLedgerEntry(string OperationId, string Type, long AmountCents, long BalanceAfterCents, DateTimeOffset CreatedAt);
}
