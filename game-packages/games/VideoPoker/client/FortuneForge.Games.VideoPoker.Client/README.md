# Fortune Forge Video Poker client

This package provides the reusable `VideoPokerGame`, browser contracts, and `HttpVideoPokerGateway`. Its playing cards are CSS-generated; no game art assets are included.

## Development preview

Run the local API adapter and Vite preview together:

```powershell
npm install --legacy-peer-deps
npm run dev
```

The preview runs on http://127.0.0.1:5176 and uses the local API on port 5186. Run either process separately with `npm run dev:api` or `npm run dev:ui`.

## Checks

```powershell
npm test
npm run check
npm run build
```
