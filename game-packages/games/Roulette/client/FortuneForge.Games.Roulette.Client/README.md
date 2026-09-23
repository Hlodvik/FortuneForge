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

The local host is developer-only; the production Fortune Forge application remains the public API host.
