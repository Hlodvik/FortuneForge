# Approved Midnight Vault export inputs

The fifteen source images are exact copies of the approved inputs, totaling
15,248,459 bytes. `approved-manifest.original.json` preserves the original
approval record; `manifest.json` changes the operational file paths to portable
local sources and retains each original file reference. SHA-256 checks protect
the masters before and after export. The dated originals are preserved.

This optional standalone tool declares sharp 0.35.5. Install its dependencies
in this directory, then provide a **new** output directory:

```powershell
npm install --prefix tools/artwork/midnight-vault
node tools/artwork/midnight-vault/export-approved-art.mjs artifacts/midnight-art-review
```

The exporter refuses an existing destination and writes runtime tiers plus its
manifest/contact sheet there. It does not overwrite application assets. Review
exports before updating runtime art. Current runtime code still requires v1
presentation assets, v2 wordmarks, v3 audio and v4 symbols; none is removed by
this promotion. Historical design experiments are preserved in tools/qa-history.
