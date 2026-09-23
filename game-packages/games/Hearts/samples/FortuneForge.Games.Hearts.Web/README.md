# Hearts local web adapter

Developer-only ASP.NET host for the reusable Hearts client. It runs one human North seat against three deterministic bots, including standard pass rotation, and delegates all rules and scoring to `HeartsMatchEngine`.

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
