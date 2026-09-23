# Roulette local web adapter

Developer-only ASP.NET host for the reusable Roulette client. It exposes `/api/games/roulette/status`, opens in-memory rounds, accepts one validated bet, and spins using a server-side cryptographic random pocket source. The `RouletteEngine` remains the settlement authority.

Run alongside the client with:

```powershell
cd games/Roulette/client/FortuneForge.Games.Roulette.Client
npm run dev
```

API-only:

```powershell
dotnet run --project games/Roulette/samples/FortuneForge.Games.Roulette.Web
```
