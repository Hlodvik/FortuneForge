# Independent game frontend releases

Neon Nights is the first game configured for the shared frontend release command.
The command selects an npm game workspace, builds its local workspace dependencies,
and bundles its configured browser entry into a versioned release. It activates the
release by replacing that game's local `active.json` after the build succeeds.

From the repository root, build and serve the local release.

```powershell
npm run deploy:game -- neon-nights
npx vite preview --outDir artifacts/game-releases --host 127.0.0.1 --port 4193 --strictPort
```

In another PowerShell terminal, start the website with the local release host.

```powershell
$env:VITE_GAME_RELEASE_BASE_URL = 'http://127.0.0.1:4193'
npm run dev --workspace fortuneforge.client
```

Open `/slots/neon-nights/demo` on the website. The website reads the active
manifest when the route opens, loads the corresponding browser module and CSS, and
uses the existing game API. Run the same game command again to publish another
version. Reload the route to open the newly active version.

A game joins the frontend release flow by declaring a `gameRelease.entry` in its
workspace manifest. The entry must export a React component as its default export.
The website supplies its React runtime to the game build.

This phase publishes to the local `artifacts/game-releases` directory. Live artifact
hosting, live promotion and independent engine deployment are separate work.
