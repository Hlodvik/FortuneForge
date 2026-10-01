# Liar's Dice local web adapter

Developer-only ASP.NET shell for the reusable Liar's Dice client. Multiplayer identity, matchmaking, and automated participants belong to the application host rather than the game package.

Run it alongside the client with:

```powershell
cd games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client
npm install
npm run dev
```

API-only:

```powershell
dotnet run --project games/LiarsDice/samples/FortuneForge.Games.LiarsDice.Web
```

The preview starts each player with five dice and has no identity, account, payment, or ledger integration.
