# Mines batch integration

All implementation and evidence are owned under `games/Mines`. No central manifest, lockfile,
Startup, solution, route registry, catalogue/index, release script or smoke script was edited.
The parent chat should apply the following as one coordinated batch alongside the other new games,
re-reading current shared files and preserving all concurrent Hearts, Dragon Tiger and Horse Flight work.

## 1. Workspace and project references

- Root `package.json`: add workspace `games/Mines/client/FortuneForge.Games.Mines.Client`.
- `fortuneforge.client/package.json`: add `"@fortuneforge/games-mines": "0.1.0"` to dependencies.
- Reconcile the root npm lockfile once for the full game batch. The package intentionally has no
  private nested install/lockfile. Existing root dependencies supported all local checks.
- Add the following five projects to the appropriate main and games `.slnx` files:
  - `games/Mines/src/FortuneForge.Games.Mines/FortuneForge.Games.Mines.csproj`
  - `games/Mines/server/FortuneForge.Games.Mines.Server/FortuneForge.Games.Mines.Server.csproj`
  - `games/Mines/samples/FortuneForge.Games.Mines.Web/FortuneForge.Games.Mines.Web.csproj`
  - `games/Mines/tests/FortuneForge.Games.Mines.Tests/FortuneForge.Games.Mines.Tests.csproj`
  - `games/Mines/tests/FortuneForge.Games.Mines.Server.Tests/FortuneForge.Games.Mines.Server.Tests.csproj`
- `FortuneForge.Server/FortuneForge.Server.csproj`: add a reference to
  `../games/Mines/server/FortuneForge.Games.Mines.Server/FortuneForge.Games.Mines.Server.csproj`.

No .NET server code is copied into the main application; the server adapter is independently packaged.
No card/chip package is needed for this game's tile mechanic. The core references shared game abstractions.

## 2. Authenticated server wiring

Import `FortuneForge.Games.Mines.Server` in `FortuneForge.Server/Configuration/FortuneForgeStartup.cs`.
In its existing service registration, use the host's current rate-policy constants:

```csharp
services.AddMinesServer(options =>
{
    options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
    options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
    // Set to a private persistent local path outside wwwroot for development practice.
    // The default is <host content root>/App_Data/mines-practice.json.
});
```

Use the host's existing authenticated-account resolver and cookie in its endpoint mapping method:

```csharp
app.MapMines(async (context, cancellationToken) =>
{
    var profile = await context.RequestServices.GetRequiredService<AccountService>()
        .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
    return profile.Value is { } account ? new MinesAccount(account.UserId) : null;
});
```

The host already owns its `FirestoreDb`, middleware, account-cookie validation and trusted-proxy setup.
The API accepts no browser-supplied user ID. JSON POSTs reject cross-origin writes. Preserve the host's
account/CSRF protections and forwarded-header restrictions.

Both `Features:MinesEnabled` and `Features:MinesPaidEnabled` default **false**. General enablement
allows the API; paid enablement additionally permits Firestore wallets. Practice is selected with the
existing `X-FortuneForge-Practice: true` header and never touches a production wallet. `ForcePractice`
is a sample-only option; do not set it in the main production host. No live flag change was made here.

Paid storage atomically updates `userBalances/{userId}_slotsCredits`, the wallet ledger, private
round, latest pointer and command receipt. Units match the host: `available` whole currency units plus
`availableFractionalCents`. Money calculations and API values use **integer cents**.

**Before paid enablement, verify Firestore rules deny all direct browser reads/writes for
`minesRounds`, `minesSessions`, and `minesCommands`, including broader wildcard rules.** The round
collection contains unrevealed boards. Access must be through server credentials and private HTTP
projections. Keep successful command receipts with the financial records; do not expire retries.
The development whole-file practice ledger is not a distributed production storage backend.
The supplied paid Firestore store is the authoritative durable backend.

## 3. Route and shared shell

Create `fortuneforge.client/src/app/routes/MinesRoute.tsx` patterned on the current Baccarat route.
The following uses existing app services; re-check imports against the batch's latest shared files:

```tsx
import { useCallback, useMemo, useState } from 'react'
import { MinesGame, HttpMinesGateway } from '@fortuneforge/games-mines'
import '@fortuneforge/games-mines/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeNavAction } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'

export function AuthenticatedMinesRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/mines')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Mines…"
      errorTitle="Mines could not be opened." onRetry={reload} />
  }
  return <MinesSession key={account.userId} initialAccount={account} />
}

function MinesSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practice = walletPracticeModeEnabled()
  const playerId = `${account.userId}:${practice ? 'practice' : 'account'}`
  const gateway = useMemo(() => new HttpMinesGateway('/api/games/mines',
    practice ? practiceAccountFetch(fetchWithAccountSession) : fetchWithAccountSession), [practice])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])
  return <InGameShell account={account} title="Mines" theme="casino" className="player-page"
    actions={<PracticeModeNavAction enabled={practice} path="/games/mines" />}>
    <MinesGame key={playerId} playerId={playerId} gateway={gateway}
      onBalanceChange={practice ? undefined : refreshBalance} currencySymbol="R" />
  </InGameShell>
}
```

`onBalanceChange` receives cents and this example refreshes the normal account summary instead of
passing cents into a currency-unit balance. The component has its own title and wallet quote; use
`showTitle={false}` if host title ownership requires it. Keep the account+mode identity: the package
keys its session and pending storage by that value. Replayed command receipts are historical; the
client always reads `/session` before input resumes. Do not replace that reconciliation with local outcomes.

Register the lazy authenticated route at `/games/mines` in `AppRoutes.tsx`; add the title in
`usePageTitle.ts`; add a real casino entry in `pages/games/OtherGamesPage.tsx` using the batch's
current entry conventions. Preserve authenticated shell/home/library navigation and the normal
practice toggle. The package preview already uses the real shared `InGameShell` without editing it.

## 4. Catalogue entry

Create `games/catalog/games/mines.game.json` with the current manifest schema:

```json
{
  "$schema": "../game-manifest.schema.json",
  "schemaVersion": 1,
  "id": "mines",
  "displayName": "Mines",
  "category": "casino",
  "packageVersion": "0.1.0",
  "clientRoute": "/games/mines",
  "clientPackage": "@fortuneforge/games-mines",
  "apiBasePath": "/api/games/mines",
  "capabilities": ["credits", "free-play", "history"],
  "migrationState": "rules-pilot"
}
```

The independently tested `MinesModule.Descriptor` already has these capabilities. Keep the review
state at `rules-pilot` until host integration and owner review are complete. This package work does
not change the current Hearts owner-led review game or claim a deployment.

## 5. Batch checks and release boundary

Add `/games/mines` to `scripts/game-smoke-inventory.mjs`, update route inventory assertions in
`scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs` once for the full batch,
and extend `PackagedGameStyles.test.ts`, shell/route tests, and the game's catalogue coverage.
Derive counts from the updated inventory; do not assume other concurrent games left the count alone.
Add host tests for authenticated account resolution, balance refresh, practice header isolation,
disabled feature gates and default-disabled paid play.

Build the new package before the main app; run workspace ownership checks, catalogue/schema checks,
route inventory, host build/tests and package tests. Exercise the actual integrated authenticated route
against an isolated emulator account, including active-round refresh/resume and practice/account
switching. No production funds are authorized for verification. Preserve receipt and round privacy.

The package already passed core, client and real Firestore emulator tests, plus browser flow and
seven viewport checks. Evidence is under `games/Mines/evidence`; see the root README and server
README for exact commands/contracts. The verified maximum gross return is 51,482,970,000 cents at
the 10,000-cent maximum wager, with no hidden cap. The owner must review exposure/limits alongside
the documented exact curve before enabling paid play; introducing a cap would require new math and
tests and must not silently change these returns.

This handoff authorizes neither commit/push nor deployment/live configuration changes. Those remain
with the parent chat and the owner's normal release process. The preview runs only on loopback
ports 5181/5201 with clearly labeled development practice credits.
