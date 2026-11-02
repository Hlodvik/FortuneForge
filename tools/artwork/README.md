# Catalogue artwork tools

Install the pinned image dependency from the repository root:

```powershell
npm ci --prefix tools/artwork
```

Package an existing approved source image with either command:

```powershell
node scripts/prepare-slot-title-card.mjs <game-id> <source-image> [version]
node scripts/prepare-non-slot-title-card.mjs <game-id> <source-image> [version]
```

Both commands produce a 768×384 WebP under the existing catalogue asset directory,
enforce the 180 KB limit, and refuse to overwrite an existing version. They use
this local dependency and do not depend on a particular computer's Codex runtime.
Keep the source master and adjacent artwork metadata for review before integration.

The separate `midnight-vault` directory contains the title-specific reel-art exporter.
