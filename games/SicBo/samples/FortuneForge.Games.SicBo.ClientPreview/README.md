# Sic Bo client preview

Local Vite shell for the published Sic Bo React package. It runs on `http://127.0.0.1:5179` and proxies `/api` to the local Sic Bo adapter on port 5189.

First build the package and start the API in separate terminals:

```powershell
npm --prefix games/SicBo/client/FortuneForge.Games.SicBo.Client run build
dotnet run --project games/SicBo/samples/FortuneForge.Games.SicBo.Web
```

Then install and run the preview:

```powershell
npm --prefix games/SicBo/samples/FortuneForge.Games.SicBo.ClientPreview install --legacy-peer-deps
npm --prefix games/SicBo/samples/FortuneForge.Games.SicBo.ClientPreview run dev
```

Use `npm run check` and `npm run build` in this folder for preview checks.
