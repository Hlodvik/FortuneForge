# Craps local web adapter

This development-only host exposes the `CrapsEngine` through the same `/api/games/craps` boundary expected by the reusable web client. It stores free-play rounds in memory and generates server-side dice. The production `FortuneForge.Games.Craps.Server` package owns paid admission, persistent rounds, recovery and settlement requests. This sample has no paid account integration.

Normally start it together with the browser client:

```powershell
cd games/Craps/client/FortuneForge.Games.Craps.Client
npm run dev
```

For API-only development:

```powershell
dotnet run --project games/Craps/samples/FortuneForge.Games.Craps.Web
```
