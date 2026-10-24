# Main-site integration — Andar Bahar

The following central integration is implemented in local source as of October 24, 2026: workspace/dependency and lockfile, both .NET solutions, authenticated route and shared navigation, catalogue/manifest, server registration, account-history display and route/release inventory. `/games/andar-bahar` is the composed host route; the isolated sample remains on 5182/API5202. No commit, push or deployment is implied by these local changes.

## Workspace and build

1. Root `package.json` registers workspace `games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client`.
2. The host depends on `"@fortuneforge/games-andar-bahar": "0.1.0"`; the root lockfile includes the workspace link without upgrading unrelated dependencies.
3. Both .NET solutions include the following projects, with server and test-host project references to the server package:
   - `games/AndarBahar/src/FortuneForge.Games.AndarBahar/FortuneForge.Games.AndarBahar.csproj`
   - `games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/FortuneForge.Games.AndarBahar.Server.csproj`
   - `games/AndarBahar/samples/FortuneForge.Games.AndarBahar.Web/FortuneForge.Games.AndarBahar.Web.csproj`
   - `games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/FortuneForge.Games.AndarBahar.Tests.csproj`

The server project references its engine, which references the approved shared card/abstraction projects. Client exports the component/gateway/types from the package root and styles from `@fortuneforge/games-andar-bahar/styles.css`.

## Authentication, routing, navigation and wallet

The actual route and stylesheet live in `fortuneforge.client/src/app/routes/AndarBaharRoute.*`. They use the authenticated account hook, shared `InGameShell` navigation, account-session fetch, isolated practice fetch and player/mode-scoped pending/history state. Paid settlement refreshes the shared wallet; identity, mounted-state and request-generation guards prevent stale responses from replacing a newer wallet. The stylesheet fills the available shell body and allows vertical scrolling when its intrinsic grid minimum exceeds a short shared-navbar viewport; horizontal overflow stays hidden. The files in `integration/` are historical scaffolds, not the active route.

`AppRoutes.tsx` lazy-loads `AuthenticatedAndarBaharRoute` at **`/games/andar-bahar`**; `usePageTitle.ts` sets **Andar Bahar — Fortune Forge**.

`OtherGamesPage.tsx` uses a named `andarBaharGame` entry in Casino games, New games and recent activity, preserving existing indexed game references. The account dashboard links recent Andar Bahar results to the actual route. Shared account history accepts its exact-cent settlement summaries, labels/groups them as Andar Bahar and opens them without a second claim or payout. The main component owns its table title; the shared shell owns navigation and the authenticated account.

The active manifest is `games/catalog/games/andar-bahar.game.json`; its migration state remains `rules-pilot`, matching the existing integrated Hi-Lo manifest.

## Server

The host references the server package, registers `AddAndarBaharServer` in Startup and maps `MapAndarBahar` after routing/rate-limit/authorization middleware. See [the server integration document](server/INTEGRATION.md) for the resolver, rate limits, Firestore schema and feature flag.

The account resolver must use the existing account session cookie. Never bind player identity, cards, outcomes or balance from a client request. Paid outcomes, wallet debit/credit, ledger, settlement event and latest pointer commit in one Firestore transaction. The public result contains only the central Joker and dealt prefix through the first match; the undealt deck is never returned or persisted. There is no cash-out endpoint for this instantly settled game.

The package's missing-feature fallback remains false; the main site's checked-in Production and Development settings now explicitly enable `Features:AndarBaharEnabled`, matching the other integrated tables. This is not a live configuration change. The main host explicitly enables authenticated practice to match Hi-Lo and Dragon Tiger: practice requests use a separate account-scoped temporary ledger and cannot debit the paid Firestore wallet. `ForcePractice` remains false. A host that disables practice receives a 403 for an explicit practice request and never silently spends paid funds.

## Tests and release inventory

The smoke inventory, route assertions, admin catalogue contract and release test matrix include Andar Bahar. Focused checks on October 24 passed: **58 client integration/history tests across 10 files**, **22 host-registration/admin-catalogue tests**, **5 route-inventory tests**, source ownership, package build, main-site production build, main-site lint and `git diff --check`. These did not run unrelated gameplay suites. Route tests cover auth/retry, shared navigation, paid/practice isolation, stale identity responses and out-of-order same-account wallet refreshes; history tests cover exact-cent returns, display, acknowledgement and recent-game links.

Retain browser acceptance of the composed shared shell at desktop, 320–430px portrait and short landscape; standalone screenshots alone do not prove the host wrapper geometry. Exercise paid mode only with an isolated emulator account, including .50-cent Andar payouts, duplicate/concurrent posts, insufficient funds, refresh/resume, private reads and a lost response followed by replay. Do not use production funds. [VERIFICATION.md](VERIFICATION.md) retains the historical isolated-package checks.

The owner must review the playable integrated route and approve its release through the existing ship-review loop. Main-site inventory is now 43 games (42 visible by default; Horse Flight remains hidden). No commit, push, live configuration change or deployment is authorized by this integration task.
