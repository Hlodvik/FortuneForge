# Hearts local web adapter

Developer-only ASP.NET shell for the reusable Hearts client. Multiplayer identity, matchmaking, and automated participants belong to the application host rather than the game package.

Run it alongside the client with:

```powershell
cd games/Hearts/client/FortuneForge.Games.Hearts.Client
npm install
npm run dev
```

API-only:

```powershell
dotnet run --project games/Hearts/samples/FortuneForge.Games.Hearts.Web
```

The preview defaults to first-to-100, where the lowest score wins. It has no identity, account, payment, or ledger integration.
