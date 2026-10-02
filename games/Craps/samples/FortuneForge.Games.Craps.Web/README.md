# Craps local web adapter

This development-only host exposes the `CrapsEngine` through the same `/api/games/craps` boundary expected by the reusable web client. It stores free-play rounds in memory and generates server-side dice. It does not implement accounts, credits, settlement, persistence, or deployment concerns owned by Fortune Forge.

Normally start it together with the browser client:

```powershell
cd games/Craps/client/FortuneForge.Games.Craps.Client
npm run dev
```

For API-only development:

```powershell
dotnet run --project games/Craps/samples/FortuneForge.Games.Craps.Web
```
