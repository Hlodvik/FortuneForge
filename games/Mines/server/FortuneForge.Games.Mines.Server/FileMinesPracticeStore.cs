using System.Text.Json;

namespace FortuneForge.Games.Mines.Server;

/// <summary>
/// Durable development-only wallet. A cross-process exclusive lock serializes readers and writers;
/// flushed temporary files replace the ledger atomically. It never accesses account balances.
/// Keep its directory private, outside the web root and outside source control.
/// </summary>
public sealed class FileMinesPracticeStore(string ledgerPath, long startingBalanceCents = 100_000) : IMinesStore
{
    private readonly string ledgerPath = Path.GetFullPath(ledgerPath);
    public bool Practice => true;

    public async Task<MinesTransactionSnapshot> ReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var fileLock = await Acquire(cancellationToken);
        var data = await Load(cancellationToken);
        return Snapshot(data, userId, null, null);
    }

    public async Task<MinesSessionResponse> ExecuteAsync(string userId, string commandId, string fingerprint, string? roundId,
        Func<MinesTransactionSnapshot, MinesMutation> action, CancellationToken cancellationToken = default)
    {
        using var fileLock = await Acquire(cancellationToken);
        var data = await Load(cancellationToken);
        var snapshot = Snapshot(data, userId, commandId, roundId);
        var mutation = action(snapshot);
        if (mutation.Replay) return mutation.Response;
        var round = mutation.Round ?? throw new InvalidOperationException("A Mines command must save a round.");
        round.Validate(userId);
        if (mutation.Response.BalanceCents != checked(snapshot.BalanceCents + mutation.BalanceDeltaCents) || mutation.Response.BalanceCents < 0)
            throw new InvalidOperationException("The Mines practice wallet mutation is invalid.");
        data.Balances[userId] = mutation.Response.BalanceCents;
        data.Rounds[round.RoundId] = round;
        data.Latest[userId] = round.RoundId;
        data.Commands.Add(commandId, new(userId, fingerprint, mutation.Response));
        if (mutation.BalanceDeltaCents != 0)
            data.Ledger.Add(new(commandId, userId, round.RoundId, mutation.BalanceDeltaCents, mutation.Response.BalanceCents,
                mutation.LedgerType ?? throw new InvalidOperationException("A wallet mutation needs a ledger type."), round.UpdatedAt));
        await Save(data, cancellationToken);
        return mutation.Response;
    }

    private MinesTransactionSnapshot Snapshot(PracticeLedger data, string userId, string? commandId, string? roundId)
    {
        var balance = data.Balances.GetValueOrDefault(userId, startingBalanceCents);
        if (balance < 0) throw new InvalidOperationException("The Mines practice wallet is corrupt.");
        MinesStoredRound? latest = null;
        if (data.Latest.TryGetValue(userId, out var latestId))
        {
            latest = data.Rounds.GetValueOrDefault(latestId) ?? throw new InvalidOperationException("The Mines active-round pointer is corrupt.");
            latest.Validate(userId);
        }
        var target = roundId is not null ? data.Rounds.GetValueOrDefault(roundId) : null;
        // An attacker cannot distinguish an absent round from another player's round.
        if (target is not null && target.UserId != userId) target = null;
        target?.Validate(userId);
        return new(balance, latest, target, commandId is not null ? data.Commands.GetValueOrDefault(commandId) : null);
    }

    private async Task<FileStream> Acquire(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(ledgerPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(15, cancellationToken); }
        }
    }
    private async Task<PracticeLedger> Load(CancellationToken cancellationToken)
    {
        if (!File.Exists(ledgerPath)) return new();
        await using var file = new FileStream(ledgerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var ledger = await JsonSerializer.DeserializeAsync<PracticeLedger>(file, MinesService.StorageJson, cancellationToken)
            ?? throw new InvalidOperationException("The Mines practice ledger is empty or corrupt.");
        if (ledger.SchemaVersion != 1) throw new InvalidOperationException("The Mines practice ledger schema is unsupported.");
        return ledger;
    }
    private async Task Save(PracticeLedger data, CancellationToken cancellationToken)
    {
        var temporary = ledgerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, data, MinesService.StorageJson, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, ledgerPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public sealed class PracticeLedger
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, long> Balances { get; set; } = [];
        public Dictionary<string, string> Latest { get; set; } = [];
        public Dictionary<string, MinesStoredRound> Rounds { get; set; } = [];
        public Dictionary<string, MinesCommandReceipt> Commands { get; set; } = [];
        public List<PracticeLedgerEntry> Ledger { get; set; } = [];
    }
    public sealed record PracticeLedgerEntry(string CommandId, string UserId, string RoundId, long AmountCents,
        long BalanceAfterCents, string Type, DateTimeOffset CreatedAt);
}
