# Independent game releases

The shared release command builds one game's browser and backend artifacts.
Each game registers its entry points in its own `game-release.json`. The game ID
comes from that configuration, so it can differ from the npm workspace and
internal API identifiers.

From the repository root, build Neon Nights and serve its browser release.

```powershell
npm run deploy:game -- neon-nights
npx vite preview --outDir artifacts/game-releases --host 127.0.0.1 --port 4193 --strictPort
```

In another PowerShell terminal, start the website with the local release host.

```powershell
$env:VITE_GAME_RELEASE_BASE_URL = 'http://127.0.0.1:4193'
npm run dev --workspace fortuneforge.client
```

Open `/slots/neon-nights/demo`. The website loads the active browser module and
CSS with its shared React runtime. The standard site API still handles play in
the website demo.

The command writes the runnable backend to the private
`artifacts/game-engine-releases/games/<game-id>/releases/<backend-release-id>`
directory. The active browser manifest also records that backend release ID.
To run the Neon engine locally, start its DLL from that directory.

```powershell
dotnet ./FortuneForge.Games.NeonNights.Server.dll --urls http://127.0.0.1:5193
```

The engine responds at `/healthz` and accepts Neon demo spin inputs at
`POST /internal/v1/demo/spins`.

For an art, audio, or UI release, use `npm run deploy:game -- neon-nights --assets-only`.
The new browser release keeps the backend release selected by the previous
combined release.

Add `--stage` to build the artifacts and print their release ID without making
them active. Use `--promote <release-id>` to select a staged release or
`--rollback <release-id>` to restore a previously active one. Both update the
active pointer for the specified game.

This command publishes local artifacts. Live promotion and website routing to
independently hosted engines are still in development. Combined local releases
are registered for the 19 slots that use the shared slots API, Jade Dragon, and
the games listed below.

Classic slots publish their own manifest, styles, and media using the shared slot
view. Their backend build selects one engine project and uses the shared
`FortuneForge.Games.ClassicSlot.Server` executable.

Jade Dragon publishes its own HTML entry and artwork for the site's iframe. Its
backend artifact runs the game engine for demo play. The site continues to use
its existing paid Jade Dragon API.

Andar Bahar, Craps, Dragon Tiger, Hi-Lo, and Mines publish browser modules and
selected `FortuneForge.Games.PackagedGame.Server` workers. For local integration
work, these workers require a loopback Firestore emulator and an explicit demo
account.

2048, Baccarat, Casino War, Drop Merge, Hearts, Keno, Roulette, Sic Bo, Texas
Hold’em, and Video Poker publish browser modules and selected
`FortuneForge.Games.GameCompute.Server` workers. Their internal HTTP endpoints
execute game rules. The site API still owns account sessions, wallet
transactions, and multiplayer coordination.
