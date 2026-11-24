using System.Security.Cryptography;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.FrontierFortune.Server;

public sealed class FrontierCryptographicRandom : IGameRandomSource
{
    public int Next(int maximumExclusive) => RandomNumberGenerator.GetInt32(maximumExclusive);
}

public sealed class FrontierRoundService(IFrontierRoundStore store, IGameWalletClient wallet,
    IGameWalletAccountReader accounts, IGameRandomSource random, TimeProvider clock)
{
    public async Task<FrontierReply> ReadAsync(string account, CancellationToken token)
    {
        ValidateAccount(account);
        var balance = await accounts.GetStateAsync(account, token);
        var saved = await store.ReadAsync(account, balance.LegacyFreeGamesRemaining, token);
        return FrontierReply.Create(saved.State, balance.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException(), saved.PendingOperation);
    }

    public async Task<FrontierReply> ReadOutlawAsync(string account, CancellationToken token)
    {
        ValidateAccount(account);
        var balance = await accounts.GetStateAsync(account, token);
        var saved = await store.ReadAsync(account, balance.LegacyFreeGamesRemaining, token);
        if (saved.PendingOperation is { MathVersion: null })
            throw new FrontierConflictException("Recover the original v1 operation through its legacy recovery route.", "frontier-legacy-obligation");
        return FrontierReply.CreateOutlaw(saved.State, balance.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException(), saved.PendingOperation);
    }

    public async Task<FrontierReply> ExecuteAsync(string account, FrontierCommand command, bool allowNewPaidRounds, CancellationToken token)
    {
        ValidateAccount(account);
        command.Validate();
        var saved = await store.FindAsync(account, command, token);
        if (saved is null)
        {
            if (command.Kind == "spin" && !allowNewPaidRounds)
                throw new FrontierConflictException("New Frontier wagers are temporarily paused. Existing bounty actions and retries remain available.", "frontier-rounds-paused");
            var accountState = await accounts.GetStateAsync(account, token);
            saved = await store.PrepareAsync(account, command, accountState.LegacyFreeGamesRemaining,
                before => FrontierRoundPolicy.Prepare(account, command, before, random, clock.GetUtcNow().UtcDateTime), token);
        }
        if (saved.Final is { } final) return final;
        var round = saved.Prepared;
        FrontierRoundPolicy.Validate(round);
        var request = FrontierRoundIdentity.WalletRequest(round);
        GameWalletSettlementReceipt receipt;
        try { receipt = await wallet.SettleAsync(account, request, token); }
        catch (GameWalletConflictException error) { throw new GameWalletUnavailableException(error); }
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, FrontierFortuneEngine.GameId, account, request))
            throw new GameWalletUnavailableException();
        return await store.FinalizeAsync(round, receipt, token);
    }

    private static void ValidateAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Length > 256 || account.Any(x => char.IsControl(x) || x is '/' or '\\'))
            throw new ArgumentException("A verified player account is required.");
    }
}
