# Fortune Forge Roulette client

Reusable React UI for the single-zero `FortuneForge.Games.Roulette` rules package. The package owns the table interaction and presentation, while the host supplies `RouletteGateway` for status, round creation, bet placement, and spinning.

## Local preview

```powershell
npm install
npm run dev
```

Open http://127.0.0.1:5175. The command starts the local ASP.NET adapter and Vite client together.

## Checks

```powershell
npm run check
npm test
npm run build
```

The local sample is developer-only. The production account API authenticates and forwards the existing URLs to the package-owned Roulette backend. See `games/Roulette/INTEGRATION.md` for persisted table and command recovery.
