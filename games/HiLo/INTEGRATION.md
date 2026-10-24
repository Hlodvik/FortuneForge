# Hi-Lo host integration

The Hi-Lo package is isolated under `games/HiLo`. The following checklist originally described pending host work; the October 23 integration now implements the authenticated route, shared shell/faces, workspace/solution registration, production feature, catalogue, trusted Hosting origins and guarded verification. The host practice file is explicitly ephemeral and account-scoped; it never contains paid funds. Paid rounds and settlement use Firestore. Preserve all unrelated edits and the existing main working tree.

## Client workspace and route

1. Add `games/HiLo/client/FortuneForge.Games.HiLo.Client` to the root `package.json` workspaces. Add `@fortuneforge/games-hi-lo` version `0.1.0` to `fortuneforge.client/package.json`, then update only the necessary root lockfile entries as part of the coordinated integration. The package declares its Sora font dependency and owns all game-specific CSS and media.
2. Build the package before the host: its exports are `HiLoGame`, `HiLoGameProps`, `HttpHiLoGateway`, the public contracts and pending-command recovery helpers. Styles are exported as `@fortuneforge/games-hi-lo/styles.css`.
3. Add `fortuneforge.client/src/app/routes/HiLoRoute.tsx`, patterned on the existing `BaccaratRoute.tsx`. Authenticate with `useAuthenticatedAccount('/games/hi-lo')`; keep the existing loading/error retry UI, `InGameShell`, shared navigation and `PracticeModeNavAction`. Memoize `HttpHiLoGateway('/api/games/hi-lo', practiceMode ? practiceAccountFetch(fetchWithAccountSession) : fetchWithAccountSession)` and import the package CSS.
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

The server owns secure draws, revisions, durable operation replay, wagers and settlement. Account mode uses the host's existing Firestore account/wallet conventions. The package's account resolver accepts no player ID from the browser. Retain existing cookie, authentication, rate limits and Firestore configuration.

The host's `practiceAccountFetch` adds `X-FortuneForge-Practice: true`; the package must route that request to its separate per-account practice store. Verify `session.practice === true` and that account credits remain unchanged before enabling the host practice route. The isolated sample sets `ForcePractice = true`, uses its own HttpOnly session cookie and durable practice file, and has no production funds. The client sends `X-FortuneForge-Game: hi-lo` on every mutation; keep same-origin production requests or explicitly reviewed allowed origins.

10. `Features:HiLoEnabled` defaults to false. Enable only the reviewed target environment after validation; the isolated sample enables only its own development configuration. This package does not edit live configuration or deploy itself.

During maintenance, disabling that feature prevents new wagers, guesses and skips while preserving authenticated reads, cashout and exact replay of previously accepted operations. Existing-round GET reports `enabled:false`; a new cashout while paused records that same flag. A disabled read with no prior round returns `503 hi_lo_disabled`. That specific rejected mutation is definitive and clears its browser journal; other uncertain 503 responses retain the original operation for recovery.

Ensure `hiLoPlayers`, `hiLoOperations` and `hiLoEvents` remain server-only in the host's Firestore security rules. No additional query indexes are required by this package's direct document reads. The adapter atomically uses `userBalances/{userId}_slotsCredits` with `available` whole credits and `availableFractionalCents` 0–99; central integration must verify other wallet writers preserve the fractional field under concurrent play. Configure the account-scoped practice file on durable storage with atomic replacement and exclusive-lock support; the local file store deliberately retains its journal and is intended for development practice.

## Coordinated verification and release

11. Add `/games/hi-lo` to `scripts/game-smoke-inventory.mjs`, the inventory tests and any fixed-count assertions in `scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs`. Derive the current expected count from the integrated catalogue; do not copy a stale count while other games join the batch.
12. Extend shared packaged-style and casino-shell route coverage (`PackagedGameStyles.test.ts`, `CasinoTableShellRoutes.test.tsx` and relevant route/account tests). Verify the catalogue entry, authenticated redirect, navigation, account switching, practice switching and wallet refresh. Run source ownership and route-inventory checks without changing the Hi-Lo engine or client contracts to bypass them.
13. Run Hi-Lo engine/server/client tests and build the client package and host. Review its standalone visual evidence and exercise the integrated route against isolated emulator accounts: normal streak, tied rank loss, ace/king unavailable direction, skip, failed guess, cashout, automatic cap settlement, insufficient credits, duplicate and concurrent operations, stale revision, refresh/resume and interrupted-response retry. Verify responsive portrait 320–430px, short landscape, keyboard/touch, mute persistence and accessible status announcements.
14. Inspect the final diff and run the guarded release workflow. The owner explicitly authorized committing, pushing main and deploying the audited current tree on October 23. This does not authorize production test wagers; financial verification uses isolated emulator and practice accounts.
