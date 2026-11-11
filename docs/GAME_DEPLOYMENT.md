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

This command publishes local artifacts. Live promotion and website routing to
independently hosted engines are still in development. Combined local releases
are registered for the 19 slots that use the shared slots API.

Classic slots publish their own manifest, styles, and media using the shared slot
view. Their backend build selects one engine project and uses the shared
`FortuneForge.Games.ClassicSlot.Server` executable.
