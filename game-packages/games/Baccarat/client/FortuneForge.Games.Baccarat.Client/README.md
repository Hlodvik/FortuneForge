# Fortune Forge Baccarat client

This package provides the reusable `BaccaratGame`, browser contracts, and `HttpBaccaratGateway`. Its cards are CSS-generated; no game image assets are included.

## Development preview

```powershell
npm install --legacy-peer-deps
npm run dev
```

The preview runs on http://127.0.0.1:5177 and uses the local API on port 5187. Run either process independently with `npm run dev:api` or `npm run dev:ui`.

## Checks

```powershell
npm test
npm run check
npm run build
```
