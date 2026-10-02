# Casino War client

Reusable React client package for the Fortune Forge Casino War contract. It includes the typed HTTP gateway, responsive `CasinoWarGame` component, and `./styles.css` export.

## Development preview

```powershell
npm install --legacy-peer-deps
npm run dev
```

The preview is available at `http://127.0.0.1:5178` and proxies `/api` to the local API on port 5188. Use `npm run dev:api` or `npm run dev:ui` to start either process separately.

## Checks

```powershell
npm test
npm run check
npm run build
```
