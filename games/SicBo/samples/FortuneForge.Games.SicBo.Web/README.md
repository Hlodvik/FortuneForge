# Sic Bo local web adapter

Developer-only ASP.NET adapter for the Sic Bo browser contract. The host validates a complete bet slip, generates three cryptographically secure dice values, and delegates every settlement to `SicBoPaytable`.

`roundId` is an in-memory local-play audit correlation ID. It is the seam where an integrating, authenticated host can attach durable round logging; this sample deliberately keeps only a local balance.

Run the API:

```powershell
dotnet run --project games/SicBo/samples/FortuneForge.Games.SicBo.Web
```

The API is available at `http://127.0.0.1:5189/api/games/sic-bo`.
