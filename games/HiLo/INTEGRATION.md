# Hi-Lo host integration

The Hi-Lo package under `games/HiLo` owns the rules, random draws, round state, operation journal, recovery and private history. The shared host authenticates and forwards paid requests to the selected worker. The account service owns canonical wallet balances and ledgers behind signed wallet requests. The separate account-scoped practice file has no paid funds.

## Client workspace and route

1. Add `games/HiLo/client/FortuneForge.Games.HiLo.Client` to the root `package.json` workspaces. Add `@fortuneforge/games-hi-lo` version `0.1.0` to `fortuneforge.client/package.json`, then update only the necessary root lockfile entries as part of the coordinated integration. The package declares its Sora font dependency and owns all game-specific CSS and media.
2. Build the package before the host: its exports are `HiLoGame`, `HiLoGameProps`, `HttpHiLoGateway`, the public contracts and pending-command recovery helpers. Styles are exported as `@fortuneforge/games-hi-lo/styles.css`.
3. The authenticated route is `fortuneforge.client/src/app/routes/HiLoRoute.tsx`. Keep `useAuthenticatedAccount('/games/hi-lo')`, the existing loading/error retry UI, `InGameShell` and shared navigation. Memoize `HttpHiLoGateway('/api/games/hi-lo', fetchWithAccountSession, initialAccount.userId)` for paid play; include the account ID in the memo dependencies. Practice uses a separate gateway with `practiceAccountFetch(fetchWithAccountSession)`. The paid gateway sends `X-FortuneForge-Expected-Account` so an account change cannot apply the opened game's command to a different wallet. Import the package CSS.
4. Render `HiLoGame` with matching React `key` and `playerId` values `${account.userId}:${practiceMode ? 'practice' : 'account'}`. This prevents account or wallet mode changes from reusing another scope's pending action. `onBalanceChange` should refresh `getCurrentAccount` in account mode; practice mode uses only its practice snapshot. The component supplies the game interface and rules/help; the host supplies navigation and account chrome.
5. Register the authenticated lazy route `/games/hi-lo` in `AppRoutes.tsx`, its `Hi-Lo` title in `usePageTitle.ts`, and a real casino catalogue card in `pages/games/OtherGamesPage.tsx`. Reuse existing approved navigation and card conventions. Do not substitute a standalone preview iframe or a client-side wallet gateway in the host.

## Catalogue and .NET projects

6. Add `games/catalog/games/hi-lo.game.json` using the existing schema: game ID `hi-lo`, display title `Hi-Lo`, casino category, client package `@fortuneforge/games-hi-lo` version `0.1.0`, API base `/api/games/hi-lo`, and a reviewed migration state appropriate to the release gate. The isolated engine package owns its engine descriptor.
7. Add a `ProjectReference` in `FortuneForge.Server/FortuneForge.Server.csproj` to `../games/HiLo/server/FortuneForge.Games.HiLo.Server/FortuneForge.Games.HiLo.Server.csproj`. Register the core, server, sample and test project paths from `games/HiLo` in the relevant root/games `.slnx` files. Preserve other games' project entries and existing project order.

## Authenticated API and wallet

8. In `FortuneForge.Server/Configuration/FortuneForgeStartup.cs`, import `FortuneForge.Games.HiLo.Server` and register:

```csharp
services.AddHiLoServer(options =>
{
    options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
    options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
    // Set PracticeStorePath to an appropriate writable, durable development path
    // if account-scoped practice mode is enabled in this host environment.
});
```

9. Map the package endpoints in the existing endpoint mapping method, using the host's authenticated account cookie:

```csharp
app.MapHiLo(async (context, cancellationToken) =>
{
    var profile = await context.RequestServices.GetRequiredService<AccountService>()
        .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
    return profile.Value is { } account ? new HiLoAccount(account.UserId) : null;
});
```

The host registration and mapping above preserve the package-owned practice adapter. Paid routes must remain assigned to the selected Hi-Lo worker, including when new play is paused or its worker is unavailable; they must fail closed instead of invoking a local paid store. The worker registers `AddHiLoServer`, `AddGameWalletClient("hi-lo")` and `MapHiLo`, and resolves the account from the verified worker assertion. Its Firestore store persists game state and signed wallet receipts; it never writes account balances or wallet ledgers. Retain existing cookie authentication and rate limits.

The worker needs `GameWallet:Url` and `GameWallet:SigningKey`. The account service needs the matching per-game `GameWallet:Games:hi-lo:SigningKey`, `AllowAccountBalanceRead=true`, `AllowWagerReservations=true` and `LegacySlotLedger=false`. A start reserves the wager before revealing the first card. A terminal action completes the same reservation, including a loss with zero return. A legacy active round already paid its wager and uses a credit-only direct settlement. Exact retries and session recovery repeat the frozen request and retain the original response and request observation.

The host's `practiceAccountFetch` adds `X-FortuneForge-Practice: true`; the package must route that request to its separate per-account practice store. Verify `session.practice === true` and that account credits remain unchanged before enabling the host practice route. The isolated sample sets `ForcePractice = true`, uses its own HttpOnly session cookie and durable practice file, and has no production funds. The client sends `X-FortuneForge-Game: hi-lo` on every mutation; keep same-origin production requests or explicitly reviewed allowed origins.

10. `Features:HiLoEnabled` defaults to false. Enable only the reviewed target environment after validation; the isolated sample enables only its own development configuration. This package does not edit live configuration or deploy itself.

During maintenance, disabling that feature prevents new wagers, guesses and skips while preserving authenticated reads, cashout and exact replay of previously accepted operations. Existing-round GET reports `enabled:false`; a new cashout while paused records that same flag. A disabled read with no prior round returns `503 hi_lo_disabled`. That specific rejected mutation is definitive and clears its browser journal; other uncertain 503 responses retain the original operation for recovery.

Ensure `hiLoPlayers`, `hiLoOperations` and `hiLoEvents` remain server-only in Firestore security rules. No additional query indexes are required by this package's direct document reads. The generic account wallet owns `userBalances`, `balanceTransactions` and reservation/completion receipts; configure the signed permissions above instead of registering a direct account-store writer in the game. Configure the account-scoped practice file on storage with atomic replacement and exclusive-lock support; the local file store retains its journal and is intended for development practice.

## Coordinated verification and release

11. Add `/games/hi-lo` to `scripts/game-smoke-inventory.mjs`, the inventory tests and any fixed-count assertions in `scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs`. Derive the current expected count from the integrated catalogue; do not copy a stale count while other games join the batch.
12. Extend shared packaged-style and casino-shell route coverage (`PackagedGameStyles.test.ts`, `CasinoTableShellRoutes.test.tsx` and relevant route/account tests). Verify the catalogue entry, authenticated redirect, navigation, account switching, practice switching and wallet refresh. Run source ownership and route-inventory checks without changing the Hi-Lo engine or client contracts to bypass them.
13. Run Hi-Lo engine/server/client tests and build the client package and host. Review its standalone visual evidence and exercise the integrated route against isolated emulator accounts: normal streak, tied rank loss, ace/king unavailable direction, skip, failed guess, cashout, automatic cap settlement, insufficient credits, duplicate and concurrent operations, stale revision, refresh/resume and interrupted-response retry. Verify responsive portrait 320–430px, short landscape, keyboard/touch, mute persistence and accessible status announcements.
14. Verify the real signed public gateway, selected worker and generic wallet together using isolated emulator accounts. Check canonical reservation/completion receipts and exactly-once ledger legs, original-key replay after lost wallet replies and worker restart, paused recovery, stale-account rejection, protected financial reconciliation, and legacy active-round continuation without a second wager. Package wallet doubles alone do not establish this integration. Release and deployment use the existing guarded workflow as a separate task.
