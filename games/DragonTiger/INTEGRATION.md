# Dragon Tiger host integration

## Backend ownership update — 21 November 2026

New paid rounds run in the packaged game worker. `DragonTigerWalletStore` saves an immutable deal intent before calling the signed `IGameWalletClient`; after the wallet confirms, the package finalizes its outcome, audit, activity and recovery pointer in one transaction. The package never updates account balances or wallet ledger documents for a new round. Schema-v1 rounds retain their saved wagers and outcomes through the read-only compatibility store. Pending rounds recover through the original POST key or the existing GET/latest routes, including while new wagers are paused.

The worker module registers `AddGameWalletClient` for `dragon-tiger`. It requires `GameWallet:Url` and `GameWallet:SigningKey`; the generic account service requires the matching per-game signing key and `AllowAccountBalanceRead=true`. These tables use the standard wallet ledger, so `LegacySlotLedger` must remain false. The shared authenticated gateway must route paid requests to the configured worker and preserve the existing public URLs. The standalone sample remains forced practice.

This is a local migration. Package verification and shared gateway cutover are tracked in `artifacts/backend-completion/table-card-subhandoff.json`; no production deployment has been performed. The older host wiring notes below describe the prior architecture.


The shared-host wiring below was implemented locally on 23 October 2026 after the Baccarat release gate opened. Dragon Tiger now uses the authenticated main app, the actual shared black-themed navbar, account/practice-isolated recovery, and the shared PlayingCard faces. The game-specific Asian backs remain in the game package. The API uses the normal account resolver and existing rate-limit policies.

The reviewed release enables `Features:DragonTigerEnabled` in Production and Development and makes the runtime catalogue entry visible. The user authorized publishing the completed Craps and Dragon Tiger work on 23 October 2026. Publication still uses the normal guarded release flow; the November signed-worker migration now requires the wallet configuration described above.

The release-owned runtime allowlist (`AdminManagementCatalog.cs`) and 41-game smoke inventory include Dragon Tiger. Release verification also runs the separate Dragon Tiger C# test project; registering it in a solution alone would not run those tests. The numbered list below describes the integration recipe, not outstanding work.

1. Root `package.json`: add workspace `games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client`. App `fortuneforge.client/package.json`: add `@fortuneforge/games-dragon-tiger` version `0.1.0`. Regenerate only the necessary root lockfile entries; validate the existing game ownership/workspace checks.
2. Add `fortuneforge.client/src/app/routes/DragonTigerRoute.tsx` patterned on `BaccaratRoute.tsx`: `useAuthenticatedAccount('/games/dragon-tiger')`, the existing `InGameShell`, `fetchWithAccountSession` and `practiceAccountFetch`. Import the new package and its `styles.css`; key the component and `playerId` by `${account.userId}:${practiceMode ? 'practice' : 'account'}`. Refresh `getCurrentAccount` on real wallet balance changes. The standalone component owns its game heading.
3. Register the lazy authenticated route in `AppRoutes.tsx`, add its title to `usePageTitle.ts`, and add a real entry on `pages/games/OtherGamesPage.tsx`. Route `/games/dragon-tiger`; title `Dragon Tiger`; category casino.
4. Add `games/catalog/games/dragon-tiger.game.json` using the existing schema, client package `@fortuneforge/games-dragon-tiger`, package version `0.1.0`, API base `/api/games/dragon-tiger`, and `rules-pilot` migration state until reviewed. The engine descriptor already exists in its own package.
5. Add a `ProjectReference` in `FortuneForge.Server/FortuneForge.Server.csproj` to `../games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/FortuneForge.Games.DragonTiger.Server.csproj`. Register core/server/sample/test projects in the relevant root and games `.slnx` files without changing existing entries.
6. In `FortuneForge.Server/Configuration/FortuneForgeStartup.cs`, import `FortuneForge.Games.DragonTiger.Server` and call:

```csharp
services.AddDragonTigerServer(options =>
{
    options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
    options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
});
```

Map the package's endpoints in the server's existing endpoint mapping method, resolving the host's authenticated account:

```csharp
app.MapDragonTiger(async (context, cancellationToken) =>
{
    var profile = await context.RequestServices.GetRequiredService<AccountService>()
        .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
    return profile.Value is { } account
        ? new DragonTigerAccount(account.UserId, account.Balances.SlotsCredits)
        : null;
});
```

Keep the normal account cookie, middleware and rate-limit policies. The package honors the existing `X-FortuneForge-Practice: true` header and keeps practice funds separate. It does not own authentication. The reusable package's feature gate fails closed when unset; the reviewed main-host configuration explicitly enables it. The standalone sample remains forced practice.

7. Add the route to `scripts/game-smoke-inventory.mjs` and adjust inventory assertions in `scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs` from 40 to 41, after verifying no other concurrent game changed that count. Extend `PackagedGameStyles.test.ts` and `CasinoTableShellRoutes.test.tsx` route coverage, add a route/account-practice integration test, and use the existing catalogue tests for the new entry.
8. Build both packages and the host, run ownership/route inventory verification, main app route tests and this package's tests. Exercise the connected authenticated route against an isolated emulator account (never production funds), checking both wallet and practice mode. Review the completed release diff, commit it to main, and use the guarded deployment described in `docs/RELEASE_SAFETY.md` after approval.
