# Liar's Dice local web adapter

Developer-only ASP.NET host for the reusable Liar's Dice client. It runs one human player against three deterministic bots, delegates exact-face bidding and challenges to `LiarsDiceMatchEngine`, and removes one die from the losing player after each challenge.

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
