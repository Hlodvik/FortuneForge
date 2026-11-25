# Work history

Record source edits here using the provenance rule in the global `AGENTS.md`.
Earlier provenance comments and logs remain preserved in their existing files.

- Edited: 2026-11-18T23:03:18-06:00 | Computer: Vinnabok | File: C:/Users/Lctiv/.codex/AGENTS.md
  - Changed the provenance rule to use repository-root `workhistory.md` entries instead of inline source comments or separate format-specific logs.
- Edited: 2026-11-18T23:03:18-06:00 | Computer: Vinnabok | File: workhistory.md
  - Created this repository's central work history.
- Edited: 2026-11-18T23:10:57-06:00 | Computer: Vinnabok | File: scripts/ReleaseGuardrails.psm1
  - Allowed exact repository ignore/history files, the route-inventory verifier, and game-root Markdown documents in the Hosting/API compatibility gate while preserving runtime and backend exclusions.
- Edited: 2026-11-18T23:10:57-06:00 | Computer: Vinnabok | File: scripts/tests/release-scope.tests.ps1
  - Covered the benign compatibility exceptions and rejected similarly named, nested, runtime, and backend paths.

- Edited: 2026-11-18T23:13:30-06:00 | Computer: Vinnabok | File: .gitignore
  - Recorded exact reviewed local archive, preview-only audio, and debugger-profile exclusions; retained all files on disk.

## Pending work reviewed for inclusion (2026-11-18T23:13:30-06:00)

- Included the existing Jade Dragon audio/control changes with their six MP3 dependencies after reviewing the diff and passing its Vite production build. The original editor and edit times could not be established.
- Included Arcane Archives task notes, which describe work already integrated.
- Retained about 1.81 GiB of local archives outside Git, including 20 Neon WAV masters used by optional offline regeneration and the local FFmpeg runtime. Those inputs remain on disk; Git alone does not reproduce that optional regeneration workflow.
- Retained the unreferenced Midnight Vault v5 review cue and localhost debugger profiles locally. No preview-only cue is included in the production release.

- Edited: 2026-11-18T23:15:43-06:00 | Computer: Vinnabok | File: scripts/tests/release-scope.tests.ps1
  - Used .NET SHA-256 directly for binary fixture hashes so nested Windows PowerShell release checks do not depend on Get-FileHash module availability.

- Edited: 2026-11-18T23:22:38-06:00 | Computer: Vinnabok | File: scripts/publish-site-hosting.mjs
  - Added the selected Google quota project to every Hosting API and upload request, exposed an injectable HTTP transport, and kept transport/response errors free of credential-bearing details.
- Edited: 2026-11-18T23:22:38-06:00 | Computer: Vinnabok | File: scripts/tests/site-hosting-release.test.mjs
  - Verified quota attribution through the HTTP boundary and full synthetic publisher, exact gzip uploads, and credential-safe failure messages without live network calls.
Edited: 2026-11-18T23:21:59-06:00 | Computer: VINNABOK | File: tools/security/RuntimeScanners.psm1 | Added scoped ZAP automation and explicit opt-in Schemathesis runners with bounded requests and environment-based credentials.
Edited: 2026-11-18T23:21:59-06:00 | Computer: VINNABOK | File: tools/security/tests/runtime-scanners.tests.ps1 | Added offline safety, schema scope, token handling, command boundary, and exit-code checks for runtime scanners.
Edited: 2026-11-18T23:34:24-06:00 | Computer: VINNABOK | File: tools/security/RuntimeScanners.psm1 | Confined Schemathesis runtime cache/state to its report directory and restored caller location after success or failure.
Edited: 2026-11-18T23:34:24-06:00 | Computer: VINNABOK | File: tools/security/tests/runtime-scanners.tests.ps1 | Verified scanner state containment and caller-location restoration for successful and failed Schemathesis launches.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/versions.json | Pinned standalone scanner versions and official archive SHA-256 hashes.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/SecurityTools.psm1 | Centralized tool paths, exact target validation, and ignored per-run report directories.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/Install-SecurityTools.ps1 | Added isolated Windows scanner installation with archive hash verification and a dedicated Python environment.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/trivy.yaml | Configured high/critical vulnerability, secret, and misconfiguration checks with development dependencies and generated-output exclusions.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/Invoke-SecurityScan.ps1 | Added scanner entry point, local/staging target controls, version checks, redacted result counts, and native exit-code propagation.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/tests/security-tools.tests.ps1 | Checked loopback and explicit staging boundaries, hostname bypasses, and unsafe URL rejection.
Edited: 2026-11-18T23:35:34-06:00 | Computer: Vinnabok | File: tools/security/README.md | Documented installation, scan commands, scope limits, authentication, and ignored runtime state.

Edited: 2026-11-18T23:36:11-06:00 | Computer: Vinnabok | File: .agents/skills/graphify/SKILL.md
  Summary: Added an optional repository-scoped Graphify skill using the local wrapper, with rg-first navigation and source verification.

Edited: 2026-11-18T23:33:44-06:00 | Computer: Vinnabok | File: tools/graphify/graphify.ps1
  Summary: Added isolated setup, local code-only refresh, query, explain, path and status commands with native exit handling and ignored runtime output.

Edited: 2026-11-18T23:38:48-06:00 | Computer: Vinnabok | File: tools/graphify/inspect_runtime.py
  Summary: Added portable Python/version and graph inspection without fragile PowerShell native command quoting.

Edited: 2026-11-18T23:22:02-06:00 | Computer: Vinnabok | File: tools/graphify/requirements.txt
  Summary: Pinned Graphify to the verified stable graphifyy 0.9.112 package.

Edited: 2026-11-18T23:26:17-06:00 | Computer: Vinnabok | File: tools/graphify/excludes.txt
  Summary: Defined graph exclusions for generated output, dependencies, artwork, archives and installed skills.

Edited: 2026-11-18T23:39:03-06:00 | Computer: Vinnabok | File: tools/graphify/README.md
  Summary: Documented setup, commands, local-only behavior, graph limitations and reversible skill disabling.
Edited: 2026-11-19T00:02:05-06:00 | Computer: VINNABOK | File: tools/security/tests/fixture_server.py | Added a disposable loopback OpenAPI fixture with truthful method handling, TRACE 405 responses, and no filesystem service or request logs.
Edited: 2026-11-19T00:02:05-06:00 | Computer: VINNABOK | File: tools/security/tests/test_fixture_server.py | Added local HTTP contract tests for advertised methods, unsupported methods, schema responses, and unknown-route isolation.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Baccarat/BaccaratModels.cs
  Summary: Reject out-of-range incoming primary and pair stakes before checked cents conversion while retaining strict settlement conversion and wager increments.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Dice/SicBo/SicBoModels.cs
  Summary: Reject incoming stakes outside the domain rand range before checked cents arithmetic and retain whole-rand validation.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/CasinoWar/CasinoWarModels.cs
  Summary: Validate primary and optional tie stake ranges before checked cents conversion without changing domain limits or internal accounting.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Baccarat/BaccaratContractTests.cs
  Summary: Added service regression cases for decimal extrema and oversized positive/negative primary/pair stakes, proving no store access or wallet/ledger mutation.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/SicBo/SicBoContractTests.cs
  Summary: Added service regression cases for decimal extrema and oversized positive/negative stakes with unchanged wallet, ledger and event assertions.

Edited: 2026-11-19T00:08:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/CasinoWar/CasinoWarContractTests.cs
  Summary: Added service regression cases for decimal extrema and oversized primary/tie stakes that must fail before store access or financial mutation.
Edited: 2026-11-19T00:15:21-06:00 | Computer: VINNABOK | File: tools/security/Invoke-TlsScan.ps1 | Added a manifest-driven single-host TLS certificate/protocol/cipher assessment wrapper with explicit command selection, isolated reports, and no application endpoint checks.

Edited: 2026-11-19T01:23:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx
  Summary: Give the blank Tome artwork an accessible button label and show the shared cauldron stir in its open dialog.

Edited: 2026-11-19T01:25:09-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ingredientFlights.css
  Summary: Anchor the shared stir at the cauldron mouth in the game and dialog while retaining the ingredient and potion animation target.

Edited: 2026-11-19T01:22:36-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionShelfArtwork.css
  Summary: Consolidate desktop shelf placement in the responsive workbench layout while keeping the reusable bottle rack styles.

Edited: 2026-11-19T01:30:15-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/tome.css
  Summary: Position the enlarged cauldron and blank Tome to the lower right of the cabinet, with the shelf above. Keep the compact workbench and wager controls visible at tablet and phone sizes.

Edited: 2026-11-19T01:27:49-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SidecarSpinControls.tsx
  Summary: Support a wager picker position that avoids the entire spin control group when it sits to the left of the cabinet.

Edited: 2026-11-19T17:55:50-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SidecarSpinControls.tsx
  Summary: Remove the wager picker placement added for the former left-side Arcane controls.

Edited: 2026-11-19T17:58:48-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/tome.css
  Summary: Align the Arcane spin, wager, and autospin controls beneath the right edge of the cabinet and fit the control row on short desktop and mobile screens.

Edited: 2026-11-19T18:03:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx
  Summary: Enable the shared docked symbol guide for Arcane Archives.

Edited: 2026-11-19T18:03:32-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/styles.css
  Summary: Load the Arcane symbol guide layout.

Edited: 2026-11-19T18:09:17-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/symbolGuideLayout.css
  Summary: Reserve space for a collapsible guide left of the cabinet on laptops and monitors.

Edited: 2026-11-19T18:04:12-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotContracts/Program.cs
  Summary: Export Arcane guide data from the game's constellation calculator, potion catalog and Tome rules.

Edited: 2026-11-19T18:06:33-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/rules/symbol-guide.json
  Summary: Generate the Arcane side rewards and ingredient details from engine rules.

Edited: 2026-11-19T18:05:30-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/tsconfig.json
  Summary: Enable typed JSON imports for the generated guide data.

Edited: 2026-11-19T18:05:30-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/symbols.ts
  Summary: Include all reel symbols in the guide with short wand, Tome and ingredient entries.

Edited: 2026-11-19T18:04:56-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/config/symbolSets.ts
  Summary: Allow guide entries to name a companion symbol and optional descriptive details.

Edited: 2026-11-19T18:08:23-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SymbolValueGuide.tsx
  Summary: Render wager-scaled symbol pairings and ingredient details on hover, touch and keyboard.

Edited: 2026-11-19T18:10:56-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/styles/pageOverrides.css
  Summary: Style symbol pairings and keep hover descriptions within the panel near its bottom edge.

Edited: 2026-11-19T18:06:01-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts
  Summary: Check that the Arcane guide covers every symbol and retains correct wand multipliers and ingredient descriptions.

Edited: 2026-11-19T18:06:01-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/engineGuide.test.ts
  Summary: Distinguish Arcane side rewards from the audited payline values and energy features.

Edited: 2026-11-19T18:06:49-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/components/SlotDialogs.test.tsx
  Summary: Check that wand amounts follow the wager and ingredient details can be opened and closed.

Edited: 2026-11-19T22:12:38-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-wolfsbane-study.mjs
  Summary: Bundle the authored growth paths in the standalone Wolfsbane preview.

Edited: 2026-11-19T22:12:38-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-effect-study-source.html
  Summary: Add SVG layers for the rail and crest growth.

Edited: 2026-11-19T22:12:38-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.css
  Summary: Keep the opening strands outside the reel window and restore the approved active frame.

Edited: 2026-11-19T22:12:38-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.js
  Summary: Grow tapered red filaments and thicken the approved frame behind their advancing tips.

Edited: 2026-11-19T22:13:57-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-fungal-paths.js
  Summary: Trace branching growth paths along the approved red border artwork.

Edited: 2026-11-19T22:13:05-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/check-wolfsbane-fungal-growth.cjs
  Summary: Check growth progression, reduced-motion playback, and exact artwork restoration.

Edited: 2026-11-19T23:08:03-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-wolfsbane-study.mjs
  Summary: Bundle the renderer that deforms the original border artwork.

Edited: 2026-11-19T23:08:03-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-effect-study-source.html
  Summary: Replace the illustration masks with a canvas for the artwork deformation.

Edited: 2026-11-19T23:08:03-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.css
  Summary: Align the animated artwork with the cabinet and fade into the finished frame.

Edited: 2026-11-19T23:09:34-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.js
  Summary: Play the new opening and settle into the approved border artwork.

Edited: 2026-11-19T23:09:34-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-frame-growth.js
  Summary: Grow the existing frame pixels from staggered points around the cabinet.

Edited: 2026-11-19T23:06:57-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-ambient.js
  Summary: Remove drawn curls while retaining subtle reflection and droplets.


Edited: 2026-11-19T23:10:12-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/check-wolfsbane-fungal-growth.cjs
  Summary: Verify textured growth, approved artwork, and clear reels on desktop and phones.

Edited: 2026-11-19T23:34:09-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-wolfsbane-study.mjs
  Summary: Bundle the bottom, side, and top paths for the standalone preview.

Edited: 2026-11-19T23:34:09-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.js
  Summary: Give individual vines time to spread before the frame settles.

Edited: 2026-11-19T23:35:56-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-frame-growth.js
  Summary: Grow traced pieces of the red artwork with the original top-corner beads at their advancing tips.

Edited: 2026-11-19T23:35:56-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-stems-bottom.js
  Summary: Trace the crossing vines and small branches along the original bottom rail.

Edited: 2026-11-19T23:35:56-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-stems-sides.js
  Summary: Trace side vines, crossings, and hanging tips with continuous main stems.

Edited: 2026-11-19T23:34:32-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-stems-top.js
  Summary: Trace the top rail vines, corner beads, and branching crest in the original artwork.

Edited: 2026-11-19T23:34:51-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/check-wolfsbane-fungal-growth.cjs
  Summary: Recheck the new opening through its longer finish on desktop and phones.


Edited: 2026-11-19T23:41:31-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-effect-study-source.html
  Summary: Add a repeating two-phase displacement filter for the idle border.

Edited: 2026-11-19T23:41:53-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.css
  Summary: Apply the bobbing filter to the completed border and preserve reduced motion.

Edited: 2026-11-19T23:41:53-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/wolfsbane-motion.js
  Summary: Weight the idle motion toward the original vine tips while keeping the stem bases steady.


Edited: 2026-11-19T23:47:46-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-mandrake-study.mjs
  Summary: Build the standalone Mandrake Tonic cabinet preview with the shared reel assets.

Edited: 2026-11-19T23:50:06-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-effect-study-source.html
  Summary: Add the Mandrake preview and its slow displacement filter for the wisps.

Edited: 2026-11-19T23:50:06-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-motion.css
  Summary: Fit the translucent frame around the reels and style the surrounding fog.

Edited: 2026-11-19T23:50:53-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-motion.js
  Summary: Gather soft fog across the cabinet at activation and drift wisps around the active frame.


Edited: 2026-11-19T23:55:53-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-motion.js
  Summary: Animate distinct wisps that lift away from the Mandrake frame during idle.

Edited: 2026-11-19T23:58:47-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-mandrake-study.mjs
  Summary: Bundle the continuous idle renderer with the Mandrake preview.

Edited: 2026-11-19T23:58:47-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-effect-study-source.html
  Summary: Add a dedicated drawing surface for the idle fog.

Edited: 2026-11-19T23:58:47-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-motion.css
  Summary: Fit and blend the idle fog surface around the reel opening.

Edited: 2026-11-19T23:59:06-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-motion.js
  Summary: Use the original frame as the idle texture and preserve the opening and reduced-motion behavior.

Edited: 2026-11-20T00:00:24-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-idle-flow.js
  Summary: Move the original fog as one field and soften its outer edges.


Edited: 2026-11-20T00:05:12-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mandrake-idle-flow.js
  Summary: Slow the idle fog, reduce its displacement and brightness change, and steady the main outline.

Edited: 2026-11-20T00:08:05-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-nightshade-study.mjs
  Summary: Build the standalone Nightshade cabinet preview using the shared cabinet builder.

Edited: 2026-11-20T00:08:05-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/nightshade-effect-study-source.html
  Summary: Add the Nightshade Elixir cabinet preview, potion art and replay control.

Edited: 2026-11-20T00:09:44-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/nightshade-motion.css
  Summary: Mount the translucent amethyst frame and add a slow reflection across the glass.

Edited: 2026-11-20T00:09:54-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/nightshade-motion.js
  Summary: Reveal the frame from the crest and move the idle glints through its etched constellations.


Edited: 2026-11-20T00:12:41-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-mooncap-study.mjs
  Summary: Build the standalone Mooncap cabinet preview using the shared cabinet builder.

Edited: 2026-11-20T00:12:41-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-effect-study-source.html
  Summary: Add the Mooncap Draught preview, potion artwork, and replay control.

Edited: 2026-11-20T00:14:27-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-motion.css
  Summary: Fit the moonstone border and blend reflections into the opal material.

Edited: 2026-11-20T00:14:19-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-motion.js
  Summary: Reveal the crescent and rising frame, then circulate a soft idle reflection.


Edited: 2026-11-20T00:19:29-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-mooncap-study.mjs
  Summary: Bundle the new artwork-based Mooncap refraction renderer into the preview.

Edited: 2026-11-20T00:19:29-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-effect-study-source.html
  Summary: Use one canvas for the Mooncap frame motion and retain the still frame.

Edited: 2026-11-20T00:19:57-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-motion.css
  Summary: Keep the full opal frame fitted around the reel opening during playback.

Edited: 2026-11-20T00:19:57-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-motion.js
  Summary: Run the revised opening and idle with reduced-motion and static fallback behavior.

Edited: 2026-11-20T00:20:47-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/mooncap-refraction.js
  Summary: Form the opal from two light currents and add local highlights inside its existing folds.


Edited: 2026-11-20T00:27:41-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/build-quicksilver-study.mjs
  Summary: Build the standalone Quicksilver preview with the shared cabinet art and styles.

Edited: 2026-11-20T00:27:41-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-effect-study-source.html
  Summary: Add the Quicksilver Essence preview, potion art, and replay control.

Edited: 2026-11-20T00:30:11-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-motion.css
  Summary: Fit the silver frame around a clear reel opening at each width.

Edited: 2026-11-20T00:30:11-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-motion.js
  Summary: Run the liquid-silver opening and idle, with reduced-motion and static fallback behavior.

Edited: 2026-11-20T00:31:03-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-fluid.js
  Summary: Merge rounded silver drops into the frame and move reflections inside the finished material.


Edited: 2026-11-20T00:43:35-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-motion.css
  Summary: Raise Quicksilver above the reel layers and show the rounded inner corners of the original artwork.

Edited: 2026-11-20T00:44:02-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-fluid.js
  Summary: Round and feather the aperture and place opening drops outward so their corner shapes remain visible.


Edited: 2026-11-20T00:47:23-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-cabinet-effects/quicksilver-motion.css
  Summary: Add a CSS idle reflection for browsers that cannot use the Quicksilver WebGL renderer.

Edited: 2026-11-20T01:09:21-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/build-preview.mjs
  Summary: Embed the new potion frame and activation sources in the standalone guide.

Edited: 2026-11-20T01:09:21-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/intro-shell.html
  Summary: Include the potion frame playback and activation scripts and styles.

Edited: 2026-11-20T01:05:30-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-effects.html
  Summary: Show the six approved cabinet effects around the existing potion examples.

Edited: 2026-11-20T01:12:12-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-effects.css
  Summary: Fit the six frames around the symbols and reserve space for amounts on each screen size.

Edited: 2026-11-20T01:05:30-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-effects.js
  Summary: Play the approved idle effects in view and use stills with reduced motion.

Edited: 2026-11-20T01:07:40-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.html
  Summary: Replace the old activation clip with the new cast surface and approved Mandrake frame.

Edited: 2026-11-20T01:13:59-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.css
  Summary: Keep the cast bottle outside the reel symbols and protect payout caption spacing.

Edited: 2026-11-20T01:08:08-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.js
  Summary: Sequence the bottle activation, fog opening, boosted payout, and soft replay.

Edited: 2026-11-20T01:09:20-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-cast.css
  Summary: Style the uncorked bottle layers and their stream in the activation example.

Edited: 2026-11-20T01:12:26-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-cast.js
  Summary: Lift the bottle, remove the cork, and carry the green stream from the mouth to the cabinet.


Edited: 2026-11-20T01:20:16-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.css
  Summary: Place the potion on the right and align the cabinet and caption around it.

Edited: 2026-11-20T01:19:32-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.js
  Summary: Overlap bottle activation with the start of the cabinet fog and reveal the payout once the rim develops.

Edited: 2026-11-20T01:22:31-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-cast.js
  Summary: Uncork upright on the right and curl a soft green plume into the cabinet fog.


Edited: 2026-11-20T01:25:44-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.css
  Summary: Reveal the approved cabinet fog softly from the point of contact on the right rail.

Edited: 2026-11-20T01:25:44-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-scenes.js
  Summary: Start the cabinet effect at liquid contact and time the payout to the formed rim.

Edited: 2026-11-20T01:32:04-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-cast.css
  Summary: Fit the registered bottle layers, hold the empty glass after the pour, and keep the cork inside phone viewports.

Edited: 2026-11-20T01:32:04-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-cast.js
  Summary: Drain the bottle, keep the cork movement within its artwork, and carry textured liquid into the approved right-rail fog.



Edited: 2026-11-20T01:49:58-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-scenes.html
  Summary: Show all six potion variants in the guide activation scene.

Edited: 2026-11-20T01:50:49-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-cast.css
  Summary: Fit the activation clip and potion picker at phone and desktop widths.

Edited: 2026-11-20T01:51:15-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-cast.js
  Summary: Play the selected activation, restart it on demand, and honor reduced motion.

Edited: 2026-11-20T01:50:35-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-scenes.css
  Summary: Remove superseded activation rules and retain the brewing illustrations.

Edited: 2026-11-20T01:50:35-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-type.css
  Summary: Keep the effect examples in the guide typeface after replacing the old activation labels.

Edited: 2026-11-20T01:49:58-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-shell.html
  Summary: Drop the superseded Mandrake-only activation script from the guide.

Edited: 2026-11-20T01:50:49-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/build-preview.mjs
  Summary: Build the guide and standalone activation scene from the same sources and clips.

Edited: 2026-11-20T01:54:04-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/render-potion-activations.py
  Summary: Render matched bottle pours and cabinet effects for the six potion activation previews.

Edited: 2026-11-20T01:57:30-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/intro-shell.html
  Summary: Explain the Potion shelf Use action, cabinet frame change, and three-spin duration in the guide.


Edited: 2026-11-20T02:05:45-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneIntroduction.tsx
  Summary: Use the illustrated guide as Arcane Archives win help and retain the entry action.

Edited: 2026-11-20T02:05:45-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/arcaneIntroduction.css
  Summary: Fit the illustrated guide and its controls inside the game dialog at phone and desktop widths.

Edited: 2026-11-20T02:05:45-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Integrate the approved guide illustrations, align examples with the current engine, and keep chapter navigation and keyboard controls in the game.

Edited: 2026-11-20T02:01:46-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/guideAssets.ts
  Summary: Resolve the guide artwork through game-owned, fingerprinted asset URLs.

Edited: 2026-11-20T02:07:46-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Test first-visit help, chapter navigation, keyboard close, mobile potion previews, and focus restoration.


Edited: 2026-11-20T02:21:56-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx
  Summary: Render clickable potion bottles below the cauldron and show their effects on hover or focus.

Edited: 2026-11-20T02:25:17-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/tome.css
  Summary: Size and position the scrollable bottle row and its tooltips across workbench layouts.

Edited: 2026-11-20T02:22:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionMotion.tsx
  Summary: Anchor potion motion to the clicked or newly bottled potion instead of the shelf.

Edited: 2026-11-20T02:22:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionMotion.css
  Summary: Center the bottling transfer on its destination bottle.

Edited: 2026-11-20T02:19:18-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Explain direct bottle activation and effect previews below the cauldron.

Edited: 2026-11-20T02:19:18-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-shell.html
  Summary: Update the standalone guide to describe the new bottle controls.

Edited: 2026-11-20T02:22:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePotions.cs
  Summary: Use the new inventory terminology in the unavailable-potion message.

Edited: 2026-11-20T02:22:11-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionShelfArtwork.tsx
  Summary: Remove the obsolete potion shelf presentation.

Edited: 2026-11-20T02:22:11-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionShelfArtwork.css
  Summary: Remove the obsolete potion shelf presentation.

Edited: 2026-11-20T02:21:19-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Exercise below-cauldron bottle activation and effect help on desktop and touch phones.


Edited: 2026-11-20T02:45:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaylineTests.cs
  Summary: Exercise three-symbol positions, the removal of four-symbol payouts, and historical replay.

Edited: 2026-11-20T02:59:26-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePotionDomainTests.cs
  Summary: Verify Mandrake's wager-based floor, potency, eligible symbols, and legacy payouts.

Edited: 2026-11-20T02:44:18-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcaneTomeTests.cs
  Summary: Verify the Grimoire spin lock, boosted reels, recipe changes, and legacy active boosts.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Prevent Arcane paid admission while a Grand Grimoire is awaiting activation.

Edited: 2026-11-20T02:42:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Return an actionable response when a Grimoire boost must be selected.

Edited: 2026-11-20T02:54:00-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs
  Summary: Replay the matching and Mandrake rules for the version captured at admission.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Evaluation/CombinationEvaluator.cs
  Summary: Use Arcane's exact-three matching in evidence evaluation.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Payouts/PayoutCalculator.cs
  Summary: Price distinct Arcane triples and full paylines consistently.

Edited: 2026-11-20T02:44:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Honor the Arcane engine version captured at admission.

Edited: 2026-11-20T02:59:26-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Verify updated guide and recipes, touch preview, direct activation, and compact phone controls.

Edited: 2026-11-20T02:43:25-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx
  Summary: Expose Grimoire readiness and the disabled spin state in the game controls.

Edited: 2026-11-20T02:46:31-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx
  Summary: Verify autoplay, manual and keyboard spins respect the pending Grimoire.

Edited: 2026-11-20T02:43:25-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Pause Arcane play until a required Grimoire page is activated.

Edited: 2026-11-20T02:42:38-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/engineGuide.test.ts
  Summary: Assert the updated Arcane payline rules and labels.

Edited: 2026-11-20T02:48:04-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionMotion.tsx
  Summary: Play the approved activation and cabinet trim for all six potions on the live cabinet.

Edited: 2026-11-20T02:44:02-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionPour.tsx
  Summary: Draw the right-side bottle lift, drain, and pour using live bottle and cabinet coordinates.

Edited: 2026-11-20T02:43:29-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomeBook.tsx
  Summary: Keep Grimoire content visible through page turns.

Edited: 2026-11-20T02:44:18-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx
  Summary: Highlight the ready lectern and allow touch players to preview potions by holding a bottle.

Edited: 2026-11-20T02:43:55-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/arcaneArchivesWordmark.css
  Summary: Reserve a phone control area below the scrollable cabinet.

Edited: 2026-11-20T02:42:38-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/manifest.ts
  Summary: Describe Arcane's three- and five-symbol wins in the help configuration.

Edited: 2026-11-20T02:43:11-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionArtwork.ts
  Summary: Associate each potion with its approved activation palette, bottle layers, and cabinet trims.

Edited: 2026-11-20T02:48:04-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionMotion.css
  Summary: Fit the approved trims around the live reels without growing the mobile viewport.

Edited: 2026-11-20T02:42:38-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/symbols.ts
  Summary: Use Scribe Mark names and display the three- and five-symbol values.

Edited: 2026-11-20T02:46:35-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/tome.css
  Summary: Fit the cabinet above the narrow-phone controls and disable native long-press menus on bottles.

Edited: 2026-11-20T02:43:33-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/tomeBook.css
  Summary: Preserve Grimoire page ink during turns and highlight a ready lectern.

Edited: 2026-11-20T02:59:26-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Align the in-game guide with the final payline, Grimoire, and bottle rules.

Edited: 2026-11-20T02:52:34-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/rules/symbol-guide.json
  Summary: Use the Scribe Mark label for the Mandrake effect.

Edited: 2026-11-20T02:42:38-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/README.md
  Summary: Document Arcane's exact-three and full-line rules.

Edited: 2026-11-20T02:54:00-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneArchivesEngine.cs
  Summary: Version the exact-three rules and calibrate Arcane's payouts while preserving v5.

Edited: 2026-11-20T02:44:17-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePaidSlotOutcomes.cs
  Summary: Use the captured Arcane rules for existing paid admissions.

Edited: 2026-11-20T02:59:26-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePotions.cs
  Summary: Increase Mandrake's Scribe Mark bonus and scale the bonus floor by wager.

Edited: 2026-11-20T02:48:07-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneTome.cs
  Summary: Adopt approved Grimoire recipes with legacy boost continuity.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/SlotDefinitions.cs
  Summary: Capture the opt-in payline matching mode with each game definition.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEngine.Math.cs
  Summary: Use the opted-in Arcane geometry without changing classic rules.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ExactStraightThreePaylines.cs
  Summary: Find exact straight and diagonal triples from all eligible reels.

Edited: 2026-11-20T02:43:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/FrozenSlotDefinitions.cs
  Summary: Preserve payline matching mode when freezing and cloning a game definition.

Edited: 2026-11-20T02:54:37-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotContracts/Program.cs
  Summary: Read canonical route metadata when exporting current engine guide contracts.

Edited: 2026-11-20T02:46:17-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/Program.cs
  Summary: Expose the Mandrake-only wager stress scenario in the math runner.

Edited: 2026-11-20T02:54:00-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/ArcanePotionSimulation.cs
  Summary: Measure low and high wagers, ingredient misses, and selective Mandrake use.

Edited: 2026-11-20T02:47:22-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/intro-shell.html
  Summary: Update the standalone guide's Grimoire, payline, and bottle instructions.

Edited: 2026-11-20T02:59:28-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/potion-effects.html
  Summary: Explain Mandrake's higher bonus and show the updated payout examples.

Edited: 2026-11-20T02:55:39-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/visualizations/2026/11/19/arcane-archives-introduction/render-potion-activations.py
  Summary: Refresh payout captions in the activation clips while keeping the approved motion.


Edited: 2026-11-20T03:01:17-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/components/SlotDialogs.test.tsx
  Summary: Verify the updated Scribe Mark label in the Symbol Values panel.

Edited: 2026-11-20T03:02:12-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json
  Summary: Refresh the Arcane payout scale from the running engine.

Edited: 2026-11-20T03:02:12-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Record the rebuilt engine-guide fixture and source evidence.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonPayoutSchedule.cs
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/FortuneForge.Games.NeonNights.csproj
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonArcadeSlotDefinitions.cs
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/manifest.ts
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/disco.ts
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:16:38-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/assets/rules/payout-schedule-v20.json
  Summary: Version the current Neon payout schedule while retaining earlier replay rules.

Edited: 2026-11-20T15:17:14-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPayoutVersionTests.cs
  Summary: Replay captured v19 base, Car, and Disco outcomes after the current schedule advances.

Edited: 2026-11-20T15:21:57-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/assets/rules/payout-schedule-v20.json
  Summary: Calibrate the current base payout scale for combined Base, Car and Disco RTP of 93–95 percent.


Edited: 2026-11-20T15:25:43-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPackageTests.cs
  Summary: Verify the candidate 0.815 Neon base scale while preserving captured historical award fixtures.

Edited: 2026-11-20T15:25:43-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/NeonArcadeTests.cs
  Summary: Verify the candidate 0.815 Neon base scale while preserving captured historical award fixtures.

Edited: 2026-11-20T15:19:04-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts
  Summary: Expose the bottle wager and reserve a game-owned wager readout.

Edited: 2026-11-20T15:19:04-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SidecarSpinControls.tsx
  Summary: Support the locked bet state and custom wager readout.

Edited: 2026-11-20T15:19:04-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Validate the optional earned wager on Arcane potion bottles.

Edited: 2026-11-20T15:25:20-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Use the earned wager for paid spins and restore the player's selected wager after the potion ends.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx
  Summary: Connect the Arcane wager readout and bottle tooltip to the active state.

Edited: 2026-11-20T15:20:44-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx
  Summary: Verify the three locked requests and restoration after expiry.

Edited: 2026-11-20T15:30:25-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Check the wager lock and animated display on desktop and phones.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionWager.tsx
  Summary: Count the wager toward the earned stake on confirmed activation.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionWager.css
  Summary: Enlarge the wager and add a gold glow during activation.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/manifest.ts
  Summary: Register the Arcane potion wager readout.

Edited: 2026-11-20T15:20:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx
  Summary: Show each bottle's earned wager in its description.

Edited: 2026-11-20T15:22:58-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Explain the potion wager lock and update the Mandrake and Wolfsbane examples.

Edited: 2026-11-20T15:17:54-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-shell.html
  Summary: Explain the potion wager lock in the standalone guide.

Edited: 2026-11-20T15:22:58-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-effects.html
  Summary: Update locked-wager potion examples in the standalone guide.

Edited: 2026-11-20T15:22:58-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/render-potion-activations.py
  Summary: Update the Mandrake and Wolfsbane payout captions.

Edited: 2026-11-20T15:24:56-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/ArcanePotionSimulation.cs
  Summary: Measure ordinary RTP separately and enforce earned potion wagers in the simulator.

Edited: 2026-11-20T15:24:56-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/Program.cs
  Summary: Expose base-only Arcane RTP simulation.

Edited: 2026-11-20T15:19:12-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePotions.cs
  Summary: Track per-unit paid wager contributions and derive a legal bottle wager.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/AccountModels.cs
  Summary: Carry potion wager rules in signed settlement evidence.

Edited: 2026-11-20T15:21:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.ArcanePotions.cs
  Summary: Apply the earned wager to newly activated and older potions.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Reject paid spins at an incorrect wager before any charge.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs
  Summary: Use the charged wager to track contributions at settlement.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Expose the locked-wager conflict response.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Models/ArcanePotionView.cs
  Summary: Return the earned wager in the inventory and active potion payloads.

Edited: 2026-11-20T15:19:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Verify logged wager locks and preserve historical replay.

Edited: 2026-11-20T15:21:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/SlotRoundAuditTests.cs
  Summary: Verify historical round replay and reject tampered wager evidence.

Edited: 2026-11-20T15:20:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePotionDomainTests.cs
  Summary: Verify weighted wagers, rounding, and compatibility.

Edited: 2026-11-20T15:20:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaidWorkerTests.cs
  Summary: Verify correct locked stakes across the signed worker contract.

Edited: 2026-11-20T15:20:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcaneTomeTests.cs
  Summary: Verify wager lock admission before wallet mutation.

Edited: 2026-11-20T15:22:39-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneArchivesEngine.cs
  Summary: Calibrate the v7 base paytable and retain the earlier profiles for replay.

Edited: 2026-11-20T15:22:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs
  Summary: Read v7 and earlier Arcane payout profiles by signed engine version.

Edited: 2026-11-20T15:22:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs
  Summary: Register the correct historical and current Arcane payout tables.

Edited: 2026-11-20T15:31:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaylineTests.cs
  Summary: Check the v7 payout table and historical outcomes.

Edited: 2026-11-20T02:54:37-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotContracts/Program.cs
  Summary: Export Arcane's versioned paytable in the guide contract.

Edited: 2026-11-20T15:23:17-06:00 | Computer: VINNABOK | File: fortuneforge.client/scripts/export-slot-guide-contracts.mjs
  Summary: Carry Arcane's game-specific payout table into the runtime guide.

Edited: 2026-11-20T15:23:17-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/engineGuide.ts
  Summary: Read game-specific line payouts from the exported guide contract.

Edited: 2026-11-20T15:29:57-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json
  Summary: Refresh Arcane's base wager and symbol payouts from v7.

Edited: 2026-11-20T15:29:57-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh Arcane payout fixtures and source evidence for v7.

Edited: 2026-11-20T15:32:12-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonPayoutSchedule.cs
  Summary: Use the authoritative 93–95 percent combined RTP band and flag measurements below its floor as well as above its ceiling.


Edited: 2026-11-20T15:32:12-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonArcadeSlotDefinitions.cs
  Summary: Use the authoritative 93–95 percent combined RTP band and flag measurements below its floor as well as above its ceiling.


Edited: 2026-11-20T15:32:12-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/NeonSlotSimulation.cs
  Summary: Use the authoritative 93–95 percent combined RTP band and flag measurements below its floor as well as above its ceiling.


Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/types.ts
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/roundState.ts
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/api.ts
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/useNeonNights.ts
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/NeonNightsView.tsx
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/components/NeonDiscoHelp.tsx
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/roundState.test.mjs
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/api.test.tsx
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.

Edited: 2026-11-20T15:52:42-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/controller.test.tsx
  Summary: Preserve and enforce the Disco triggering wager through restoration, queued rounds, settlement, and controls; cover the wager lock contract.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonPlayPolicy.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/AccountModels.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Models/SpinModels.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonAdmin.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonNitroChoice.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/DemoSlotsController.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPlayPolicyTests.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.
Edited: 2026-11-20T15:54:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/NeonDiscoWagerTests.cs | Added independent Disco wager lock persistence, enforcement, API carry state, or progression/replay tests.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts
  Summary: Carry rare potion variants through the slot experience contract.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Validate rare potion variant identifiers in the slot API.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionArtwork.ts
  Summary: Map each rare potion to its registered full and empty bottle artwork, rim, and liquid colors.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx
  Summary: Render variant-specific bottles while retaining ingredient art fallback.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionMotion.tsx
  Summary: Show rare cabinet rims and multicolor pours using the active bottle variant.

Edited: 2026-11-20T16:04:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionMotion.css
  Summary: Animate the ambient glow on the two rare cabinet frames.

Edited: 2026-11-20T16:05:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Verify both rare bottles, active cabinet frames, guide previews, and locked wagers.

Edited: 2026-11-20T16:04:23-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/ArcanePotionSimulation.cs
  Summary: Measure natural and isolated rare potion frequency, return, and volatility.

Edited: 2026-11-20T16:00:34-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/Program.cs
  Summary: Accept the Arcane rare-potion simulation modes.

Edited: 2026-11-20T16:04:05-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePotions.cs
  Summary: Brew and apply the two rare four-pair potions at their earned wager.

Edited: 2026-11-20T16:03:29-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneArchivesEngine.cs
  Summary: Version the rare potion rules while preserving older Arcane outcomes.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePaidSlotOutcomes.cs
  Summary: Apply rare variant effects only on supporting engine versions.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/PaidSlotOutcome.cs
  Summary: Transport rare potion variants in the signed paid outcome contract.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Carry Arcane potion variants into signed worker requests.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Models/ArcanePotionView.cs
  Summary: Return rare variant names, effects, and identifiers to the slot client.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.ArcanePotions.cs
  Summary: Settle rare brew progression according to the admitted engine version.

Edited: 2026-11-20T16:01:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Replay rare and historical brew rules using the signed version.

Edited: 2026-11-20T16:03:29-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaidWorkerTests.cs
  Summary: Verify rare potions in signed outcomes and previous-version replay.

Edited: 2026-11-20T16:02:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePotionDomainTests.cs
  Summary: Verify exact recipes, locked wagers, payout effects, and every supported reel layout.

Edited: 2026-11-20T16:04:57-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/guideAssets.ts
  Summary: Resolve both rare bottles, frames, and activation previews in the embedded guide.

Edited: 2026-11-20T16:04:57-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Explain the two rare four-pair recipes and preview their effects.

Edited: 2026-11-20T15:59:59-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-shell.html
  Summary: Introduce rare potion brewing in the standalone illustrated guide.

Edited: 2026-11-20T16:04:57-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-effects.html
  Summary: Add both rare potion appearances, effects, and replay choices.

Edited: 2026-11-20T16:04:57-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-scenes.html
  Summary: Add the rare recipes to the standalone potion scenes.

Edited: 2026-11-20T16:04:22-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/render-potion-activations.py
  Summary: Render the two distinct rare pours and their original cabinet frames.

Edited: 2026-11-20T16:08:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh Arcane v8 guide evidence from the native runtime export.

Edited: 2026-11-20T16:12:47-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Link directly to the two rare potion recipes from the in-game guide menu.

Edited: 2026-11-20T16:12:55-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/slots.visual.spec.ts
  Summary: Verify both rare recipes and their effects through the in-game How to win guide.

Edited: 2026-11-20T16:12:52-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/intro-shell.html
  Summary: Add a Rare potions shortcut to the standalone guide navigation.

Edited: 2026-11-20T16:12:52-06:00 | Computer: VINNABOK | File: C:\Users\Lctiv\.codex\visualizations\2026\11\19\arcane-archives-introduction/potion-effects.html
  Summary: Anchor the standalone rare potion entries.

Edited: 2026-11-20T16:16:05-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json
  Summary: Refresh current Arcane and Neon guide prices from the candidate runtime.

Edited: 2026-11-20T16:16:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh all engine guide fixtures and source hashes for the combined release.

Edited: 2026-11-20T16:17:30-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/guide/illustrated-guide.html
  Summary: Normalize trailing whitespace in the embedded font license for the release.

Edited: 2026-11-20T16:20:10-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEngine.cs
  Summary: Preserve boosted reel model identity during preparation and audit recording.

Edited: 2026-11-20T16:20:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/SlotFeatureSpinTests.cs
  Summary: Verify Wukong boosted spins replay and preparation cannot replace the effective reel model.

Edited: 2026-11-20T16:22:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Regenerate release guide evidence from the corrected classic slot runtime.

Edited: 2026-11-20T17:01:26-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/GameNavbar.contract.test.tsx
  Summary: Allow the real-route navbar integration tests enough time to load cold workspace bundles.

Edited: 2026-11-20T17:56:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionSettlementPipelineFirestoreTests.cs
  Summary: Exercise the arcade executor through the wallet-owned credit transaction.

Edited: 2026-11-20T17:56:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/FirestoreArcadeCompetitionPayoutCreditorTests.cs
  Summary: Cover legacy payout replay and conflicting ledger identity.

Edited: 2026-11-20T17:56:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaidWorkerTests.cs
  Summary: Verify signed Arcane settlement with package-owned options.

Edited: 2026-11-20T17:56:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaylineTests.cs
  Summary: Use the package codec in Arcane worker and replay coverage.

Edited: 2026-11-20T18:02:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.Documents.cs
  Summary: Reuse the wallet-owned balance and transaction paths.

Edited: 2026-11-20T18:02:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.cs
  Summary: Reuse the wallet currency and fractional-cent field identifiers.

Edited: 2026-11-20T17:59:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionConfiguration.cs
  Summary: Register the wallet-backed competition creditor.

Edited: 2026-11-20T17:56:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/FirestoreArcadeCompetitionPayoutCreditor.cs
  Summary: Delegate crediting to the wallet while retaining competition policy and legacy receipt IDs.

Edited: 2026-11-20T17:59:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/AccountServicesConfiguration.cs
  Summary: Provision the wallet credit store with account infrastructure.

Edited: 2026-11-20T17:58:34-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs
  Summary: Opt existing authenticated routes into private worker forwarding without a second writer.

Edited: 2026-11-20T18:01:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeGameService.cs
  Summary: Use the game package's terminal audit and evidence identifiers.

Edited: 2026-11-20T17:55:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightController.cs
  Summary: Reuse public game contracts and preserve legacy host endpoints.

Edited: 2026-11-20T17:55:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightGameService.cs
  Summary: Use the package reducer and keep compatibility for host-managed sessions.

Edited: 2026-11-20T18:01:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PracticeActionReplayAudit.cs
  Summary: Keep shared journal verification in the host and delegate puzzle rules to their packages.

Edited: 2026-11-20T18:01:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PracticeOutcomeAudit.cs
  Summary: Delegate terminal outcome verification to the 2048 and Drop Merge packages.

Edited: 2026-11-20T17:55:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Use package-owned codes and option serialization in the signed outcome path.

Edited: 2026-11-20T17:59:51-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include the package-owned 2048 server and its tests.

Edited: 2026-11-20T18:02:59-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document opt-in verification and prerequisites for a safe 2048 cutover.

Edited: 2026-11-20T17:59:47-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh captured slot guide source fingerprints after worker contract changes.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePaidSlotOutcomes.cs
  Summary: Validate Arcane options and identify Arcane responses within the package.

Edited: 2026-11-20T17:59:51-06:00 | Computer: VINNABOK | File: games/FortuneForge.Games.slnx
  Summary: Build the standalone 2048 server and its tests with the game projects.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/Program.cs
  Summary: Use shared rejection of foreign non-default worker options.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/Program.cs
  Summary: Use shared rejection of foreign non-default worker options.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/Program.cs
  Summary: Use shared rejection of foreign non-default worker options.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/Program.cs
  Summary: Validate Disco options with the game-owned codec.

Edited: 2026-11-20T17:56:51-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/game-release.json
  Summary: Select the private stateful game backend.

Edited: 2026-11-20T17:55:38-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/GameComputeModule.cs
  Summary: Retain current compute routes using package-owned contracts.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.ClassicSlot.Server/ClassicPaidSlotOutcomes.cs
  Summary: Use common validation for games without game-specific worker options.

Edited: 2026-11-20T18:00:25-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerAuthentication.cs
  Summary: Expose the request body digest only after worker assertion verification.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/PaidSlotOutcome.cs
  Summary: Carry package-owned worker options under their existing wire names.

Edited: 2026-11-20T17:57:16-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Recognize 2048 as a packaged worker with no worker feature flags.

Edited: 2026-11-20T17:59:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs
  Summary: Exercise opt-in route matching and existing feature gates.

Edited: 2026-11-20T17:56:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PaidSlotWorkerWireTests.cs
  Summary: Check backward-compatible Arcane and Neon outcome requests.

Edited: 2026-11-20T18:02:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletCreditStore.cs
  Summary: Atomically post credit and its replay-safe receipt.

Edited: 2026-11-20T18:02:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletDocuments.cs
  Summary: Centralize wallet balances, transaction paths, and currency fields.

Edited: 2026-11-20T17:55:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletCreditStore.cs
  Summary: Define the wallet credit and receipt contract.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/PaidArcaneSpinOptions.cs
  Summary: Own versioned Tome and potion options in the Arcane package.

Edited: 2026-11-20T17:58:56-06:00 | Computer: VINNABOK | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeActionReplay.cs
  Summary: Own independent Drop Merge command replay in the game package.

Edited: 2026-11-20T18:01:56-06:00 | Computer: VINNABOK | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeAudit.cs
  Summary: Own independent Drop Merge terminal rules and evidence identifiers.

Edited: 2026-11-20T17:55:10-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/PaidNeonSpinOptions.cs
  Summary: Own signed Disco options and validation in the Neon package.

Edited: 2026-11-20T17:56:51-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/FortuneForge.Games.TwentyFortyEight.Server.csproj
  Summary: Define the private 2048 backend independent of the shared API project.

Edited: 2026-11-20T18:00:37-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/TwentyFortyEightEndpoints.cs
  Summary: Map authenticated game actions to package-owned sessions.

Edited: 2026-11-20T18:00:55-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/TwentyFortyEightFirestoreSessions.cs
  Summary: Persist state, undo, receipts, and audit evidence atomically.

Edited: 2026-11-20T18:00:55-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/GameWorkerModule.cs
  Summary: Connect the private hosting runtime to 2048 game endpoints.

Edited: 2026-11-20T17:58:56-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightActionReplay.cs
  Summary: Own independent 2048 action replay in the game package.

Edited: 2026-11-20T17:58:45-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightAudit.cs
  Summary: Capture and validate 2048 audit evidence and settlement analytics.

Edited: 2026-11-20T17:54:55-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightContracts.cs
  Summary: Own game endpoints' response, command, and error contracts.

Edited: 2026-11-20T17:54:55-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightSession.cs
  Summary: Own the authoritative 2048 session transitions.

Edited: 2026-11-20T17:58:45-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/tests/FortuneForge.Games.TwentyFortyEight.Tests/FortuneForge.Games.TwentyFortyEight.Tests.csproj
  Summary: Provide an emulator regression test project within the game package.

Edited: 2026-11-20T18:01:23-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/tests/FortuneForge.Games.TwentyFortyEight.Tests/TwentyFortyEightFirestoreSessionsTests.cs
  Summary: Verify account isolation, replica durability, command retries, and evidence continuity.

Edited: 2026-11-20T17:58:56-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/PuzzleReplayEvidence.cs
  Summary: Share generic puzzle audit evidence parsing and independent RNG replay utilities.

Edited: 2026-11-20T18:19:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/FirestoreWalletRoundSettlementStoreTests.cs
  Summary: Verify exact replay, gross affordability, concurrent settlement, ledger integrity, and atomic legacy transaction behavior.

Edited: 2026-11-20T18:19:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs
  Summary: Cover new-start gating, legacy and durable session routes, and practice requests.

Edited: 2026-11-20T18:22:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/JournalManifestFirestoreTests.cs
  Summary: Verify paged journal integrity, bounded retention, and legal holds.

Edited: 2026-11-20T18:15:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaidWorkerTests.cs
  Summary: Exercise the Arcane package-owned Tome store alongside the existing signed spin workflow.

Edited: 2026-11-20T18:15:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcaneTomeTests.cs
  Summary: Exercise package-owned activation with legacy guards and the integrated paid settlement.

Edited: 2026-11-20T18:16:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/NeonArcadePaidFirestoreTests.cs
  Summary: Verify paid Disco compatibility with the legacy missing wager lock.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Services/AccountService.Slots.cs
  Summary: Remove Arcane feature mutations from the shared account service.

Edited: 2026-11-20T18:14:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.AccountReads.cs
  Summary: Reuse the wallet's legacy balance reader.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.Documents.cs
  Summary: Reuse the shared physical slot guard key and collection mapping.

Edited: 2026-11-20T18:14:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonAdmin.cs
  Summary: Use Neon-owned Disco preview grant validation and state.

Edited: 2026-11-20T18:15:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Delegate Arcane feature and Neon Disco admission rules to their packages.

Edited: 2026-11-20T18:15:40-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Read saved Arcane features and Neon Disco state through package-owned decoders.

Edited: 2026-11-20T18:19:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs
  Summary: Atomically settle slot funds through the wallet and delegate Arcane and Disco updates to their packages.

Edited: 2026-11-20T18:14:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletCreditStore.cs
  Summary: Reuse the shared cent-precision schema with the round settlement store.

Edited: 2026-11-20T18:14:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletDocuments.cs
  Summary: Own canonical wallet reads, balance updates, and transaction identity.

Edited: 2026-11-20T18:19:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs
  Summary: Post debit and credit once per prepared outcome and validate the immutable financial receipt.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IAccountStore.cs
  Summary: Remove Arcane feature mutation methods from the account store interface.

Edited: 2026-11-20T18:14:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs
  Summary: Define the wallet round settlement terms and immutable receipt contract.

Edited: 2026-11-20T18:14:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/AccountServicesConfiguration.cs
  Summary: Register the wallet round settlement service.

Edited: 2026-11-20T18:18:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs
  Summary: Map the existing Arcane URLs to game-owned endpoints with account resolution and rate limiting.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/SlotServicesConfiguration.cs
  Summary: Register the Arcane package's feature store.

Edited: 2026-11-20T18:18:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Delegate Arcane feature routes to the game package.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the package-owned Arcane backend.

Edited: 2026-11-20T18:19:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs
  Summary: Preserve session ownership across a 2048 rollout and new-start pause.

Edited: 2026-11-20T18:16:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightGameService.cs
  Summary: Check the run identity and version in legacy optimistic updates.

Edited: 2026-11-20T18:16:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityIntegrityAuditor.cs
  Summary: Handle existing and paged journal manifests through the audit reader.

Edited: 2026-11-20T18:16:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityJournalManifestAudit.cs
  Summary: Verify committed journal page count, identities, and event order.

Edited: 2026-11-20T18:15:40-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SecurityAuditRawRetention.cs
  Summary: Purge journal pages in bounded transactions after verifying retention eligibility.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Models/SpinModels.cs
  Summary: Use the Arcane package's potion response model.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include the Arcane backend in the platform solution.

Edited: 2026-11-20T18:20:52-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document the order of the 2048 cutover, parallel session routing, and new-start pause behavior.

Edited: 2026-11-20T18:22:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh verified guide source fingerprints after the game ownership changes.

Edited: 2026-11-20T18:18:13-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureEndpoints.cs
  Summary: Own the authenticated Arcane potion and Tome endpoints within the game package.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureRequests.cs
  Summary: Own the request shapes for potion activation and Tome selection.

Edited: 2026-11-20T18:14:27-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureStore.cs
  Summary: Replace the old Arcane account-store partials and preserve existing guards, admission records, and transitions.

Edited: 2026-11-20T18:18:13-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/FortuneForge.Games.ArcaneArchives.Server.csproj
  Summary: Build the Arcane feature backend with shared slot contracts and Firestore.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePotionView.cs
  Summary: Move the potion response model from the shared API into Arcane.

Edited: 2026-11-20T18:14:57-06:00 | Computer: VINNABOK | File: games/FortuneForge.Games.slnx
  Summary: Include the Arcane backend in the games solution.

Edited: 2026-11-20T18:13:57-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonDiscoRoundState.cs
  Summary: Own legacy Disco storage, locked wagers, play progression, and settlement rules in Neon.

Edited: 2026-11-20T18:15:40-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonDiscoRoundStateTests.cs
  Summary: Verify the Disco transition and legacy storage contract in the Neon package.

Edited: 2026-11-20T18:16:02-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/TwentyFortyEightFirestoreSessions.cs
  Summary: Persist bounded manifest pages with atomic legacy conversion and validate the current tail.

Edited: 2026-11-20T18:14:52-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightAudit.cs
  Summary: Use a stable audit episode identifier across manifest schemas.

Edited: 2026-11-20T18:16:45-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightSession.cs
  Summary: Recognize durable session IDs and restart audit versions on each new run.

Edited: 2026-11-20T18:17:35-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/tests/FortuneForge.Games.TwentyFortyEight.Tests/TwentyFortyEightFirestoreSessionsTests.cs
  Summary: Verify long journals, malformed page rejection, and transactional legacy conversion.

Edited: 2026-11-20T18:14:52-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameSecurityEventDocuments.cs
  Summary: Define the paged manifest schema and event identity contract.

Edited: 2026-11-20T18:14:27-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/SlotSpinGuard.cs
  Summary: Define the physical guard identity and timing shared by compatibility hosts and game features.

Edited: 2026-11-20T18:36:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs
  Summary: Verify Drop Merge session routing and the opt-in Arcane feature routes.

Edited: 2026-11-20T18:39:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestObservationTests.cs
  Summary: Cover the prepared Arcane action in request observation.

Edited: 2026-11-20T18:44:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePreparedSpinTests.cs
  Summary: Verify recovery after timeout, gross funds changes, pause, changed terms, and raw evidence retention.

Edited: 2026-11-20T18:34:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/AccountModels.cs
  Summary: Carry the original request observation with a recovered spin.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/NeonCarRespinModels.cs
  Summary: Reuse the package-owned Car respin offer contract.

Edited: 2026-11-20T18:34:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/NeonNitroChoiceModels.cs
  Summary: Reuse the package-owned Nitro choice offer contract.

Edited: 2026-11-20T18:41:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Services/AccountService.Slots.cs
  Summary: Pass the prepared operation identity and new-start policy.

Edited: 2026-11-20T18:40:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonAdmin.cs
  Summary: Use package Car round initialization and storage fields.

Edited: 2026-11-20T18:40:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonCarRespin.cs
  Summary: Apply Neon-owned respin eligibility, receipts, and progression.

Edited: 2026-11-20T18:40:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonNitroChoice.cs
  Summary: Validate saved spinoffs and choices in the Neon package.

Edited: 2026-11-20T18:44:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.PreparedSlotOperations.cs
  Summary: Persist and replay immutable prepared rounds with an operation index.

Edited: 2026-11-20T18:41:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Recover exact Arcane preparations atomically and use Neon-owned Nitro offer setup.

Edited: 2026-11-20T18:40:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Project Car round state through the Neon package.

Edited: 2026-11-20T18:40:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs
  Summary: Bind recovered settles to the prepared outcome and apply package Car progression.

Edited: 2026-11-20T18:41:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IAccountStore.cs
  Summary: Accept an optional stable operation identifier and preparation policy.

Edited: 2026-11-20T18:41:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Expose account-bound Arcane preparation and safe replay with independent rollout gating.

Edited: 2026-11-20T18:35:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs
  Summary: Route registered Arcane features and preserve Drop Merge session ownership.

Edited: 2026-11-20T18:35:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeController.cs
  Summary: Reuse Drop Merge package API contracts for legacy host traffic.

Edited: 2026-11-20T18:35:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeGameService.cs
  Summary: Run legacy Drop Merge sessions through package-owned transitions.

Edited: 2026-11-20T18:35:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PracticeActionReplayAudit.cs
  Summary: Recognize atomically recorded Drop Merge evidence.

Edited: 2026-11-20T18:36:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/RequestObservationMiddleware.cs
  Summary: Observe authenticated commands on registered worker routes.

Edited: 2026-11-20T18:37:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/RequestPayloadObservation.cs
  Summary: Classify Arcane preparations and retain approved feature request terms.

Edited: 2026-11-20T18:41:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Use the Neon-owned Car play classification.

Edited: 2026-11-20T18:36:20-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Register Drop Merge and shared puzzle persistence projects and tests.

Edited: 2026-11-20T18:42:04-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document independent worker routes and the Arcane prepared-round rollout and pause conditions.

Edited: 2026-11-20T18:39:15-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.preparedSpin.test.ts
  Summary: Verify browser retry keys, original wagers, server recovery, and older API compatibility.

Edited: 2026-11-20T18:41:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Save an account-scoped operation and retry the original Arcane paid request.

Edited: 2026-11-20T18:38:53-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Pass the current account for paid spin recovery.

Edited: 2026-11-20T18:43:41-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh engine guide sources after ownership changes.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/game-release.json
  Summary: Select the private packaged Arcane worker.

Edited: 2026-11-20T18:37:46-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureEndpoints.cs
  Summary: Register worker-owned Arcane feature routes and the prepared-spin route contract.

Edited: 2026-11-20T18:33:51-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureStore.cs
  Summary: Keep feature state locked while a durable spin is pending.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/FortuneForge.Games.ArcaneArchives.Server.csproj
  Summary: Reference the shared worker registration contract.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/GameWorkerModule.cs
  Summary: Compose Arcane feature routes and the existing signed paid outcome endpoint.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/game-release.json
  Summary: Select the stateful private Drop Merge worker.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/FortuneForge.Games.DropMerge.Server/DropMergeEndpoints.cs
  Summary: Expose the existing Drop Merge actions from its worker.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/FortuneForge.Games.DropMerge.Server/DropMergeFirestoreSessions.cs
  Summary: Persist package-owned Drop Merge sessions with atomic journal evidence.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/FortuneForge.Games.DropMerge.Server/FortuneForge.Games.DropMerge.Server.csproj
  Summary: Build the Drop Merge backend with shared puzzle persistence.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/GameComputeModule.cs
  Summary: Keep the existing compute contract compatible.

Edited: 2026-11-20T18:35:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/GameWorkerModule.cs
  Summary: Compose stateful Drop Merge routes and retain compute routes.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeAudit.cs
  Summary: Own Drop Merge evidence capture and completion validation.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeContracts.cs
  Summary: Own Drop Merge request, response, and error contracts.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeSession.cs
  Summary: Own the authoritative Drop Merge session reducer.

Edited: 2026-11-20T18:36:20-06:00 | Computer: VINNABOK | File: games/DropMerge/tests/FortuneForge.Games.DropMerge.Tests/DropMergeFirestoreSessionsTests.cs
  Summary: Verify cross-worker sessions, duplicate delivery, undo, and completion evidence.

Edited: 2026-11-20T18:36:20-06:00 | Computer: VINNABOK | File: games/DropMerge/tests/FortuneForge.Games.DropMerge.Tests/FortuneForge.Games.DropMerge.Tests.csproj
  Summary: Reference the package backend for Firestore regressions.

Edited: 2026-11-20T18:36:20-06:00 | Computer: VINNABOK | File: games/FortuneForge.Games.slnx
  Summary: Register standalone Drop Merge backend and tests.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonCarRespin.cs
  Summary: Reuse Neon-owned Car eligibility and frozen offer validation.

Edited: 2026-11-20T18:40:57-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonCarRoundState.cs
  Summary: Own Car play classification, persisted state, progression, and respin receipts.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonNightsEngine.cs
  Summary: Reuse the package Car play classification.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonNitroChoice.cs
  Summary: Own saved Nitro choice creation, identity, and replay validation.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonNitroWild.cs
  Summary: Use the same Car and Nitro choice rules in the engine.

Edited: 2026-11-20T18:39:03-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonOutcomeReplay.cs
  Summary: Reuse package Car rules when replaying frozen outcomes.

Edited: 2026-11-20T18:41:21-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPackageTests.cs
  Summary: Verify saved Car and Nitro outcomes, legacy receipt IDs, and round progress.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/FortuneForge.Games.TwentyFortyEight.Server.csproj
  Summary: Reference shared Firestore puzzle persistence.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/TwentyFortyEightEndpoints.cs
  Summary: Reuse shared infrastructure errors while retaining 2048 response contracts.

Edited: 2026-11-20T18:38:26-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/TwentyFortyEightFirestoreSessions.cs
  Summary: Adapt 2048 transitions to shared durable session storage.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightAudit.cs
  Summary: Use common puzzle audit identity while retaining evidence compatibility.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightSession.cs
  Summary: Reuse the shared session identity and version contract.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/PuzzleSessionIdentity.cs
  Summary: Define shared durable puzzle session and audit identity.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestorePuzzleSessions.cs
  Summary: Share atomic puzzle sessions, duplicate delivery protection, and paged journals.

Edited: 2026-11-20T18:33:57-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FortuneForge.Games.Firestore.csproj
  Summary: Build the shared Firestore persistence components.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerPublicRoute.cs
  Summary: Define trusted metadata for an existing route served by a private worker.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/Program.cs
  Summary: Apply account and observation checks to registered worker routes.

Edited: 2026-11-20T18:34:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/SlotSpinGuard.cs
  Summary: Add a shared marker for an unresolved durable paid spin.

Edited: 2026-11-20T18:34:31-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Recognize the packaged Arcane and Drop Merge workers.

Edited: 2026-11-20T22:45:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/JournalManifestFirestoreTests.cs
  Summary: Verify purge holds and operation tombstones for private Arcane spins.

Edited: 2026-11-20T22:39:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs
  Summary: Reuse platform slot history and statistics fields for legacy settlements.

Edited: 2026-11-20T22:37:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs
  Summary: Map the signed game wallet settlement and scoped account-state endpoints.

Edited: 2026-11-20T22:37:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Forward Arcane paid rounds and state to the worker while recovering known legacy keys.

Edited: 2026-11-20T22:45:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SecurityAuditRawRetention.cs
  Summary: Apply the existing raw retention policy to package-settled Arcane spins.

Edited: 2026-11-20T22:42:34-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include package-owned Arcane backend tests in the repository solution.

Edited: 2026-11-20T22:42:34-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Arcane worker cutover controls, wallet identity, and legacy recovery.

Edited: 2026-11-20T22:38:08-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneArchivesEngine.cs
  Summary: Expose validation of captured Arcane layouts for authoritative replay.

Edited: 2026-11-20T22:47:18-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcanePaidSlotOutcomes.cs
  Summary: Document the shared engine entry point for native and compatibility rounds.

Edited: 2026-11-20T22:42:34-06:00 | Computer: VINNABOK | File: games/FortuneForge.Games.slnx
  Summary: Include Arcane backend tests in the game solution.

Edited: 2026-11-20T22:38:47-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerAuthentication.cs
  Summary: Reuse signed request identity validation with distinct ingress and wallet scopes.

Edited: 2026-11-20T22:49:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletSettlementRpcTests.cs
  Summary: Exercise signed wallet settlement, idempotency, and scoped entitlement reads.

Edited: 2026-11-20T22:39:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/ArcanePaidWorkerGatewayTests.cs
  Summary: Verify exact legacy recovery signals and fail-closed worker forwarding.

Edited: 2026-11-20T22:45:55-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePlatformReportingTests.cs
  Summary: Verify native Arcane response, reporting, and historical outcome replay.

Edited: 2026-11-20T22:48:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletDocuments.cs
  Summary: Expose the existing scoped token inventory snapshot to the wallet store.

Edited: 2026-11-20T22:48:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs
  Summary: Settle signed game rounds idempotently and read scoped legacy entitlements.

Edited: 2026-11-20T22:39:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs
  Summary: Define scoped game wallet settlement and legacy entitlement lookup operations.

Edited: 2026-11-20T22:51:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletSettlementEndpoints.cs
  Summary: Authenticate worker-owned settlements and read only the signed account scope.

Edited: 2026-11-20T22:37:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/ArcanePaidWorkerGateway.cs
  Summary: Forward account-authenticated Arcane commands to their private worker.

Edited: 2026-11-20T22:44:54-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneFeatureStore.cs
  Summary: Coordinate Tome and potion state with outstanding Arcane rounds.

Edited: 2026-11-20T22:48:59-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcanePaidEndpoints.cs
  Summary: Serve Arcane spin and saved state from the private worker.

Edited: 2026-11-20T22:37:11-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcanePaidRouteContract.cs
  Summary: Define Arcane's public spin and state route contracts.

Edited: 2026-11-20T22:42:10-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundContracts.cs
  Summary: Define durable Arcane admission, wallet result, and read state.

Edited: 2026-11-20T22:40:12-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundService.cs
  Summary: Coordinate prepared Arcane outcomes, signed wallet calls, and exact retry.

Edited: 2026-11-20T22:45:34-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundStore.cs
  Summary: Own Arcane round admission, feature state, recovery, and evidence persistence.

Edited: 2026-11-20T22:42:58-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneSpinReport.cs
  Summary: Build canonical Arcane history, statistics, and audit evidence.

Edited: 2026-11-20T22:39:32-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneSpinResponse.cs
  Summary: Return the existing browser round contract from package-owned state.

Edited: 2026-11-20T22:38:36-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/GameWorkerModule.cs
  Summary: Register the Arcane authoritative round services and signed wallet client.

Edited: 2026-11-20T22:38:08-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/src/FortuneForge.Games.ArcaneArchives/ArcaneNativeReplay.cs
  Summary: Validate frozen native Arcane outcomes against historical math versions.

Edited: 2026-11-20T22:50:26-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/tests/FortuneForge.Games.ArcaneArchives.Tests/ArcaneRoundStoreTests.cs
  Summary: Cover Arcane feature progress and prepared round recovery in Firestore.

Edited: 2026-11-20T22:42:59-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/tests/FortuneForge.Games.ArcaneArchives.Tests/FortuneForge.Games.ArcaneArchives.Tests.csproj
  Summary: Add the independently runnable Arcane backend test project.

Edited: 2026-11-20T22:48:01-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletSettlement.cs
  Summary: Define game-facing wallet settlement, receipt, and scoped read contracts.

Edited: 2026-11-20T22:48:01-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletAuthentication.cs
  Summary: Sign exact game wallet actions with a separately scoped key.

Edited: 2026-11-20T22:49:59-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletClient.cs
  Summary: Call the private wallet and validate settlement and state responses.

Edited: 2026-11-20T22:39:32-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Contracts/SlotSpinResultFields.cs
  Summary: Share the existing platform slot history and statistics field mapping.

Edited: 2026-11-20T22:54:11-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs
  Summary: Register a signed worker transport that preserves deliberate recovery responses.

Edited: 2026-11-20T22:54:11-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/GameWorkerClient.cs
  Summary: Use the dedicated transport for authoritative commands with exact retries.

Edited: 2026-11-20T22:52:43-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh current engine source fingerprints and proof for all nineteen slot guides.

Edited: 2026-11-20T23:03:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestObservationTests.cs
  Summary: Verify that Neon action routes sign their game identity and selected reel.

Edited: 2026-11-20T23:01:33-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonAdmin.cs
  Summary: Fence legacy grants once Neon owns the player's state.

Edited: 2026-11-20T23:03:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonCarRespin.cs
  Summary: Keep historical receipt replay and prevent legacy writes after cutover.

Edited: 2026-11-20T23:01:33-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.NeonNitroChoice.cs
  Summary: Keep unresolved native Nitro choices with the Neon worker.

Edited: 2026-11-20T23:03:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Block new host admissions against worker-owned Neon state.

Edited: 2026-11-20T23:03:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Activity/FirestoreGameAnalyticsStore.cs
  Summary: Use the package-owned Car receipt source when aggregating paid adjustments.

Edited: 2026-11-20T23:03:36-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Route signed Neon spins, settlements, choices, and previews to their own worker.

Edited: 2026-11-20T22:57:53-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference Neon request and compatibility contracts from their server package.

Edited: 2026-11-20T23:03:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/RequestPayloadObservation.cs
  Summary: Record Neon Car, Nitro, and admin commands with their signed identifiers.

Edited: 2026-11-20T23:02:12-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Neon worker release, wallet scope, and cutover recovery.

Edited: 2026-11-20T22:58:41-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Reuse shared browser persistence for the exact Arcane spin attempt.

Edited: 2026-11-20T23:00:08-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/api.ts
  Summary: Resume the Neon player's original prepared spin through the authenticated gateway.

Edited: 2026-11-20T22:59:47-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/types.ts
  Summary: Describe signed current balance and recoverable paid round terms.

Edited: 2026-11-20T23:00:08-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/useNeonNights.ts
  Summary: Recover an interrupted paid spin without starting another wager.

Edited: 2026-11-20T23:01:49-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/api.test.tsx
  Summary: Verify prepared spin negotiation and exact retry by player.

Edited: 2026-11-20T23:06:00-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/controller.test.tsx
  Summary: Verify reload recovery uses current balance and keeps old play sequencing.

Edited: 2026-11-20T22:57:12-06:00 | Computer: VINNABOK | File: games/NeonNights/game-release.json
  Summary: Publish the Neon-specific stateful worker through its game release.

Edited: 2026-11-20T22:59:42-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/FortuneForge.Games.NeonNights.Server.csproj
  Summary: Enable the Neon worker to persist authoritative state with Firestore.

Edited: 2026-11-20T22:57:12-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/Program.cs
  Summary: Reuse the same compatibility outcome endpoint in both worker executables.

Edited: 2026-11-20T23:05:57-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/FortuneForge.Games.NeonNights.Tests.csproj
  Summary: Reference the Neon authoritative backend from its package tests.

Edited: 2026-11-20T22:57:12-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register Neon for independent packaged worker publishing.

Edited: 2026-11-20T22:59:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletSettlementRpcTests.cs
  Summary: Verify optional balance scope, exact legacy count, and authorized cash-plus-entitlement settlement.

Edited: 2026-11-20T23:08:09-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/NeonCarRespinWorkerTests.cs
  Summary: Verify durable Car adjustment, exact Nitro selection, and historical recovery.

Edited: 2026-11-20T22:57:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletDocuments.cs
  Summary: Read current cash cents from the canonical wallet document.

Edited: 2026-11-20T22:57:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs
  Summary: Settle cash with an exact legacy entitlement count in one transaction.

Edited: 2026-11-20T22:57:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs
  Summary: Define signed settlement count checks and immutable wallet receipts.

Edited: 2026-11-20T22:57:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletSettlementEndpoints.cs
  Summary: Gate optional signed wallet balances and legacy usage by service policy.

Edited: 2026-11-20T22:56:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/ArcanePaidWorkerGateway.cs
  Summary: Reuse exact forwarder behavior while preserving Arcane's historical retry.

Edited: 2026-11-20T22:56:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/SlotWorkerGateway.cs
  Summary: Forward typed slot commands and preserve authoritative worker responses.

Edited: 2026-11-20T22:57:08-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonCarRespinContracts.cs
  Summary: Define the Neon Car choice and respin wire shapes.

Edited: 2026-11-20T22:57:08-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonCarRespinEndpoints.cs
  Summary: Serve authenticated Car respin, dismissal, and nested Nitro routes.

Edited: 2026-11-20T23:03:33-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonCarRespinReport.cs
  Summary: Commit package-owned Car earnings, receipts, and audit evidence.

Edited: 2026-11-20T23:03:12-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonCarRespinStore.cs
  Summary: Freeze the chosen reel and adjustment before the signed payout.

Edited: 2026-11-20T22:57:12-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPaidSlotOutcomes.cs
  Summary: Own the historical paid outcome protocol for Neon in its package.

Edited: 2026-11-20T23:03:35-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryFeatures.cs
  Summary: Own native and carried Car state, Disco progression, and round balance.

Edited: 2026-11-20T23:02:21-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundActions.cs
  Summary: Recover saved Nitro choices and issue administrator preview rounds inside Neon.

Edited: 2026-11-20T23:02:21-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundEndpoints.cs
  Summary: Expose package-owned paid spin, state, Nitro, and preview actions.

Edited: 2026-11-20T23:04:47-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundModels.cs
  Summary: Define Neon round terms, current state, and exact public results.

Edited: 2026-11-20T23:00:20-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundService.cs
  Summary: Coordinate frozen game state with the signed shared wallet.

Edited: 2026-11-20T23:04:47-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundStore.cs
  Summary: Own durable admission, guard, reporting, and exact Neon settlement recovery.

Edited: 2026-11-20T23:03:34-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimarySpinReport.cs
  Summary: Persist canonical Neon account history and audit evidence after payment approval.

Edited: 2026-11-20T23:00:41-06:00 | Computer: VINNABOK | File: games/NeonNights/server/GameWorkerModule.cs
  Summary: Register Neon's package endpoints, store, random source, and wallet connection.

Edited: 2026-11-20T23:07:45-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPrimaryRoundTests.cs
  Summary: Cover native feature state and frozen Neon settlement in a local emulator.

Edited: 2026-11-20T22:57:05-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletSettlement.cs
  Summary: Add optional wallet cash observation and exact legacy entitlement assertions.

Edited: 2026-11-20T22:58:34-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletClient.cs
  Summary: Validate game-scoped cash and entitlement responses from the wallet.

Edited: 2026-11-20T22:58:41-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/preparedSpin.ts
  Summary: Keep a recoverable slot attempt scoped to its signed player and game.

Edited: 2026-11-20T23:03:03-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/NeonCarRespinReceipt.cs
  Summary: Remove the host's duplicate Car receipt collection definition.

Edited: 2026-11-20T23:14:40-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Neon Nitro's wallet reservation capability and pause behavior.

Edited: 2026-11-20T23:11:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs
  Summary: Persist immutable wager reservations and a separately replayable completion receipt.

Edited: 2026-11-20T23:10:18-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs
  Summary: Define phase-specific settlement terms for the internal wallet store.

Edited: 2026-11-20T23:10:18-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletSettlementEndpoints.cs
  Summary: Enable reservation phases only for configured games and signed operations.

Edited: 2026-11-20T23:11:11-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/DemoSlotsController.cs
  Summary: Delegate Neon practice rules to its package while preserving public audit and response contracts.

Edited: 2026-11-20T23:11:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletSettlementRpcTests.cs
  Summary: Verify signed reserve and payout rights through concurrent and interrupted deliveries.

Edited: 2026-11-20T23:11:53-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/NeonArcadeTests.cs
  Summary: Verify Neon uses its own demo progression with server-side pity.

Edited: 2026-11-20T23:14:03-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh slot guide evidence after moving Neon demo policy into its package.

Edited: 2026-11-20T23:10:18-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletSettlement.cs
  Summary: Define additive wallet reservation and payout phase identities.

Edited: 2026-11-20T23:10:18-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletClient.cs
  Summary: Validate a signed receipt for the specific reservation or completion phase.

Edited: 2026-11-20T23:10:21-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/api.ts
  Summary: Validate a Nitro reservation balance and any already selected reel.

Edited: 2026-11-20T23:10:21-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/types.ts
  Summary: Describe the optional reserved Nitro wager and selected reel.

Edited: 2026-11-20T23:10:44-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/useNeonNights.ts
  Summary: Show the reserved wager balance and reload the committed Nitro choice.

Edited: 2026-11-20T23:10:44-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/tests/controller.test.tsx
  Summary: Exercise precharged and reloaded Nitro choices without another debit.

Edited: 2026-11-20T23:10:40-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonDemoEngineRequest.cs
  Summary: Define the package demo engine request and its server-owned pity option.

Edited: 2026-11-20T23:10:40-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonDemoPolicy.cs
  Summary: Own Neon-only practice validation, selection, and progression in the game package.

Edited: 2026-11-20T23:10:21-06:00 | Computer: VINNABOK | File: games/NeonNights/src/FortuneForge.Games.NeonNights/NeonNitroChoice.cs
  Summary: Carry an optional authorized wager balance and saved choice in Nitro views.

Edited: 2026-11-20T23:12:36-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundActions.cs
  Summary: Persist the held Nitro offer and restore the selected reel across restarts.

Edited: 2026-11-20T23:12:36-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryFeatures.cs
  Summary: Reconcile precharged Car entitlements with the approved reservation.

Edited: 2026-11-20T23:09:23-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundEndpoints.cs
  Summary: Keep the private gateway response compatible with the held Nitro result.

Edited: 2026-11-20T23:10:55-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundService.cs
  Summary: Reserve the wager before showing Nitro and credit its chosen payout exactly once.

Edited: 2026-11-20T23:12:36-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundStore.cs
  Summary: Verify both payments against the frozen Nitro admission and retained evidence.

Edited: 2026-11-20T23:12:36-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimarySpinReport.cs
  Summary: Bind both reserved-wager and chosen-payout receipts to Neon evidence.

Edited: 2026-11-20T23:11:53-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonDemoPolicyTests.cs
  Summary: Cover Neon practice validation and saved mode rules.

Edited: 2026-11-20T23:11:24-06:00 | Computer: VINNABOK | File: games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/NeonPrimaryRoundTests.cs
  Summary: Exercise declined and interrupted Nitro holds plus the chosen payout.

Edited: 2026-11-20T23:17:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Prevent a legacy writer from starting a Jade spin against worker-owned state.

Edited: 2026-11-20T23:23:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Route owned Jade state reads away from archived game counters.

Edited: 2026-11-20T23:18:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/JadeDragonPresentationController.cs
  Summary: Serve both existing aliases from Jade's package presentation contract.

Edited: 2026-11-20T23:17:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Route authenticated Jade spins and recovery to its signed worker.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference Jade's game-specific server contracts from the gateway.

Edited: 2026-11-20T23:23:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Check a saved Jade V1 round using its package-owned replay evidence.

Edited: 2026-11-20T23:18:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Games/JadeDragon/JadeDragonWagerPolicy.cs
  Summary: Reuse Jade’s canonical paid wager cap in the legacy adapter.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Restore the legacy Jade audit from its explicit worker envelope.

Edited: 2026-11-20T23:20:20-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include the authoritative Jade backend test project.

Edited: 2026-11-20T23:19:44-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Jade's signed ownership cutover and retained free games.

Edited: 2026-11-20T23:18:50-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/JadeDragonRoute.tsx
  Summary: Serialize iframe initialization and paid spin recovery through the authenticated bridge.

Edited: 2026-11-20T23:19:25-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/jadeDragonApi.test.ts
  Summary: Verify exact player-bound replay after switching gateway capabilities.

Edited: 2026-11-20T23:23:41-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/jadeDragonApi.ts
  Summary: Persist Jade's original request and resume it after a lost reply.

Edited: 2026-11-20T23:25:15-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh engine contracts against the current Jade paid backend sources.

Edited: 2026-11-20T23:20:20-06:00 | Computer: VINNABOK | File: games/FortuneForge.Games.slnx
  Summary: Include Jade backend tests in the game solution.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: games/JadeDragon/game-release.json
  Summary: Select Jade’s package-owned private backend in its independent release.

Edited: 2026-11-20T23:23:41-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/FortuneForge.Games.JadeDragon.Server.csproj
  Summary: Wire Firestore, shared slot runtime, and internal tests for the Jade package.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/Program.cs
  Summary: Reuse the Jade-owned compatibility endpoint and preserve legacy audit data.

Edited: 2026-11-20T23:16:58-06:00 | Computer: VINNABOK | File: games/JadeDragon/src/FortuneForge.Games.JadeDragon/JadeDragonCascadeRules.cs
  Summary: Own the R100 paid wager cap in the Jade package.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Recognize the Jade packaged worker for standalone publishing.

Edited: 2026-11-20T23:18:02-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeDragonPresentation.cs
  Summary: Own Jade's public display rules and wagering metadata in its package.

Edited: 2026-11-20T23:19:28-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadePaidEndpoints.cs
  Summary: Serve Jade spin admission and saved state from authenticated package routes.

Edited: 2026-11-20T23:16:45-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadePaidSlotOutcomes.cs
  Summary: Keep distinct native and historical outcome contracts for old workers.

Edited: 2026-11-20T23:19:28-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeRoundModels.cs
  Summary: Define durable Jade terms, carry state, and approved settlement response.

Edited: 2026-11-20T23:22:08-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeRoundPolicy.cs
  Summary: Preserve distinct V1 and V2 wager, boost, and earned play rules.

Edited: 2026-11-20T23:17:09-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeRoundService.cs
  Summary: Settle Jade rounds through the scoped wallet and resume the original receipt.

Edited: 2026-11-20T23:19:31-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeRoundStateCodec.cs
  Summary: Import only Jade's old game counters and mark ownership atomically.

Edited: 2026-11-20T23:23:21-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeRoundStore.cs
  Summary: Freeze either versioned outcome before wallet settlement and finalize once.

Edited: 2026-11-20T23:23:16-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeSpinEvidence.cs
  Summary: Validate saved Jade outcomes, exact draws, and ledger amounts in their original version.

Edited: 2026-11-20T23:23:16-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeSpinReport.cs
  Summary: Publish canonical Jade history, analytics, and replay evidence after wallet approval.

Edited: 2026-11-20T23:22:31-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadeSpinResponse.cs
  Summary: Maintain the current iframe response for both Jade math versions.

Edited: 2026-11-20T23:17:37-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/GameWorkerModule.cs
  Summary: Run the Jade private backend with its own storage and signed wallet client.

Edited: 2026-11-20T23:16:58-06:00 | Computer: VINNABOK | File: games/JadeDragon/tests/FortuneForge.Games.JadeDragon.Tests/FortuneForge.Games.JadeDragon.Tests.csproj
  Summary: Build dedicated authoritative Jade package coverage.

Edited: 2026-11-20T23:19:03-06:00 | Computer: VINNABOK | File: games/JadeDragon/tests/FortuneForge.Games.JadeDragon.Tests/JadeRoundStateCodecTests.cs
  Summary: Verify versioned carry import and scope against the local emulator.

Edited: 2026-11-20T23:23:21-06:00 | Computer: VINNABOK | File: games/JadeDragon/tests/FortuneForge.Games.JadeDragon.Tests/JadeRoundStoreTests.cs
  Summary: Exercise interruption, old carry locks, and local credit settlement.

Edited: 2026-11-20T23:22:48-06:00 | Computer: VINNABOK | File: games/JadeDragon/tests/FortuneForge.Games.JadeDragon.Tests/JadeSpinReportingTests.cs
  Summary: Cover replay, tamper detection, and explicit legacy audit transport.

Edited: 2026-11-20T23:27:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Fence Gold Foundry’s retired admission path after worker ownership.

Edited: 2026-11-20T23:27:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Read owned Crucible progress through its worker.

Edited: 2026-11-20T23:27:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Forward signed paid spins, state, and recovery to Gold Foundry.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Use the published Gold Foundry package contracts at the gateway.

Edited: 2026-11-20T23:30:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Verify native Gold Foundry evidence alongside historical settlement audits.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Share the Gold Foundry package’s compatibility outcome identifier.

Edited: 2026-11-20T23:30:58-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document the Gold Foundry cutover, signed wallet scope, and pause behavior.

Edited: 2026-11-20T23:33:11-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh exported guides with current Foundry sources and compatibility adapter.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: games/GoldFoundry/game-release.json
  Summary: Select the standalone authoritative Foundry worker in its release.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/GameWorkerModule.cs
  Summary: Register Foundry’s own storage, authoritative routes, and signed wallet.

Edited: 2026-11-20T23:30:58-06:00 | Computer: VINNABOK | File: games/GoldFoundry/src/FortuneForge.Games.GoldFoundry/GoldFoundryCrucibleRules.cs
  Summary: Identify shared Crucible math after migrating paid state into its package.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Allow independent publishing of the packaged Gold Foundry worker.

Edited: 2026-11-20T23:26:19-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/FortuneForge.Games.GoldFoundry.Server.csproj
  Summary: Wire package-only Firestore persistence and test visibility.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/Program.cs
  Summary: Use one compatibility outcome handler for Gold Foundry.

Edited: 2026-11-20T23:26:57-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryPaidSlotOutcomes.cs
  Summary: Own the signed old-worker outcome adapter in the game package.

Edited: 2026-11-20T23:26:26-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryPaidEndpoints.cs
  Summary: Map package-owned paid spins and current Crucible state.

Edited: 2026-11-20T23:26:26-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryRoundModels.cs
  Summary: Define frozen Foundry terms, feature progress, and settlement state.

Edited: 2026-11-20T23:28:59-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryRoundPolicy.cs
  Summary: Apply Foundry wager and Crucible rules before deciding the cash credit.

Edited: 2026-11-20T23:26:26-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryRoundService.cs
  Summary: Resume prepared rounds and settle through the authenticated shared wallet.

Edited: 2026-11-20T23:26:19-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryRoundStateCodec.cs
  Summary: Import Foundry’s original charge and qualifying wages into owned state.

Edited: 2026-11-20T23:29:46-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryRoundStore.cs
  Summary: Freeze, settle, and report Gold Foundry rounds once across interrupted requests.

Edited: 2026-11-20T23:29:54-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundrySpinEvidence.cs
  Summary: Recompute Foundry draws, Crucible awards, and total paid cents.

Edited: 2026-11-20T23:29:54-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundrySpinReport.cs
  Summary: Write paid results and historical-format energy witnesses from the package.

Edited: 2026-11-20T23:27:35-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundrySpinResponse.cs
  Summary: Expose reel and Crucible results at their existing currency units.

Edited: 2026-11-20T23:26:19-06:00 | Computer: VINNABOK | File: games/GoldFoundry/tests/FortuneForge.Games.GoldFoundry.Tests/FortuneForge.Games.GoldFoundry.Tests.csproj
  Summary: Run Gold Foundry’s authoritative server tests inside its package.

Edited: 2026-11-20T23:26:48-06:00 | Computer: VINNABOK | File: games/GoldFoundry/tests/FortuneForge.Games.GoldFoundry.Tests/GoldFoundryRoundStateCodecTests.cs
  Summary: Verify scope and parity when importing historical Crucible charge.

Edited: 2026-11-20T23:31:37-06:00 | Computer: VINNABOK | File: games/GoldFoundry/tests/FortuneForge.Games.GoldFoundry.Tests/GoldFoundryRoundStoreTests.cs
  Summary: Verify engine-based payouts, prepared replay, and once-only Crucible witnesses.

Edited: 2026-11-20T23:31:48-06:00 | Computer: VINNABOK | File: games/GoldFoundry/tests/FortuneForge.Games.GoldFoundry.Tests/GoldFoundrySpinReportingTests.cs
  Summary: Verify exact-cent Crucible and settlement evidence reporting.

Edited: 2026-11-20T23:31:37-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/src/api.ts
  Summary: Recover the original Gold Foundry spin after lost responses.

Edited: 2026-11-20T23:28:41-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/src/types.ts
  Summary: Describe the current signed balance and saved operation for Gold Foundry.

Edited: 2026-11-20T23:29:06-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/src/useGoldFoundry.ts
  Summary: Restore a saved wager and balance without replaying the reel animation.

Edited: 2026-11-20T23:31:37-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/tests/api.test.ts
  Summary: Verify exact replay across an interrupted spin and balance refresh.

Edited: 2026-11-20T23:35:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Fence legacy Midnight Vault writers after the game takes ownership.

Edited: 2026-11-20T23:35:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Serve worker-owned Midnight Vault queues through their signed state route.

Edited: 2026-11-20T23:42:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/DemoSlotsController.cs
  Summary: Delegate Midnight Vault's nonfinancial demo rules and queue progression to the package.

Edited: 2026-11-20T23:35:47-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Route registered authoritative slot workers through the same signed gateway.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference Midnight Vault’s own server-side compatibility contracts.

Edited: 2026-11-20T23:42:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Use package-owned Midnight Vault evidence for current and historical rules.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs
  Summary: Reuse Midnight Vault’s signed compatibility outcome identifier.

Edited: 2026-11-20T23:38:00-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Midnight Vault’s independent cutover, wallet scope, and saved queue recovery.

Edited: 2026-11-20T23:45:24-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json
  Summary: Refresh slot guide evidence against current Midnight Vault sources.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: games/MidnightVault/game-release.json
  Summary: Publish the authoritative Midnight Vault worker through its game release.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/GameWorkerModule.cs
  Summary: Register Midnight Vault's round store, routes, randomness, and signed wallet.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/src/FortuneForge.Games.MidnightVault/MidnightVaultBonusRounds.cs
  Summary: Use the package's canonical order and seal identifiers for migration.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/src/FortuneForge.Games.MidnightVault/MidnightVaultSlotDefinitions.cs
  Summary: Define the canonical 25-cent unit in Midnight Vault.

Edited: 2026-11-20T23:42:41-06:00 | Computer: VINNABOK | File: games/MidnightVault/src/FortuneForge.Games.MidnightVault/MidnightVaultDemoPolicy.cs
  Summary: Own practice-round admission and queue progression in Midnight Vault.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Allow independent packaging of Midnight Vault’s private worker.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/FortuneForge.Games.MidnightVault.Server.csproj
  Summary: Wire the Midnight Vault server package for Firestore and dedicated tests.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/Program.cs
  Summary: Reuse one Midnight Vault compatibility endpoint for both worker entry points.

Edited: 2026-11-20T23:35:36-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultPaidEndpoints.cs
  Summary: Serve authenticated Midnight Vault spins and saved state.

Edited: 2026-11-20T23:35:18-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultPaidSlotOutcomes.cs
  Summary: Own the legacy compute worker contract in the Midnight Vault package.

Edited: 2026-11-20T23:37:07-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultRoundModels.cs
  Summary: Define versioned Midnight Vault round requests, outcomes, and queued state.

Edited: 2026-11-20T23:35:36-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultRoundPolicy.cs
  Summary: Apply the saved wager, queue head, model, and progression to each round.

Edited: 2026-11-20T23:34:50-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultRoundService.cs
  Summary: Settle or resume an identical Midnight Vault spin through the signed wallet.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultRoundStateCodec.cs
  Summary: Import exact old queue, points, energy, pity, and counts under one owner.

Edited: 2026-11-20T23:35:23-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultRoundStore.cs
  Summary: Freeze before payment and finalize Midnight Vault queue and reporting once.

Edited: 2026-11-20T23:39:22-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultSpinEvidence.cs
  Summary: Verify versioned Midnight Vault payouts and legacy queue ownership independently.

Edited: 2026-11-20T23:39:52-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultSpinReport.cs
  Summary: Write canonical history, game-unit witnesses, activity, and exact paid evidence.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultSpinResponse.cs
  Summary: Return live and queued bonus state with the established wire schema.

Edited: 2026-11-20T23:35:01-06:00 | Computer: VINNABOK | File: games/MidnightVault/tests/FortuneForge.Games.MidnightVault.Tests/FortuneForge.Games.MidnightVault.Tests.csproj
  Summary: Build the game-owned Midnight Vault backend tests.

Edited: 2026-11-20T23:36:13-06:00 | Computer: VINNABOK | File: games/MidnightVault/tests/FortuneForge.Games.MidnightVault.Tests/MidnightVaultRoundStateCodecTests.cs
  Summary: Verify scoped historical import and cutoff of the signed legacy queue prefix.

Edited: 2026-11-20T23:39:20-06:00 | Computer: VINNABOK | File: games/MidnightVault/tests/FortuneForge.Games.MidnightVault.Tests/MidnightVaultRoundStoreTests.cs
  Summary: Verify old-to-native queue transition, boosts, declines, and exact retry.

Edited: 2026-11-20T23:40:51-06:00 | Computer: VINNABOK | File: games/MidnightVault/tests/FortuneForge.Games.MidnightVault.Tests/MidnightVaultSpinReportingTests.cs
  Summary: Verify archive and monetary accounting for the three historical payout models.

Edited: 2026-11-20T23:37:02-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/api.ts
  Summary: Retry Midnight Vault with the same account, terms, and operation.

Edited: 2026-11-20T23:38:00-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/roundState.ts
  Summary: Present expanded rows for saved composite legacy modes.

Edited: 2026-11-20T23:37:02-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/types.ts
  Summary: Expose the signed current balance and prepared spin to the game UI.

Edited: 2026-11-20T23:43:40-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/useMidnightVault.ts
  Summary: Restore saved bonus order, result, and balance before the next spin.

Edited: 2026-11-20T23:38:21-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/tests/api.test.ts
  Summary: Verify exact browser retry for a legacy odd-wager round and its successor.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Bind recovery requests to the expected account and forward paid Wukong and Rainbow Realm rounds to their signed workers.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Fence the legacy writer after Wukong or Rainbow Realm adopts an account.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Prevent legacy state reads from presenting stale progress for migrated accounts.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.cs
  Summary: Use the Wukong package constant for its historical storage scope.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.Serialization.cs
  Summary: Resolve the legacy storage scope consistently within the trusted host.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletDocuments.cs
  Summary: Resolve Wukong's historical entitlement from the authenticated game scope.

Edited: 2026-11-20T23:48:16-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletSettlementRpcTests.cs
  Summary: Test signed historical Wukong entitlement lookup and settlement with actor and game isolation.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the package-owned Wukong and Rainbow Realm server libraries.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include Wukong and Rainbow Realm backend test projects.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Dispatch native Wukong and Rainbow Realm settlement evidence to the package replayers.

Edited: 2026-11-20T23:46:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Bonuses/EnergyBonus.cs
  Summary: Adapt host replay to the shared slot energy reducer.

Edited: 2026-11-20T23:57:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Bonuses/SlotSpecialRoundProfiles.cs
  Summary: Adapt historical practice progression to the shared classic slot runtime.

Edited: 2026-11-20T23:46:23-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEnergy.cs
  Summary: Implement common energy progress, per-match rounding, and reset semantics.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEngine.cs
  Summary: Support explicitly authorized carried free-spin wagers while preserving normal paid validation.

Edited: 2026-11-20T23:57:17-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotModels.cs
  Summary: Expose classic track identifiers and preserve the earned-wager fallback.

Edited: 2026-11-20T23:57:08-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotPracticeCollections.cs
  Summary: Own the rounded-average collection progression used by public demos and archived practice replay.

Edited: 2026-11-20T23:52:39-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotReplayEvidence.cs
  Summary: Share the immutable classic slot evidence format and bounded random draw codec.

Edited: 2026-11-20T23:52:57-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/FortuneForge.Games.Slots.Persistence.csproj
  Summary: Provide a neutral Firestore library for classic slot compatibility state.

Edited: 2026-11-20T23:53:29-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotRoundStateCodec.cs
  Summary: Share validated import, persistence, and transition comparisons for the classic merged bonus bank.

Edited: 2026-11-20T23:47:05-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/preparedSpin.ts
  Summary: Share account-bound recovery and caller operation headers.

Edited: 2026-11-20T23:58:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Bind state to the caller, negotiate separate per-game recovery stores, and verify the exact returned spin.

Edited: 2026-11-20T23:50:31-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.preparedSpin.test.ts
  Summary: Verify account-bound Wukong recovery of a saved odd wager through dropped replies.

Edited: 2026-11-20T23:58:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Resume saved rounds without a second optimistic debit and refresh current player progress after replay.

Edited: 2026-11-20T23:48:56-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/jadeDragonApi.ts
  Summary: Bind paid state and original-round retries to the Jade Dragon account.

Edited: 2026-11-20T23:48:56-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/api.ts
  Summary: Bind paid state and original-round retries to the Neon Nights account.

Edited: 2026-11-20T23:48:56-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/src/api.ts
  Summary: Bind paid state and original-round retries to the Gold Foundry account.

Edited: 2026-11-20T23:48:56-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/api.ts
  Summary: Bind paid state and original-round retries to the Midnight Vault account.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: games/Wukong/src/FortuneForge.Games.Wukong/WukongEngine.cs
  Summary: Own the historical scope alias and validate stored odd wagers for free rounds only.

Edited: 2026-11-20T23:51:53-06:00 | Computer: VINNABOK | File: games/RainbowRealm/src/FortuneForge.Games.RainbowRealm/RainbowRealmEngine.cs
  Summary: Own Rainbow Realm wager validation and permit exact saved free-spin wagers.

Edited: 2026-11-20T23:54:45-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Add dedicated Wukong and Rainbow Realm packaged worker targets.

Edited: 2026-11-20T23:57:17-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document per-game cutover, outstanding admission reconciliation, and recovery pause settings.

Edited: 2026-11-20T23:46:40-06:00 | Computer: VINNABOK | File: games/Wukong/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-20T23:46:40-06:00 | Computer: VINNABOK | File: games/Wukong/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-20T23:46:40-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-20T23:53:29-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/FortuneForge.Games.Wukong.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: games/Wukong/tests/FortuneForge.Games.Wukong.Tests/FortuneForge.Games.Wukong.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-20T23:46:40-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-20T23:46:50-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-20T23:46:50-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-20T23:53:29-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-20T23:46:50-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-20T23:47:22-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-20T23:52:39-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-20T23:52:39-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-20T23:52:39-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-20T23:47:08-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-20T23:50:16-06:00 | Computer: VINNABOK | File: games/Wukong/tests/FortuneForge.Games.Wukong.Tests/WukongRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-20T23:48:53-06:00 | Computer: VINNABOK | File: games/Wukong/tests/FortuneForge.Games.Wukong.Tests/WukongRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-20T23:50:02-06:00 | Computer: VINNABOK | File: games/Wukong/tests/FortuneForge.Games.Wukong.Tests/WukongSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-20T23:54:45-06:00 | Computer: VINNABOK | File: games/RainbowRealm/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-20T23:54:45-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-20T23:54:45-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-20T23:53:29-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/FortuneForge.Games.RainbowRealm.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-20T23:51:53-06:00 | Computer: VINNABOK | File: games/RainbowRealm/tests/FortuneForge.Games.RainbowRealm.Tests/FortuneForge.Games.RainbowRealm.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-20T23:54:45-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-20T23:55:15-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-20T23:55:51-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-20T23:51:53-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-20T23:53:29-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-20T23:51:53-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-20T23:54:00-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-20T23:53:02-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-20T23:53:22-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-20T23:53:41-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-20T23:53:37-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-20T23:57:06-06:00 | Computer: VINNABOK | File: games/RainbowRealm/tests/FortuneForge.Games.RainbowRealm.Tests/RainbowRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-20T23:54:01-06:00 | Computer: VINNABOK | File: games/RainbowRealm/tests/FortuneForge.Games.RainbowRealm.Tests/RainbowRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-20T23:55:16-06:00 | Computer: VINNABOK | File: games/RainbowRealm/tests/FortuneForge.Games.RainbowRealm.Tests/RainbowSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:06:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Use one registry for authenticated prepared slot worker routes.

Edited: 2026-11-21T00:09:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs
  Summary: Reject legacy writers once a package has adopted the player, including Pirates' Fortune.

Edited: 2026-11-21T00:07:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs
  Summary: Share the worker ownership fence with legacy slot state reads.

Edited: 2026-11-21T00:05:04-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Replay native Pirates proof and delegate legacy gem awards to the same game reducer.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the package-owned Pirates' Fortune server.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include the Pirates' Fortune package regression suite.

Edited: 2026-11-21T00:03:01-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Pirates' Fortune's signed worker, migration terms, and recovery pause.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register the Pirates' Fortune dedicated packaged worker release.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Use an account-scoped exact retry store for Pirates' Fortune.

Edited: 2026-11-21T00:06:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Register public paid worker routes and game-owned legacy markers in one place.

Edited: 2026-11-21T00:05:04-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotStatistics.cs
  Summary: Delegate the legacy Pirates' Fortune gem award to the game-owned reducer.

Edited: 2026-11-21T00:08:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Verify package adoption prevents legacy reads and admissions while preserving other game scopes.

Edited: 2026-11-21T00:04:10-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx
  Summary: Verify a paid round with a lost reply recovers at zero credits and refreshes the balance.

Edited: 2026-11-21T00:06:02-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/FortuneForge.Games.Slots.Persistence.csproj
  Summary: Reference neutral wallet and observation contracts for paid round storage.

Edited: 2026-11-21T00:09:07-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotPaidRoundStore.cs
  Summary: Share durable prepared rounds, receipt validation, and wallet retry while keeping per-game rules and serialization.

Edited: 2026-11-21T00:01:37-06:00 | Computer: VINNABOK | File: games/PiratesFortune/src/FortuneForge.Games.PiratesFortune/PiratesFortuneEngine.cs
  Summary: Authorize saved odd classic bonus wagers independently of cash wagers.

Edited: 2026-11-21T00:04:02-06:00 | Computer: VINNABOK | File: games/PiratesFortune/src/FortuneForge.Games.PiratesFortune/PiratesGemBonus.cs
  Summary: Own the exact per-spin gem grants, combined modes, and weighted earned wager.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: games/PiratesFortune/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-21T00:02:01-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-21T00:01:04-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-21T00:01:37-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/FortuneForge.Games.PiratesFortune.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-21T00:01:37-06:00 | Computer: VINNABOK | File: games/PiratesFortune/tests/FortuneForge.Games.PiratesFortune.Tests/FortuneForge.Games.PiratesFortune.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-21T00:01:04-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-21T00:04:02-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-21T00:09:07-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-21T00:04:02-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-21T00:01:44-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-21T00:03:54-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-21T00:01:44-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-21T00:01:44-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-21T00:05:23-06:00 | Computer: VINNABOK | File: games/PiratesFortune/tests/FortuneForge.Games.PiratesFortune.Tests/PiratesRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-21T00:03:39-06:00 | Computer: VINNABOK | File: games/PiratesFortune/tests/FortuneForge.Games.PiratesFortune.Tests/PiratesRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-21T00:03:28-06:00 | Computer: VINNABOK | File: games/PiratesFortune/tests/FortuneForge.Games.PiratesFortune.Tests/PiratesSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundService.cs
  Summary: Delegate wallet workflow to neutral paid round storage.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundStore.cs
  Summary: Bind neutral lifecycle hooks to the existing scope, archive, and serial types.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundStateCodec.cs
  Summary: Reuse the prepared lifecycle's exact existing scope and compatibility layout.

Edited: 2026-11-21T00:09:07-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongRoundModels.cs
  Summary: Expose round identity and timestamp to the store without changing JSON.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundService.cs
  Summary: Delegate wallet workflow to neutral paid round storage.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundStore.cs
  Summary: Bind neutral lifecycle hooks to the existing scope, archive, and serial types.

Edited: 2026-11-21T00:07:58-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundStateCodec.cs
  Summary: Reuse the prepared lifecycle's exact existing scope and compatibility layout.

Edited: 2026-11-21T00:09:07-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowRoundModels.cs
  Summary: Expose round identity and timestamp to the store without changing JSON.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Require an original operation on the prepared-spin route and forward its exact signed path.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Register Olympus worker scope, owner guard, and the shared prepared-spin path.

Edited: 2026-11-21T00:15:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/RequestPayloadObservation.cs
  Summary: Record the new prepared-spin route with the game scope from its request.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Delegate native Olympus classic and tumble evidence to the game package.

Edited: 2026-11-21T00:15:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestObservationTests.cs
  Summary: Verify the prepared-spin route participates in durable request observation.

Edited: 2026-11-21T00:15:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Verify Olympus ownership closes legacy reads and admissions.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the Olympus game server package.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Add Olympus package backend tests to the solution.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Olympus cutover, stored feature compatibility, and prepared-path rollout.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register the dedicated Gods of Olympus packaged worker.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerPublicRoute.cs
  Summary: Map both exact audited paths to an opted-in prepared slot handler.

Edited: 2026-11-21T00:14:34-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/preparedSpin.ts
  Summary: Export the strict recovery path shared by prepared slot clients.

Edited: 2026-11-21T00:16:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Use a worker-only retry path, recover the saved terms, and defer incompatible concurrent operations until state refresh.

Edited: 2026-11-21T00:16:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.preparedSpin.test.ts
  Summary: Verify saved operations survive older gateways and cross-tab free-mode conflicts.

Edited: 2026-11-21T00:16:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Refresh the current game state after a different prepared round is discovered.

Edited: 2026-11-21T00:14:53-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/jadeDragonApi.ts
  Summary: Submit keyed Jade rounds through the worker-only prepared route.

Edited: 2026-11-21T00:14:34-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/src/FortuneForge.Games.GodsOfOlympus/GodsOfOlympusEngine.cs
  Summary: Expose the package's authoritative wager rules for current and saved classic games.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/NeonNights/server/FortuneForge.Games.NeonNights.Server/NeonPrimaryRoundEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:14:53-06:00 | Computer: VINNABOK | File: games/NeonNights/client/FortuneForge.Games.NeonNights.Client/src/api.ts
  Summary: Submit keyed rounds through the worker-only route and retain ordinary legacy play.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/JadeDragon/server/FortuneForge.Games.JadeDragon.Server/JadePaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/GoldFoundry/server/FortuneForge.Games.GoldFoundry.Server/GoldFoundryPaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:14:53-06:00 | Computer: VINNABOK | File: games/GoldFoundry/client/FortuneForge.Games.GoldFoundry.Client/src/api.ts
  Summary: Submit keyed rounds through the worker-only route and retain ordinary legacy play.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/MidnightVault/server/FortuneForge.Games.MidnightVault.Server/MidnightVaultPaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:14:53-06:00 | Computer: VINNABOK | File: games/MidnightVault/client/FortuneForge.Games.MidnightVault.Client/src/api.ts
  Summary: Submit keyed rounds through the worker-only route and retain ordinary legacy play.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/Wukong/server/FortuneForge.Games.Wukong.Server/WukongPaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/RainbowRealm/server/FortuneForge.Games.RainbowRealm.Server/RainbowPaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/PiratesFortune/server/FortuneForge.Games.PiratesFortune.Server/PiratesPaidEndpoints.cs
  Summary: Register the dedicated prepared-spin path without altering existing handlers.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-21T00:14:34-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/FortuneForge.Games.GodsOfOlympus.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-21T00:14:34-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/tests/FortuneForge.Games.GodsOfOlympus.Tests/FortuneForge.Games.GodsOfOlympus.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-21T00:15:23-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-21T00:20:38-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-21T00:17:39-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-21T00:15:08-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-21T00:14:34-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-21T00:15:08-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-21T00:14:57-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-21T00:14:48-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-21T00:16:02-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-21T00:16:28-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-21T00:15:15-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/server/FortuneForge.Games.GodsOfOlympus.Server/OlympusSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-21T00:16:44-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/tests/FortuneForge.Games.GodsOfOlympus.Tests/OlympusRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-21T00:17:52-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/tests/FortuneForge.Games.GodsOfOlympus.Tests/OlympusRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-21T00:17:50-06:00 | Computer: VINNABOK | File: games/GodsOfOlympus/tests/FortuneForge.Games.GodsOfOlympus.Tests/OlympusSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Register signed Reel Riches routes and the active worker owner field.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Dispatch native Reel Riches outcomes to package replay.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Check the legacy fence for an adopted Reel Riches account.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the game-owned Reel Riches server.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include Reel Riches package tests.

Edited: 2026-11-21T00:24:54-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document the Reel Riches worker cutover and recovery controls.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register Reel Riches as a dedicated packaged worker release.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Use scoped authenticated retry for Reel Riches.

Edited: 2026-11-21T00:24:16-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotReportWriter.cs
  Summary: Centralize standard classic result, history, analytics, and audit persistence from game projections.

Edited: 2026-11-21T00:23:47-06:00 | Computer: VINNABOK | File: games/ReelRiches/src/FortuneForge.Games.ReelRiches/ReelRichesEngine.cs
  Summary: Validate earned odd bonus wagers without changing cash wager steps.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: games/ReelRiches/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-21T00:23:47-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/FortuneForge.Games.ReelRiches.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-21T00:23:47-06:00 | Computer: VINNABOK | File: games/ReelRiches/tests/FortuneForge.Games.ReelRiches.Tests/FortuneForge.Games.ReelRiches.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-21T00:24:32-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-21T00:24:17-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-21T00:24:17-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-21T00:24:17-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-21T00:23:47-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-21T00:24:17-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-21T00:24:49-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-21T00:24:37-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-21T00:24:37-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-21T00:26:40-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-21T00:24:37-06:00 | Computer: VINNABOK | File: games/ReelRiches/server/FortuneForge.Games.ReelRiches.Server/ReelRichesSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-21T00:26:09-06:00 | Computer: VINNABOK | File: games/ReelRiches/tests/FortuneForge.Games.ReelRiches.Tests/ReelRichesRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-21T00:24:22-06:00 | Computer: VINNABOK | File: games/ReelRiches/tests/FortuneForge.Games.ReelRiches.Tests/ReelRichesRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-21T00:25:56-06:00 | Computer: VINNABOK | File: games/ReelRiches/tests/FortuneForge.Games.ReelRiches.Tests/ReelRichesSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Register High Noon Fortune's scoped worker routes and legacy owner mark.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Delegate native High Noon outcomes to its archived game proof.

Edited: 2026-11-21T00:34:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs
  Summary: Honor the frozen meter setting when replaying old slot payouts.

Edited: 2026-11-21T00:34:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/SlotRoundAuditTests.cs
  Summary: Replay a paid High Noon archive with dormant legacy energy and reject an inflated payout.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Verify adopted High Noon accounts remain closed to legacy readers and new legacy starts.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the package-owned High Noon Fortune server.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include the High Noon Fortune package backend tests.

Edited: 2026-11-21T00:31:01-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Describe High Noon worker cutover, wallet scope and exact recovery settings.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register the dedicated High Noon Fortune packaged worker.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Use authenticated prepared recovery for High Noon Fortune.

Edited: 2026-11-21T00:31:05-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotRoundProgression.cs
  Summary: Advance a classic scatter-only bank while preserving dormant historical tracks.

Edited: 2026-11-21T00:30:08-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/src/FortuneForge.Games.HighNoonFortune/HighNoonFortuneEngine.cs
  Summary: Validate exact odd bonus wagers separately from cash steps.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-21T00:30:08-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/FortuneForge.Games.HighNoonFortune.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-21T00:30:08-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/tests/FortuneForge.Games.HighNoonFortune.Tests/FortuneForge.Games.HighNoonFortune.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-21T00:30:17-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-21T00:30:11-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-21T00:31:05-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-21T00:30:11-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-21T00:30:08-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-21T00:30:11-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-21T00:31:05-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-21T00:30:59-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-21T00:30:59-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-21T00:30:59-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-21T00:30:59-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/server/FortuneForge.Games.HighNoonFortune.Server/HighNoonSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-21T00:32:09-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/tests/FortuneForge.Games.HighNoonFortune.Tests/HighNoonRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-21T00:30:43-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/tests/FortuneForge.Games.HighNoonFortune.Tests/HighNoonRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-21T00:31:55-06:00 | Computer: VINNABOK | File: games/HighNoonFortune/tests/FortuneForge.Games.HighNoonFortune.Tests/HighNoonSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Register the signed Royal Draw routes and active worker owner marker.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Dispatch Royal Draw native payouts and feature history to its game replayer.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Verify adopted Royal Draw accounts stay fenced from legacy reads and starts.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the game-owned Royal Draw API and persistence layer.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include Royal Draw package regression tests.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Royal Draw worker, signed wallet scope and recovery cutover.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register the dedicated packaged Royal Draw release.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Use authenticated prepared-spin recovery for Royal Draw.

Edited: 2026-11-21T00:36:29-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotRoundProgression.cs
  Summary: Share standard classic carry, energy settlement and collection advancement without game-specific parameters.

Edited: 2026-11-21T00:36:01-06:00 | Computer: VINNABOK | File: games/RoyalDraw/src/FortuneForge.Games.RoyalDraw/RoyalDrawEngine.cs
  Summary: Preserve classic cash increments and permit verified odd carried wagers.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: games/RoyalDraw/game-release.json
  Summary: Select the game's dedicated paid worker release.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/GameWorkerModule.cs
  Summary: Wire the signed package worker, game state services, and scoped wallet client.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/Program.cs
  Summary: Retain the signed legacy outcome route during game worker cutover.

Edited: 2026-11-21T00:36:01-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/FortuneForge.Games.RoyalDraw.Server.csproj
  Summary: Define the dedicated game worker's project dependencies.

Edited: 2026-11-21T00:36:01-06:00 | Computer: VINNABOK | File: games/RoyalDraw/tests/FortuneForge.Games.RoyalDraw.Tests/FortuneForge.Games.RoyalDraw.Tests.csproj
  Summary: Define the game package backend regression test project.

Edited: 2026-11-21T00:36:23-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawPaidSlotOutcomes.cs
  Summary: Resolve old signed spin admissions with the package engine.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawPaidEndpoints.cs
  Summary: Expose the signed paid spin and state routes with original-operation recovery.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawRoundModels.cs
  Summary: Define the game-owned progress, prepared round, and error contracts.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawRoundStore.cs
  Summary: Prepare and finalize immutable game-local Firestore rounds and safely import legacy state.

Edited: 2026-11-21T00:36:01-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawRoundStateCodec.cs
  Summary: Map game-owned saved carry and progress into the validated classic compatibility format.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawRoundService.cs
  Summary: Settle frozen rounds through the scoped wallet and safely retry original receipts.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawRoundPolicy.cs
  Summary: Apply exact game-specific wager locks, bonus progression, and outcome checks.

Edited: 2026-11-21T00:36:25-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawSpinDraws.cs
  Summary: Record and replay the exact engine draw sequence.

Edited: 2026-11-21T00:36:32-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawSpinEvidence.cs
  Summary: Verify immutable RNG, game progression, and signed wallet witness evidence.

Edited: 2026-11-21T00:36:25-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawSpinReport.cs
  Summary: Write exact settlement, history, analytics, and archive witnesses once.

Edited: 2026-11-21T00:36:25-06:00 | Computer: VINNABOK | File: games/RoyalDraw/server/FortuneForge.Games.RoyalDraw.Server/RoyalDrawSpinResponse.cs
  Summary: Project the settled game-owned outcome onto the established client contract.

Edited: 2026-11-21T00:37:10-06:00 | Computer: VINNABOK | File: games/RoyalDraw/tests/FortuneForge.Games.RoyalDraw.Tests/RoyalDrawRoundStoreTests.cs
  Summary: Verify saved rounds, wallet loss recovery, and inheritance against the Firestore emulator.

Edited: 2026-11-21T00:36:38-06:00 | Computer: VINNABOK | File: games/RoyalDraw/tests/FortuneForge.Games.RoyalDraw.Tests/RoyalDrawRoundStateCodecTests.cs
  Summary: Verify saved bonus, storage scope, wager compatibility, and invalid-state handling.

Edited: 2026-11-21T00:37:03-06:00 | Computer: VINNABOK | File: games/RoyalDraw/tests/FortuneForge.Games.RoyalDraw.Tests/RoyalDrawSpinReportingTests.cs
  Summary: Verify exact payout witness, draw replay, and evidence tampering.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/FortuneForge.Games.SamuraiFortune.Server.csproj — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/Program.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortunePaidEndpoints.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortunePaidSlotOutcomes.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundModels.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundPolicy.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundService.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundStateCodec.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundStore.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinDraws.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinEvidence.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinReport.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinResponse.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/server/GameWorkerModule.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/src/FortuneForge.Games.SamuraiFortune/SamuraiFortuneEngine.cs — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.

Edited: 2026-11-21T00:39:41-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/game-release.json — Added Samurai-owned durable paid rounds, legacy import, signed wallet settlement and replay.
Edited: 2026-11-21T00:40:18-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoContracts.cs — Add game-owned Sic Bo paid backend contracts and historical outcome replay.
Edited: 2026-11-21T00:40:18-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoOutcomeAudit.cs — Add game-owned Sic Bo paid backend contracts and historical outcome replay.
Edited: 2026-11-21T00:40:18-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/FortuneForge.Games.SicBo.Server.csproj — Add game-owned Sic Bo paid backend contracts and historical outcome replay.
Edited: 2026-11-21T00:40:18-06:00 | Computer: Vinnabok | File: games/SicBo/game-release.json — Add game-owned Sic Bo paid backend contracts and historical outcome replay.
Edited: 2026-11-21T00:41:02-06:00 | Computer: VINNABOK | File: games/Asteroids/src/FortuneForge.Games.Asteroids/AsteroidsReplay.cs — Move authoritative Asteroids replay evaluation and verification to the game package.
Edited: 2026-11-21T00:41:02-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Asteroids/AsteroidsReplayCore.cs — Move authoritative Asteroids replay evaluation and verification to the game package.
Edited: 2026-11-21T00:41:02-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsReplayEvaluatorTests.cs — Move authoritative Asteroids replay evaluation and verification to the game package.
Edited: 2026-11-21T00:41:02-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/FortuneForge.Games.Asteroids.Tests.csproj — Move authoritative Asteroids replay evaluation and verification to the game package.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/FortuneForge.Games.CandyCarnival.Server.csproj — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalPaidEndpoints.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalPaidSlotOutcomes.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalRoundModels.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalRoundPolicy.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalRoundService.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalRoundStateCodec.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalRoundStore.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalSpinDraws.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalSpinEvidence.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalSpinReport.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/CandyCarnivalSpinResponse.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/FortuneForge.Games.CandyCarnival.Server/Program.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/server/GameWorkerModule.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/src/FortuneForge.Games.CandyCarnival/CandyCarnivalEngine.cs — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:06-06:00 | Computer: Vinnabok | File: games/CandyCarnival/game-release.json — Added Candy-owned paid rounds, scatter carry, signed wallet, legacy import and replay.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratModels.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/PracticeBaccaratStore.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratOutcomeAudit.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/FortuneForge.Games.Baccarat.Server.csproj — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratFirestoreStore.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratEndpoints.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/server/GameWorkerModule.cs — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:08-06:00 | Computer: Vinnabok | File: games/Baccarat/game-release.json — Added package-owned Baccarat admission, saved-deal recovery, signed wallet settlement, legacy replay and worker routes.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoWalletStore.cs — Persist Sic Bo outcomes before signed wallet settlement and recover exact retries.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoEndpoints.cs — Persist Sic Bo outcomes before signed wallet settlement and recover exact retries.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/SicBo/server/GameWorkerModule.cs — Persist Sic Bo outcomes before signed wallet settlement and recover exact retries.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/DragonTigerFirestoreStore.cs — Move paid Dragon Tiger to recoverable package intents and the signed wallet.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/DragonTigerWalletStore.cs — Move paid Dragon Tiger to recoverable package intents and the signed wallet.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/DragonTigerEndpoints.cs — Move paid Dragon Tiger to recoverable package intents and the signed wallet.
Edited: 2026-11-21T00:41:44-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/GameWorkerModule.cs — Move paid Dragon Tiger to recoverable package intents and the signed wallet.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/FortuneForge.Games.PhantomManor.Server.csproj — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorPaidEndpoints.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorPaidSlotOutcomes.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorRoundModels.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorRoundPolicy.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorRoundService.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorRoundStateCodec.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorRoundStore.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorSpinDraws.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorSpinEvidence.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorSpinReport.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/PhantomManorSpinResponse.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/FortuneForge.Games.PhantomManor.Server/Program.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/server/GameWorkerModule.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/src/FortuneForge.Games.PhantomManor/PhantomManorEngine.cs — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:41:59-06:00 | Computer: Vinnabok | File: games/PhantomManor/game-release.json — Added Phantom-owned paid rounds, paw scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/FortuneForge.Games.NordicLegends.Server.csproj — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsPaidEndpoints.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsPaidSlotOutcomes.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsRoundModels.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsRoundPolicy.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsRoundService.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsRoundStateCodec.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsRoundStore.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsSpinDraws.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsSpinEvidence.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsSpinReport.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/NordicLegendsSpinResponse.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/FortuneForge.Games.NordicLegends.Server/Program.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/server/GameWorkerModule.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/src/FortuneForge.Games.NordicLegends/NordicLegendsEngine.cs — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.

Edited: 2026-11-21T00:42:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/game-release.json — Added Nordic-owned paid rounds, sync-paw scatter carry and direct-value replay, signed wallet and legacy import.
Edited: 2026-11-21T00:42:30-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/tests/FortuneForge.Games.TwentyFortyEight.Tests/TwentyFortyEightFirestoreSessionsTests.cs — Verify lost-response start/reset/undo receipts across replicas and later actions.
Edited: 2026-11-21T00:42:30-06:00 | Computer: Vinnabok | File: games/DropMerge/tests/FortuneForge.Games.DropMerge.Tests/DropMergeFirestoreSessionsTests.cs — Verify lost-response start/reset/undo receipts across replicas and later actions.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsRunAudit.cs — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsFreeRunStore.cs — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/FortuneForge.Games.Asteroids.Server.csproj — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Asteroids/FirestoreAsteroidsFreeRunService.cs — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Asteroids/AsteroidsReplayEvaluatorTests.cs — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.
Edited: 2026-11-21T00:42:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Asteroids/ArcadeCompetitionAsteroidsPaidEntryServiceTests.cs — Extract Asteroids free-run lifecycle and compose the existing transactional journal through callbacks.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FlappyFreeRunStore.cs — Move durable authenticated free-run lifecycle and replay verification into the game backend; preserve historical storage and retry identity.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Flappy/FirestoreFlappyFreeRunService.cs — Delegate legacy free-run operations to package-owned lifecycle with existing shared audit writer.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: games/HorseFlight/src/FortuneForge.Games.HorseFlight/HorseFlightRunContracts.cs — Own recorded free-run request/result/error contracts and lifecycle errors in Horse Flight.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightFreeRunStore.cs — Move authoritative recorded free-run persistence, legacy rules replay and exact retries into game backend; balance comes from generic account reader.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/HorseFlight/HorseFlightController.cs — Keep legacy HTTP authentication and feature gate while delegating Horse Flight durable state to its package.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/HorseFlight/HorseFlightFirestoreStore.cs — Adapt existing Horse Flight host to package lifecycle while preserving the historical account balance display.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FortuneForge.Games.Flappy.Server.csproj — Create game-specific durable backend library without a host dependency.

Edited: 2026-11-21T00:42:51-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/FortuneForge.Games.HorseFlight.Server.csproj — Create game-specific durable backend library without a host dependency.
Edited: 2026-11-21T00:42:56-06:00 | Computer: Vinnabok | File: games/CandyCarnival/tests/FortuneForge.Games.CandyCarnival.Tests/FortuneForge.Games.CandyCarnival.Tests.csproj — Add Candy Carnival native lifecycle, legacy compatibility, reporting and replay verification.
Edited: 2026-11-21T00:42:56-06:00 | Computer: Vinnabok | File: games/CandyCarnival/tests/FortuneForge.Games.CandyCarnival.Tests/CandyCarnivalRoundStateCodecTests.cs — Add Candy Carnival native lifecycle, legacy compatibility, reporting and replay verification.
Edited: 2026-11-21T00:42:56-06:00 | Computer: Vinnabok | File: games/CandyCarnival/tests/FortuneForge.Games.CandyCarnival.Tests/CandyCarnivalRoundStoreTests.cs — Add Candy Carnival native lifecycle, legacy compatibility, reporting and replay verification.
Edited: 2026-11-21T00:42:56-06:00 | Computer: Vinnabok | File: games/CandyCarnival/tests/FortuneForge.Games.CandyCarnival.Tests/CandyCarnivalSpinReportingTests.cs — Add Candy Carnival native lifecycle, legacy compatibility, reporting and replay verification.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/server/FortuneForge.Games.Snake.Server/FirestoreSnakeGameService.cs — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/src/FortuneForge.Games.Snake/SnakeReplayAudit.cs — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/server/FortuneForge.Games.Snake.Server/FortuneForge.Games.Snake.Server.csproj — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/server/FortuneForge.Games.Snake.Server/SnakeEndpoints.cs — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/server/GameWorkerModule.cs — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:42:57-06:00 | Computer: Vinnabok | File: games/Snake/game-release.json — Move free Snake state, recovery, replay and signed request receipts into its game package.
Edited: 2026-11-21T00:43:18-06:00 | Computer: Vinnabok | File: games/SicBo/tests/FortuneForge.Games.SicBo.Server.Tests/FortuneForge.Games.SicBo.Server.Tests.csproj — Add emulator-backed Sic Bo paid round verification.
Edited: 2026-11-21T00:43:18-06:00 | Computer: Vinnabok | File: games/SicBo/tests/FortuneForge.Games.SicBo.Server.Tests/SicBoEmulator.cs — Add emulator-backed Sic Bo paid round verification.
Edited: 2026-11-21T00:43:15-06:00 | Computer: Vinnabok | File: games\PhantomManor\tests\FortuneForge.Games.PhantomManor.Tests\FortuneForge.Games.PhantomManor.Tests.csproj — Add package verification for owned paid rounds, legacy state, replay and history.
Edited: 2026-11-21T00:43:15-06:00 | Computer: Vinnabok | File: games\PhantomManor\tests\FortuneForge.Games.PhantomManor.Tests\PhantomManorRoundStateCodecTests.cs — Add package verification for owned paid rounds, legacy state, replay and history.
Edited: 2026-11-21T00:43:15-06:00 | Computer: Vinnabok | File: games\PhantomManor\tests\FortuneForge.Games.PhantomManor.Tests\PhantomManorRoundStoreTests.cs — Add package verification for owned paid rounds, legacy state, replay and history.
Edited: 2026-11-21T00:43:15-06:00 | Computer: Vinnabok | File: games\PhantomManor\tests\FortuneForge.Games.PhantomManor.Tests\PhantomManorSpinReportingTests.cs — Add package verification for owned paid rounds, legacy state, replay and history.
Edited: 2026-11-21T00:43:25-06:00 | Computer: Vinnabok | File: games/SicBo/tests/FortuneForge.Games.SicBo.Server.Tests/PaidRoundTests.cs — Verify lost wallet reply, concurrent retries, paused admission, history and legacy stakes.
Edited: 2026-11-21T00:43:31-06:00 | Computer: Vinnabok | File: games/HiLo/server/FortuneForge.Games.HiLo.Server/HiLoFirestoreStore.cs — Replace direct wallet writes with recoverable signed-wallet admission and settlement while preserving legacy journals.
Edited: 2026-11-21T00:43:31-06:00 | Computer: Vinnabok | File: games/HiLo/server/FortuneForge.Games.HiLo.Server/HiLoEndpoints.cs — Resolve signed wallet services for paid requests.
Edited: 2026-11-21T00:43:31-06:00 | Computer: Vinnabok | File: games/HiLo/server/GameWorkerModule.cs — Register game-scoped signed wallet client in Hi-Lo worker.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/FortuneForge.Games.DesertTreasures.Server.csproj — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/Program.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresPaidEndpoints.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresPaidSlotOutcomes.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresRoundModels.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresRoundPolicy.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresRoundService.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresRoundStateCodec.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresRoundStore.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresSpinDraws.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresSpinEvidence.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresSpinReport.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/FortuneForge.Games.DesertTreasures.Server/DesertTreasuresSpinResponse.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/server/GameWorkerModule.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/src/FortuneForge.Games.DesertTreasures/DesertTreasuresEngine.cs — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:43:44-06:00 | Computer: Vinnabok | File: games/DesertTreasures/game-release.json — Added Desert-owned durable paid rounds, energy and weighted collections, signed wallet, legacy import and replay.
Edited: 2026-11-21T00:43:48-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/HouseHoldem/HouseHoldemModels.cs — Move authoritative House Hold'em contracts and service into the game backend package.
Edited: 2026-11-21T00:43:48-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/HouseHoldem/HouseHoldemService.cs — Move authoritative House Hold'em contracts and service into the game backend package.
Edited: 2026-11-21T00:43:53-06:00 | Computer: Vinnabok | File: games/Baccarat/tests/FortuneForge.Games.Baccarat.Tests/FortuneForge.Games.Baccarat.Tests.csproj — Verified durable paid deal retry, concurrent settlement, decline, historical V1 replay and corruption rejection.
Edited: 2026-11-21T00:43:48-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemModels.cs — Move authoritative House Hold'em contracts and service into the game backend package.

Edited: 2026-11-21T00:43:59-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/FortuneForge.Games.SamuraiFortune.Tests.csproj — Added Samurai paid lifecycle, legacy, collection and replay tests.

Edited: 2026-11-21T00:43:59-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiPolicyTests.cs — Added Samurai paid lifecycle, legacy, collection and replay tests.

Edited: 2026-11-21T00:43:59-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiRoundStoreTests.cs — Added Samurai paid lifecycle, legacy, collection and replay tests.

Edited: 2026-11-21T00:43:59-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiTestSupport.cs — Added Samurai paid lifecycle, legacy, collection and replay tests.
Edited: 2026-11-21T00:43:53-06:00 | Computer: Vinnabok | File: games/Baccarat/tests/FortuneForge.Games.Baccarat.Tests/BaccaratWalletTests.cs — Verified durable paid deal retry, concurrent settlement, decline, historical V1 replay and corruption rejection.
Edited: 2026-11-21T00:43:48-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemService.cs — Move authoritative House Hold'em contracts and service into the game backend package.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/FortuneForge.Games.RobotRevolution.Server.csproj — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidEndpoints.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidSlotOutcomes.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundModels.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundPolicy.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundService.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStateCodec.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStore.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinDraws.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinEvidence.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinReport.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinResponse.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/Program.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/GameWorkerModule.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.

Edited: 2026-11-21T00:44:15-06:00 | Computer: Vinnabok | File: games/RobotRevolution/game-release.json — Added Robot-owned durable paid rounds, rows scatter carry, signed wallet, legacy import and replay.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloCodec.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloContracts.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloEndpoints.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloService.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloStore.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/FortuneForge.Games.Blackjack.Server.csproj — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.
Edited: 2026-11-21T00:44:48-06:00 | Computer: Vinnabok | File: games/Blackjack/server/GameWorkerModule.cs — Add package-owned solo Blackjack frozen-command lifecycle, signed-wallet recovery and historical codec.

Edited: 2026-11-21T00:44:59-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/server/FortuneForge.Games.FreeSolitaire.Server/FortuneForge.Games.FreeSolitaire.Server.csproj — Extract free Solitaire run lifecycle and shared public contracts into its game backend.

Edited: 2026-11-21T00:44:59-06:00 | Computer: Vinnabok | File: games/Solitaire/src/FortuneForge.Games.Solitaire/SolitaireFreeRunContracts.cs — Extract free Solitaire run lifecycle and shared public contracts into its game backend.

Edited: 2026-11-21T00:44:59-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/server/FortuneForge.Games.FreeSolitaire.Server/FirestoreSolitaireFreeRunService.cs — Extract free Solitaire run lifecycle and shared public contracts into its game backend.

Edited: 2026-11-21T00:44:59-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreSolitaireFreeRunService.cs — Extract free Solitaire run lifecycle and shared public contracts into its game backend.

Edited: 2026-11-21T00:44:59-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/SolitaireController.cs — Extract free Solitaire run lifecycle and shared public contracts into its game backend.
Edited: 2026-11-21T00:45:08-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/FortuneForge.Games.HouseHoldem.Server.csproj — Add game-owned durable House Hold'em preparation, historical replay and immutable command receipts.
Edited: 2026-11-21T00:45:08-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemRoundCodec.cs — Add game-owned durable House Hold'em preparation, historical replay and immutable command receipts.
Edited: 2026-11-21T00:45:08-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemPreparedCommand.cs — Add game-owned durable House Hold'em preparation, historical replay and immutable command receipts.
Edited: 2026-11-21T00:45:08-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemPreparedStore.cs — Add game-owned durable House Hold'em preparation, historical replay and immutable command receipts.

Edited: 2026-11-21T00:45:16-06:00 | Computer: Vinnabok | File: games/NordicLegends/tests/FortuneForge.Games.NordicLegends.Tests/FortuneForge.Games.NordicLegends.Tests.csproj — Added Nordic paid-round durable retry, legacy bank, pause/cooldown and one-time settlement coverage.

Edited: 2026-11-21T00:45:16-06:00 | Computer: Vinnabok | File: games/NordicLegends/tests/FortuneForge.Games.NordicLegends.Tests/NordicLegendsTestSupport.cs — Added Nordic paid-round durable retry, legacy bank, pause/cooldown and one-time settlement coverage.

Edited: 2026-11-21T00:45:16-06:00 | Computer: Vinnabok | File: games/NordicLegends/tests/FortuneForge.Games.NordicLegends.Tests/NordicLegendsRoundStoreTests.cs — Added Nordic paid-round durable retry, legacy bank, pause/cooldown and one-time settlement coverage.
Edited: 2026-11-21T00:45:30-06:00 | Computer: Vinnabok | File: games/HiLo/tests/FortuneForge.Games.HiLo.Server.Tests/FirestoreTests.cs — Verify signed-wallet reserve/complete recovery, exact retry, history, concurrency, declines and legacy compatibility.
Edited: 2026-11-21T00:46:01-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemFirestoreStore.cs — Coordinate signed wallet effects and package-owned history, audit and recovery.
Edited: 2026-11-21T00:46:01-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemRoundReporting.cs — Coordinate signed wallet effects and package-owned history, audit and recovery.

Edited: 2026-11-21T00:46:28-06:00 | Computer: Vinnabok | File: games/NordicLegends/tests/FortuneForge.Games.NordicLegends.Tests/NordicLegendsPolicyTests.cs — Added exact historical state, authored direct value and scatter rules, signed reporting and replay tamper tests.

Edited: 2026-11-21T00:46:37-06:00 | Computer: Vinnabok | File: games/Mines/server/FortuneForge.Games.Mines.Server/FirestoreMinesStore.cs — Replace direct wallet writes with durable Mines command preparation and signed reservation completion.
Edited: 2026-11-21T00:46:37-06:00 | Computer: Vinnabok | File: games/Mines/server/FortuneForge.Games.Mines.Server/MinesService.cs — Replace direct wallet writes with durable Mines command preparation and signed reservation completion.
Edited: 2026-11-21T00:46:37-06:00 | Computer: Vinnabok | File: games/Mines/server/FortuneForge.Games.Mines.Server/MinesEndpoints.cs — Replace direct wallet writes with durable Mines command preparation and signed reservation completion.
Edited: 2026-11-21T00:46:37-06:00 | Computer: Vinnabok | File: games/Mines/server/FortuneForge.Games.Mines.Server/FortuneForge.Games.Mines.Server.csproj — Replace direct wallet writes with durable Mines command preparation and signed reservation completion.
Edited: 2026-11-21T00:47:01-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsFreeRunEndpoints.cs — Add authenticated recovery and legacy-compatible Asteroids replay evidence verification.
Edited: 2026-11-21T00:47:01-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsReplayEvidenceTests.cs — Add authenticated recovery and legacy-compatible Asteroids replay evidence verification.
Edited: 2026-11-21T00:47:01-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsFreeRunStoreTests.cs — Add authenticated recovery and legacy-compatible Asteroids replay evidence verification.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/server/FortuneForge.Games.FreeSolitaire.Server/FreeSolitaireEndpoints.cs — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/server/GameWorkerModule.cs — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/game-release.json — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/tests/FortuneForge.Games.FreeSolitaire.Tests/FortuneForge.Games.FreeSolitaire.Tests.csproj — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/tests/FortuneForge.Games.FreeSolitaire.Tests/FreeSolitaireRunServiceTests.cs — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Solitaire/FirestoreSolitaireFreeRunServiceTests.cs — Add independently hosted free Solitaire routes and move durable lifecycle verification into the game package.

Edited: 2026-11-21T00:47:04-06:00 | Computer: Vinnabok | File: games/Flappy/tests/FortuneForge.Games.Flappy.Tests/FortuneForge.Games.Flappy.Tests.csproj — Add standalone game backend verification project independent of host build.

Edited: 2026-11-21T00:47:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/FortuneForge.Games.HorseFlight.Tests.csproj — Add standalone game backend verification project independent of host build.

Edited: 2026-11-21T00:47:04-06:00 | Computer: Vinnabok | File: games/Flappy/tests/FortuneForge.Games.Flappy.Tests/FlappyFreeRunStoreTests.cs — Verify historical replay bytes, cross-replica lost-response recovery, one-time lifecycle, identity boundaries and corrupt outcome rejection.

Edited: 2026-11-21T00:47:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/HorseFlightFreeRunStoreTests.cs — Verify cross-replica start/completion recovery, stable balance receipt, no wallet writes, all legacy rules, and rejected invalid or corrupt outcomes.
Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemEndpoints.cs — Route House Hold'em lifecycle through its package and signed generic wallet; remove host persistence ownership.
Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/GameWorkerModule.cs — Route House Hold'em lifecycle through its package and signed generic wallet; remove host persistence ownership.
Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: games/HouseHoldem/game-release.json — Route House Hold'em lifecycle through its package and signed generic wallet; remove host persistence ownership.
Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/HouseHoldem/HouseHoldemFirestoreStore.cs — Route House Hold'em lifecycle through its package and signed generic wallet; remove host persistence ownership.
Edited: 2026-11-21T00:47:02-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/HouseHoldem/HouseHoldemController.cs — Route House Hold'em lifecycle through its package and signed generic wallet; remove host persistence ownership.
Edited: 2026-11-21T00:47:19-06:00 | Computer: Vinnabok | File: games/DragonTiger/tests/FortuneForge.Games.DragonTiger.Tests/DragonTigerFirestoreTests.cs — Signed wallet ownership with durable intent, replay, history, and historical compatibility.
Edited: 2026-11-21T00:47:19-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/AndarBaharFirestoreStore.cs — Signed wallet ownership with durable intent, replay, history, and historical compatibility.
Edited: 2026-11-21T00:47:19-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/AndarBaharWalletStore.cs — Signed wallet ownership with durable intent, replay, history, and historical compatibility.
Edited: 2026-11-21T00:47:19-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/AndarBaharEndpoints.cs — Signed wallet ownership with durable intent, replay, history, and historical compatibility.
Edited: 2026-11-21T00:47:19-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/GameWorkerModule.cs — Signed wallet ownership with durable intent, replay, history, and historical compatibility.
Edited: 2026-11-21T00:47:13-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/tests/FortuneForge.Games.TwentyFortyEight.Tests/TwentyFortyEightReplayTests.cs — Verify independent terminal action replay, legacy missing inputs, and tampered score/queue rejection.
Edited: 2026-11-21T00:47:13-06:00 | Computer: Vinnabok | File: games/DropMerge/tests/FortuneForge.Games.DropMerge.Tests/DropMergeReplayTests.cs — Verify independent terminal action replay, legacy missing inputs, and tampered score/queue rejection.
Edited: 2026-11-21T00:47:35-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackSoloRecoveryTests.cs — Verify signed-wallet lost-response recovery, immutable retries, declines and legacy paid hands with Firestore emulator.
Edited: 2026-11-21T00:47:35-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/FortuneForge.Games.Blackjack.Tests.csproj — Verify signed-wallet lost-response recovery, immutable retries, declines and legacy paid hands with Firestore emulator.
Edited: 2026-11-21T00:48:03-06:00 | Computer: Vinnabok | File: games/Mines/tests/FortuneForge.Games.Mines.Server.Tests/MinesTestWallet.cs — Verify Mines through a separate transactional wallet boundary.
Edited: 2026-11-21T00:48:03-06:00 | Computer: Vinnabok | File: games/Mines/tests/FortuneForge.Games.Mines.Server.Tests/MinesFirestoreTests.cs — Verify Mines through a separate transactional wallet boundary.

Edited: 2026-11-21T00:48:38-06:00 | Computer: Vinnabok | File: games/Flappy/src/FortuneForge.Games.Flappy/FlappyReplayEvaluator.cs — Expose authoritative bounded replay validation for game backend and legacy HTTP admission.

Edited: 2026-11-21T00:48:38-06:00 | Computer: Vinnabok | File: games/HorseFlight/src/FortuneForge.Games.HorseFlight/HorseFlightReplayEvaluator.cs — Expose authoritative bounded replay validation for game backend and legacy HTTP admission.

Edited: 2026-11-21T00:48:38-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FlappyFreeRunEndpoints.cs — Own authenticated Flappy free-run HTTP lifecycle at preserved URLs for private workers.

Edited: 2026-11-21T00:48:38-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightFreeRunEndpoints.cs — Own both preserved Horse Flight free-run URL families and game-specific feature gate/error contracts in backend package.
Edited: 2026-11-21T00:48:47-06:00 | Computer: Vinnabok | File: games/Mines/tests/FortuneForge.Games.Mines.Server.Tests/MinesWalletRecoveryTests.cs — Verify reserve and payout lost-reply recovery, privacy, pause and exact retries.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerModels.cs — Moved Video Poker HTTP contracts, wager policy and application service into the game package.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerRoundCodec.cs — Retained legacy single-hand and multi-hand round readers and state serialization in the game package.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/FortuneForge.Games.VideoPoker.Server.csproj — Added the dedicated signed-wallet Video Poker backend package.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerPreparedOperation.cs — Added frozen game-owned deal and draw settlement terms.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerRoundRecording.cs — Recorded game-owned audit, analytics and participation evidence after wallet confirmation.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerFirestoreStore.cs — Replaced account writes with durable prepared operations, signed wallet settlement, exact retries and interrupted-round recovery.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerEndpoints.cs — Exposed authenticated game-owned paid routes, history and paused admission retry support.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/GameWorkerModule.cs — Added the packaged Video Poker worker and retained old compute routes for deployment compatibility.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: games/VideoPoker/game-release.json — Selected the complete packaged game backend.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/VideoPoker/VideoPokerModels.cs — Removed host-owned contracts and service after moving the authoritative source into the game package.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/VideoPoker/VideoPokerFirestoreStore.cs — Removed host-owned paid round and account mutation implementation.
Edited: 2026-11-21T00:49:17-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/VideoPoker/VideoPokerController.cs — Kept practice support and fail-closed paid route compatibility behind generic worker forwarding.

Edited: 2026-11-21T00:49:36-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/server/FortuneForge.Games.FreeSolitaire.Server/FreeSolitaireSeeds.cs — Centralize nonzero deal seeds and verify signed legacy routes, lost-response recovery and exactly-once completion.

Edited: 2026-11-21T00:49:36-06:00 | Computer: Vinnabok | File: games/FreeSolitaire/tests/FortuneForge.Games.FreeSolitaire.Tests/FreeSolitaireEndpointTests.cs — Centralize nonzero deal seeds and verify signed legacy routes, lost-response recovery and exactly-once completion.
Edited: 2026-11-21T00:49:50-06:00 | Computer: Vinnabok | File: games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/AndarBaharFirestoreTests.cs — Verify signed settlement, restart, pause, history, fractional payouts, and legacy R100 recovery.
Edited: 2026-11-21T00:49:50-06:00 | Computer: Vinnabok | File: games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/AndarBaharWalletStoreTests.cs — Verify signed settlement, restart, pause, history, fractional payouts, and legacy R100 recovery.
Edited: 2026-11-21T00:49:56-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoServerContracts.cs — Move Keno backend contracts and versioned outcome auditing into the game package.
Edited: 2026-11-21T00:49:56-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoOutcomeAudit.cs — Move Keno backend contracts and versioned outcome auditing into the game package.
Edited: 2026-11-21T00:49:56-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/FortuneForge.Games.Keno.Server.csproj — Move Keno backend contracts and versioned outcome auditing into the game package.
Edited: 2026-11-21T00:49:56-06:00 | Computer: Vinnabok | File: games/Keno/game-release.json — Move Keno backend contracts and versioned outcome auditing into the game package.
Edited: 2026-11-21T00:50:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStateCodecTests.cs — Verify Robot Revolution rows feature, lifecycle recovery, legacy compatibility and replay.
Edited: 2026-11-21T00:50:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs — Verify Robot Revolution rows feature, lifecycle recovery, legacy compatibility and replay.
Edited: 2026-11-21T00:50:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionSpinReportingTests.cs — Verify Robot Revolution rows feature, lifecycle recovery, legacy compatibility and replay.
Edited: 2026-11-21T00:50:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FortuneForge.Games.RobotRevolution.Tests.csproj — Verify Robot Revolution rows feature, lifecycle recovery, legacy compatibility and replay.
Edited: 2026-11-21T00:50:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/HouseHoldem/HouseHoldemFirestoreTests.cs — Move paid House Hold'em lifecycle verification into the game package.
Edited: 2026-11-21T00:50:05-06:00 | Computer: Vinnabok | File: games/HouseHoldem/tests/FortuneForge.Games.HouseHoldem.Tests/FortuneForge.Games.HouseHoldem.Tests.csproj — Move paid House Hold'em lifecycle verification into the game package.
Edited: 2026-11-21T00:50:31-06:00 | Computer: VINNABOK | File: games/Asteroids/server/GameWorkerModule.cs — Compose private Asteroids free-run backend with shared transactional audit persistence.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/FortuneForge.Games.Roulette.Server.csproj — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteContracts.cs — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteManagedPlayers.cs — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteGameService.cs — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RoulettePopulation.cs — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteAudit.cs — Moved Roulette table lifecycle and replay checks into its package behind narrow population and recording boundaries.
Edited: 2026-11-21T00:50:44-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoEndpoints.cs — Own Keno routes, immutable draws, signed wallet settlement and recovery in the game backend.
Edited: 2026-11-21T00:50:44-06:00 | Computer: Vinnabok | File: games/Keno/server/GameWorkerModule.cs — Own Keno routes, immutable draws, signed wallet settlement and recovery in the game backend.
Edited: 2026-11-21T00:50:44-06:00 | Computer: Vinnabok | File: games/Keno/tests/FortuneForge.Games.Keno.Server.Tests/FortuneForge.Games.Keno.Server.Tests.csproj — Own Keno routes, immutable draws, signed wallet settlement and recovery in the game backend.
Edited: 2026-11-21T00:50:44-06:00 | Computer: Vinnabok | File: games/Keno/tests/FortuneForge.Games.Keno.Server.Tests/KenoEmulator.cs — Own Keno routes, immutable draws, signed wallet settlement and recovery in the game backend.
Edited: 2026-11-21T00:50:44-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoWalletStore.cs — Own Keno routes, immutable draws, signed wallet settlement and recovery in the game backend.
Edited: 2026-11-21T00:50:50-06:00 | Computer: Vinnabok | File: games/Keno/tests/FortuneForge.Games.Keno.Server.Tests/PaidRoundTests.cs — Verify immutable Keno draw, wallet loss recovery and v1/v2/v3 historical paytables.

Edited: 2026-11-21T00:50:52-06:00 | Computer: Vinnabok | File: games/DesertTreasures/tests/FortuneForge.Games.DesertTreasures.Tests/DesertPolicyTests.cs — Added Desert paid lifecycle, weighted collection, active energy/collector and replay tests.

Edited: 2026-11-21T00:50:52-06:00 | Computer: Vinnabok | File: games/DesertTreasures/tests/FortuneForge.Games.DesertTreasures.Tests/DesertRoundStoreTests.cs — Added Desert paid lifecycle, weighted collection, active energy/collector and replay tests.

Edited: 2026-11-21T00:50:52-06:00 | Computer: Vinnabok | File: games/DesertTreasures/tests/FortuneForge.Games.DesertTreasures.Tests/DesertTestSupport.cs — Added Desert paid lifecycle, weighted collection, active energy/collector and replay tests.

Edited: 2026-11-21T00:50:52-06:00 | Computer: Vinnabok | File: games/DesertTreasures/tests/FortuneForge.Games.DesertTreasures.Tests/FortuneForge.Games.DesertTreasures.Tests.csproj — Added Desert paid lifecycle, weighted collection, active energy/collector and replay tests.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/src/contracts.ts — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/src/contracts.ts — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:51:01-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LegacyLiarsDiceGameService.cs — Move legacy memory-match ownership into the game package while preserving existing sessions and evidence.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/src/httpTwentyFortyEightGateway.ts — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/src/httpDropMergeGateway.ts — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/src/TwentyFortyEightGame.tsx — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:50:57-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/src/DropMergeGame.tsx — Add saved-board read/recovery contract and make retry recover instead of reset.
Edited: 2026-11-21T00:51:13-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LiarsDiceContracts.cs — Own compatible public Liar’s Dice contracts in its game package.
Edited: 2026-11-21T00:51:13-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LiarsDiceManagedActionPolicy.cs — Move deterministic managed-player decisions to the game package.

Edited: 2026-11-21T00:51:14-06:00 | Computer: Vinnabok | File: games/Flappy/server/GameWorkerModule.cs — Compose private authenticated game-owned free-run backend from shared audit and wallet dependencies.

Edited: 2026-11-21T00:51:14-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/GameWorkerModule.cs — Compose private authenticated game-owned free-run backend from shared audit and wallet dependencies.
Edited: 2026-11-21T00:51:33-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/LiarsDice/LiarsDiceGameService.cs — Delegate historical memory-only matches to package-owned legacy lifecycle.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/LiarsDice/LiarsDiceContracts.cs — Keep host contract aliases over the authoritative game package schema.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/LiarsDice/LiarsDiceController.cs — Use package-owned match responses for the legacy authenticated adapter.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/LiarsDice/LiarsDiceManagedPlayers.cs — Keep generic bot leases behind the game-owned roster contract and legacy scheduler.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/LiarsDice/LiarsDiceManagedActionPolicy.cs — Delegate legacy managed-player choices to the package policy.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/LiarsDice/LiarsDiceGameServiceTests.cs — Keep legacy tests bound to the canonical managed-player contract.
Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/SecurityAudit/LiarsDiceEvidenceReplayAuditTests.cs — Keep legacy tests bound to the canonical managed-player contract.

Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/cards/solitaire/solitaireFreeRunApi.ts — Add validated owner-scoped recovery reads for free Solitaire and rejection tests.

Edited: 2026-11-21T00:51:34-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/cards/solitaire/solitaireFreeRunApi.test.ts — Add validated owner-scoped recovery reads for free Solitaire and rejection tests.
Edited: 2026-11-21T00:51:49-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LegacyLiarsDiceEvidenceReplayAudit.cs — Own independent replay of historical saved Liar’s Dice journal evidence in the game package.
Edited: 2026-11-21T00:51:49-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/LiarsDiceEvidenceReplayAudit.cs — Delegate historical journal replay to the owning game package.
Edited: 2026-11-21T00:51:49-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/FortuneForge.Games.LiarsDice.Server/LiarsDiceGameService.cs — Own durable free matches, private replay, signed receipts, recovery and managed-player lifecycle.
Edited: 2026-11-21T00:51:57-06:00 | Computer: Vinnabok | File: games/DragonTiger/INTEGRATION.md — Document packaged paid ownership, signed wallet configuration, and legacy recovery.
Edited: 2026-11-21T00:51:57-06:00 | Computer: Vinnabok | File: games/AndarBahar/INTEGRATION.md — Document packaged paid ownership, signed wallet configuration, and legacy recovery.
Edited: 2026-11-21T00:52:28-06:00 | Computer: Vinnabok | File: games/HouseHoldem/tests/FortuneForge.Games.HouseHoldem.Tests/HouseHoldemRoundTests.cs — Verify concurrent retries, lost wallet replies, exact history, atomic admission and historical R500 hands.
Edited: 2026-11-21T00:52:49-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/FirestoreRouletteService.cs — Added durable Roulette checkpoints, command receipts, private history, worker routes and package maintenance.
Edited: 2026-11-21T00:52:49-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteEndpoints.cs — Added durable Roulette checkpoints, command receipts, private history, worker routes and package maintenance.
Edited: 2026-11-21T00:52:49-06:00 | Computer: Vinnabok | File: games/Roulette/server/GameWorkerModule.cs — Added durable Roulette checkpoints, command receipts, private history, worker routes and package maintenance.
Edited: 2026-11-21T00:52:49-06:00 | Computer: Vinnabok | File: games/Roulette/game-release.json — Added durable Roulette checkpoints, command receipts, private history, worker routes and package maintenance.

Edited: 2026-11-21T00:52:56-06:00 | Computer: Vinnabok | File: games/Flappy/tests/FortuneForge.Games.Flappy.Tests/FlappyFreeRunEndpointTests.cs — Verify preserved free-run HTTP routes, authentication, feature gates, result shape and cross-route lost-response recovery.

Edited: 2026-11-21T00:52:56-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/HorseFlightFreeRunEndpointTests.cs — Verify preserved free-run HTTP routes, authentication, feature gates, result shape and cross-route lost-response recovery.
Edited: 2026-11-21T00:53:22-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Roulette/RouletteHostAdapters.cs — Added narrow adapters from shared matchmaking and security recording to package-owned Roulette runtime.
Edited: 2026-11-21T00:53:40-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/FortuneForge.Games.DragonTiger.Server.csproj — Reference the shared authoritative Firestore audit writers.
Edited: 2026-11-21T00:53:40-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/FortuneForge.Games.AndarBahar.Server.csproj — Reference the shared authoritative Firestore audit writers.
Edited: 2026-11-21T00:53:57-06:00 | Computer: Vinnabok | File: games/VideoPoker/tests/FortuneForge.Games.VideoPoker.Tests/FortuneForge.Games.VideoPoker.Tests.csproj — Added game-package paid-round test project.
Edited: 2026-11-21T00:53:57-06:00 | Computer: Vinnabok | File: games/VideoPoker/tests/FortuneForge.Games.VideoPoker.Tests/VideoPokerPaidRoundTests.cs — Verified signed-wallet retries, lost-response recovery, legacy states, concurrent draws, audit history and one-time settlement.
Edited: 2026-11-21T00:54:13-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/package.json — Integrate shared per-account active-board and exact command recovery.
Edited: 2026-11-21T00:54:13-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/package.json — Integrate shared per-account active-board and exact command recovery.
Edited: 2026-11-21T00:54:13-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/TwentyFortyEightRoute.tsx — Integrate shared per-account active-board and exact command recovery.
Edited: 2026-11-21T00:54:13-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/DropMergeRoute.tsx — Integrate shared per-account active-board and exact command recovery.
Edited: 2026-11-21T00:54:18-06:00 | Computer: Vinnabok | File: games/HiLo/server/FortuneForge.Games.HiLo.Server/FortuneForge.Games.HiLo.Server.csproj — Reuse shared authoritative Firestore audit writer.
Edited: 2026-11-21T00:54:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Snake/FirestoreSnakeGameService.cs — Delegate existing hosted Snake games to package-owned durable state and replay.
Edited: 2026-11-21T00:54:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Snake/SnakeReplayAudit.cs — Delegate historical Snake evidence replay to its source package.
Edited: 2026-11-21T00:54:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Snake/SnakeController.cs — Use game-owned request and response contracts in existing authenticated URLs.
Edited: 2026-11-21T00:54:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Snake/FirestoreSnakeGameServiceTests.cs — Verify the package-owned Snake errors through the legacy facade.
Edited: 2026-11-21T00:54:48-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/TileMergeRoutes.test.tsx — Preserve authenticated routing assertions with per-account command recovery.

Edited: 2026-11-21T00:54:53-06:00 | Computer: Vinnabok | File: games/Flappy/src/FortuneForge.Games.Flappy/FlappyRunReplay.cs — Own canonical stored replay bytes, parsing and terminal names once for free and paid Flappy outcomes.

Edited: 2026-11-21T00:54:53-06:00 | Computer: Vinnabok | File: games/HorseFlight/src/FortuneForge.Games.HorseFlight/HorseFlightRunReplay.cs — Own legacy recorded-run rules resolution and phase protocol once for paid and free Horse Flight replays.
Edited: 2026-11-21T00:55:01-06:00 | Computer: Vinnabok | File: games/Keno/client/FortuneForge.Games.Keno.Client/src/KenoGame.tsx — Keep ambiguous paid draws locked until original request recovery.
Edited: 2026-11-21T00:55:01-06:00 | Computer: Vinnabok | File: games/Keno/client/FortuneForge.Games.Keno.Client/src/httpKenoGateway.ts — Expose validated historical round retrieval through the existing URL.
Edited: 2026-11-21T00:55:16-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/httpSnakeGateway.ts — Persist and recover exact account-scoped Snake commands before accepting further actions.
Edited: 2026-11-21T00:55:16-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/contracts.ts — Expose durable recovery without forcing local gateways to persist games.
Edited: 2026-11-21T00:55:16-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/snakeRunController.ts — Recover unresolved mutations and latest saved state before resuming an interrupted Snake run.
Edited: 2026-11-21T00:55:16-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/SnakeGame.tsx — Offer recovery after an interrupted run instead of discarding it.
Edited: 2026-11-21T00:55:16-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SnakeRoute.tsx — Scope persisted Snake commands and saved games to the authenticated account.
Edited: 2026-11-21T00:55:32-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/httpLiarsDiceGateway.ts — Recover exact unresolved Liar’s Dice commands and saved matches across remounts.
Edited: 2026-11-21T00:55:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/VideoPoker/PracticeVideoPokerStore.cs — Removed host practice lifecycle after moving the authoritative implementation into the game package.
Edited: 2026-11-21T00:55:45-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/PracticeVideoPokerStore.cs — Moved isolated Video Poker practice session state into the game package.
Edited: 2026-11-21T00:55:45-06:00 | Computer: Vinnabok | File: games/Keno/client/FortuneForge.Games.Keno.Client/src/KenoGame.test.tsx — Check ticket and wager lock during ambiguous draw recovery.
Edited: 2026-11-21T00:55:53-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/contracts.ts — Expose durable recovery while preserving standalone and legacy gateways.
Edited: 2026-11-21T00:55:53-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/useLiarsDiceTable.ts — Replay unresolved accepted commands and restore current match before allowing further play.
Edited: 2026-11-21T00:55:53-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/LiarsDiceRoute.tsx — Bind exact-command and match recovery to the authenticated Liar’s Dice account.
Edited: 2026-11-21T00:55:53-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/package.json — Reuse shared account-scoped game command recovery.
Edited: 2026-11-21T00:55:53-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/package.json — Reuse shared account-scoped game command recovery.
Edited: 2026-11-21T00:55:59-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/src/httpTwentyFortyEightGateway.test.ts — Test persistent exact retries, remount recovery, malformed responses, queue/timer restoration and account isolation.
Edited: 2026-11-21T00:55:59-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/src/httpDropMergeGateway.test.ts — Test persistent exact retries, remount recovery, malformed responses, queue/timer restoration and account isolation.
Edited: 2026-11-21T00:56:17-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/httpSnakeGateway.test.ts — Verify account isolation, blocked mutations, remount replay and latest-state recovery.
Edited: 2026-11-21T00:56:40-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemBackend.cs — Centralize House Hold'em backend identity and historical collection/configuration contract.
Edited: 2026-11-21T00:56:49-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/httpLiarsDiceGateway.test.ts — Verify lost-start response replay, latest-state reads, blocked commands and account isolation.
Edited: 2026-11-21T00:56:55-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/GameComputeModule.cs — Remove obsolete compute-only deployment module after migrating the full backend to its package worker.
Edited: 2026-11-21T00:57:05-06:00 | Computer: Vinnabok | File: games/HiLo/tests/FortuneForge.Games.HiLo.Server.Tests/HiLoEmulator.cs — Bound private emulator heap during parallel catalogue work.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/FortuneForge.Games.CasinoWar.Server.csproj — Move Casino War contracts, stake policy and practice lifecycle into the game backend.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/AssemblyInfo.cs — Move Casino War contracts, stake policy and practice lifecycle into the game backend.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarModels.cs — Move Casino War contracts, stake policy and practice lifecycle into the game backend.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/CasinoWar/CasinoWarModels.cs — Move Casino War contracts, stake policy and practice lifecycle into the game backend.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/PracticeCasinoWarStore.cs — Move Casino War contracts, stake policy and practice lifecycle into the game backend.

Edited: 2026-11-21T00:57:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/CasinoWar/PracticeCasinoWarStore.cs — Move Casino War contracts, stake policy and practice lifecycle into the game backend.
Edited: 2026-11-21T00:57:18-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/package.json — Added shared account-scoped command recovery and frozen revision bodies for Roulette retries and reloads.
Edited: 2026-11-21T00:57:18-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/src/contracts.ts — Added shared account-scoped command recovery and frozen revision bodies for Roulette retries and reloads.
Edited: 2026-11-21T00:57:18-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/src/httpRouletteGateway.ts — Added shared account-scoped command recovery and frozen revision bodies for Roulette retries and reloads.
Edited: 2026-11-21T00:57:18-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/RouletteRoute.tsx — Added shared account-scoped command recovery and frozen revision bodies for Roulette retries and reloads.
Edited: 2026-11-21T00:57:18-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/FortuneForge.Games.Craps.Server.csproj — Added shared Firestore persistence primitives for signed-wallet migration.
Edited: 2026-11-21T00:57:22-06:00 | Computer: VINNABOK | File: games/Asteroids/src/FortuneForge.Games.Asteroids/AsteroidsModule.cs — Expose the authoritative Asteroids identifier for backend composition.
Edited: 2026-11-21T00:57:23-06:00 | Computer: Vinnabok | File: games/Keno/client/FortuneForge.Games.Keno.Client/src/httpKenoGateway.test.ts — Verify validated historical Keno round retrieval and wrong-result rejection.
Edited: 2026-11-21T00:58:44-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/SnakeGame.lifecycle.test.tsx — Keep optional recovery methods compatible with typed gateway test doubles.
Edited: 2026-11-21T00:58:46-06:00 | Computer: Vinnabok | File: games/Mines/tests/FortuneForge.Games.Mines.Server.Tests/MinesHttpTests.cs — Expect recoverable service unavailability when paid wallet storage is absent.
Edited: 2026-11-21T00:58:57-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/snakeRunController.test.ts — Verify cabinet mount and interrupted-step recovery preserve the durable run.
Edited: 2026-11-21T00:59:03-06:00 | Computer: Vinnabok | File: games/HiLo/client/FortuneForge.Games.HiLo.Client/src/httpHiLoGateway.ts — Bind paid game requests to the expected authenticated account.
Edited: 2026-11-21T00:59:03-06:00 | Computer: Vinnabok | File: games/HiLo/client/FortuneForge.Games.HiLo.Client/src/httpHiLoGateway.test.ts — Verify account header and exact unchanged retry payloads.
Edited: 2026-11-21T00:59:03-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HiLoRoute.tsx — Pass the table account into the Hi-Lo gateway.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LiarsDiceSession.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LiarsDiceSessionReplay.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/src/FortuneForge.Games.LiarsDice/LiarsDiceStateJson.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/FortuneForge.Games.LiarsDice.Server/FortuneForge.Games.LiarsDice.Server.csproj — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/FortuneForge.Games.LiarsDice.Server/LiarsDiceEndpoints.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/FortuneForge.Games.LiarsDice.Server/LiarsDiceWorkerRoster.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/GameWorkerModule.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/Snake/tests/FortuneForge.Games.Snake.Tests/FortuneForge.Games.Snake.Tests.csproj — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/Snake/tests/FortuneForge.Games.Snake.Tests/SnakeBackendTests.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/tests/FortuneForge.Games.LiarsDice.Tests/FortuneForge.Games.LiarsDice.Tests.csproj — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:20-06:00 | Computer: Vinnabok | File: games/LiarsDice/tests/FortuneForge.Games.LiarsDice.Tests/LiarsDiceBackendTests.cs — Own game rules, durable private lifecycle, replay and exact retry verification.
Edited: 2026-11-21T00:59:38-06:00 | Computer: Vinnabok | File: games/HiLo/client/FortuneForge.Games.HiLo.Client/src/hiLoPendingCommand.ts — Retain the original account's pending operation after a session change rejection.
Edited: 2026-11-21T00:59:45-06:00 | Computer: Vinnabok | File: games/Mines/server/GameWorkerModule.cs — Register the Mines signed wallet only inside its own worker process.
Edited: 2026-11-21T00:59:57-06:00 | Computer: Vinnabok | File: games/HiLo/client/FortuneForge.Games.HiLo.Client/src/hiLoPendingCommand.test.ts — Verify pending identity survives expected-account mismatch.
Edited: 2026-11-21T01:00:12-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client/README.md — Document private free-play ownership, exact recovery and opt-in legacy routing.
Edited: 2026-11-21T01:00:12-06:00 | Computer: Vinnabok | File: games/DropMerge/client/FortuneForge.Games.DropMerge.Client/README.md — Document private free-play ownership, exact recovery and opt-in legacy routing.
Edited: 2026-11-21T01:00:23-06:00 | Computer: Vinnabok | File: games/Mines/client/FortuneForge.Games.Mines.Client/src/httpMinesGateway.ts — Bind paid requests to the account that opened the game.
Edited: 2026-11-21T01:00:23-06:00 | Computer: Vinnabok | File: games/Mines/client/FortuneForge.Games.Mines.Client/src/MinesGame.tsx — Create an account-bound default Mines gateway.
Edited: 2026-11-21T01:00:23-06:00 | Computer: Vinnabok | File: games/Mines/client/FortuneForge.Games.Mines.Client/src/useMinesSession.ts — Preserve pending identity when the signed account session changes.
Edited: 2026-11-21T01:00:37-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/useLiarsDiceTable.behavior.test.tsx — Keep legacy hook fixtures explicit while durable recovery remains optional.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/BlackjackModels.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/BlackjackStore.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/FirestoreBlackjackStore.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/BlackjackControllers.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/BlackjackContractTests.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/LegacyBlackjackAuditFirestoreTests.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: games/Blackjack/src/FortuneForge.Games.Blackjack/BlackjackRules.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloReplay.cs — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: games/Blackjack/client/FortuneForge.Games.Blackjack.Client/src/tableApi.ts — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: games/Blackjack/client/FortuneForge.Games.Blackjack.Client/src/BlackjackTableGame.tsx — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/cards/blackjack/BlackjackTablePage.tsx — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:00:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/cards/blackjack/blackjackTableApi.test.ts — Move solo lifecycle to game package, preserve paid-hand replay, and retain account-scoped exact table requests across reloads.
Edited: 2026-11-21T01:01:04-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/FirestorePaidCrapsStore.cs — Replaced direct account writes with saved Craps transitions and exact signed-wallet recovery for opening, rolls, odds, side bets and refunds.
Edited: 2026-11-21T01:01:04-06:00 | Computer: Vinnabok | File: games/Craps/server/GameWorkerModule.cs — Replaced direct account writes with saved Craps transitions and exact signed-wallet recovery for opening, rolls, odds, side bets and refunds.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: games/SicBo/client/FortuneForge.Games.SicBo.Client/src/httpSicBoGateway.ts — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/MinesRoute.tsx — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/KenoRoute.tsx — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SicBoRoute.tsx — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/KenoRoute.test.tsx — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SicBoRoute.test.tsx — Bind number-game wallet requests to their original signed account and preserve scoped recovery.
Edited: 2026-11-21T01:01:37-06:00 | Computer: Vinnabok | File: games/HiLo/src/FortuneForge.Games.HiLo/HiLoEngine.cs — Own revealed-history validation in pure game engine with historical wager compatibility.
Edited: 2026-11-21T01:01:37-06:00 | Computer: Vinnabok | File: games/HiLo/tests/FortuneForge.Games.HiLo.Engine.Tests/HiLoEngineTests.cs — Check historical replay and rejection of changed outcomes, revisions and history.
Edited: 2026-11-21T01:02:00-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRuns.cs — Own Asteroids paid seed, legacy run codec, replay validation and admission lifecycle over shared competition operations.
Edited: 2026-11-21T01:02:00-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRunService.cs — Own Asteroids paid seed, legacy run codec, replay validation and admission lifecycle over shared competition operations.
Edited: 2026-11-21T01:02:02-06:00 | Computer: Vinnabok | File: games/HiLo/server/FortuneForge.Games.HiLo.Server/README.md — Document signed wallet configuration, staged lifecycle, legacy replay and recovery.
Edited: 2026-11-21T01:02:05-06:00 | Computer: Vinnabok | File: games/Mines/client/FortuneForge.Games.Mines.Client/src/MinesGame.test.tsx — Verify expected-account headers and pending-command retention after session changes.
Edited: 2026-11-21T01:02:05-06:00 | Computer: Vinnabok | File: games/Mines/client/FortuneForge.Games.Mines.Client/src/httpMinesGateway.test.ts — Verify expected-account headers and pending-command retention after session changes.
Edited: 2026-11-21T01:02:20-06:00 | Computer: Vinnabok | File: games/SicBo/client/FortuneForge.Games.SicBo.Client/src/httpSicBoGateway.test.ts — Verify account binding for paid slip retries and historical round reads.
Edited: 2026-11-21T01:02:21-06:00 | Computer: Vinnabok | File: games/HiLo/server/FortuneForge.Games.HiLo.Server/DurablePracticeHiLoStore.cs — Reuse pure engine history validation without a paid-store dependency.
Edited: 2026-11-21T01:03:04-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsPaidRunTests.cs — Verify paid Asteroids one-time admission, lost debit recovery, rollover retries and legacy ledgers through the shared runtime.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarRoundCodec.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarPreparedAction.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarFirestoreStore.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarRoundReporting.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarEndpoints.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarBackendRegistration.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/GameWorkerModule.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: games/CasinoWar/game-release.json — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/CasinoWar/CasinoWarFirestoreStore.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.

Edited: 2026-11-21T01:03:25-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/CasinoWar/CasinoWarController.cs — Own Casino War paid actions, signed-wallet receipts, recovery and historical replay in the game backend.
Edited: 2026-11-21T01:03:37-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/SignedRoulettePopulation.cs — Integrated shared managed policies and signed roster leases; retained historical Craps replay with signed receipt financial reconciliation.
Edited: 2026-11-21T01:03:37-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/PaidCrapsReplayAudit.cs — Integrated shared managed policies and signed roster leases; retained historical Craps replay with signed receipt financial reconciliation.
Edited: 2026-11-21T01:03:47-06:00 | Computer: Vinnabok | File: games/TwentyFortyEight/src/FortuneForge.Games.TwentyFortyEight/TwentyFortyEightActionReplay.cs — Expose complete-run replay across completion and undo episodes using authoritative action verification.
Edited: 2026-11-21T01:03:47-06:00 | Computer: Vinnabok | File: games/DropMerge/src/FortuneForge.Games.DropMerge/DropMergeActionReplay.cs — Expose complete-run replay across completion and undo episodes using authoritative action verification.
Edited: 2026-11-21T01:04:58-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SnakeRoute.test.tsx — Keep the existing route assertion aligned with account-scoped gateway construction.
Edited: 2026-11-21T01:04:58-06:00 | Computer: Vinnabok | File: games/Snake/tests/FortuneForge.Games.Snake.Tests/SnakeEndpointTests.cs — Verify preserved public URLs, validation, private-state boundaries and authenticated ownership.
Edited: 2026-11-21T01:04:59-06:00 | Computer: Vinnabok | File: games/LiarsDice/tests/FortuneForge.Games.LiarsDice.Tests/LiarsDiceEndpointTests.cs — Verify preserved public URLs, validation, private-state boundaries and authenticated ownership.

Edited: 2026-11-21T01:05:10-06:00 | Computer: Vinnabok | File: games/CasinoWar/tests/FortuneForge.Games.CasinoWar.Tests/FortuneForge.Games.CasinoWar.Tests.csproj — Verify Casino War immutable receipts, lost wallet responses, concurrent retries, legacy paid stakes and fail-closed host fallback.

Edited: 2026-11-21T01:05:10-06:00 | Computer: Vinnabok | File: games/CasinoWar/tests/FortuneForge.Games.CasinoWar.Tests/CasinoWarBackendTests.cs — Verify Casino War immutable receipts, lost wallet responses, concurrent retries, legacy paid stakes and fail-closed host fallback.

Edited: 2026-11-21T01:05:10-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/CasinoWar/CasinoWarAnalyticsFirestoreTests.cs — Verify Casino War immutable receipts, lost wallet responses, concurrent retries, legacy paid stakes and fail-closed host fallback.

Edited: 2026-11-21T01:05:10-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/CasinoWar/CasinoWarContractTests.cs — Verify Casino War immutable receipts, lost wallet responses, concurrent retries, legacy paid stakes and fail-closed host fallback.
Edited: 2026-11-21T01:05:11-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/src/browser.ts — Expose the deployable default browser component through the established game entry contract.
Edited: 2026-11-21T01:05:11-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/browser.ts — Expose the deployable default browser component through the established game entry contract.
Edited: 2026-11-21T01:05:13-06:00 | Computer: Vinnabok | File: games/Roulette/tests/FortuneForge.Games.Roulette.Tests/FortuneForge.Games.Roulette.Tests.csproj — Added package tests for exact Roulette retries, private history, multi-replica state, disconnect recovery and account-scoped browser replay.
Edited: 2026-11-21T01:05:13-06:00 | Computer: Vinnabok | File: games/Roulette/tests/FortuneForge.Games.Roulette.Tests/RoulettePersistenceTests.cs — Added package tests for exact Roulette retries, private history, multi-replica state, disconnect recovery and account-scoped browser replay.
Edited: 2026-11-21T01:05:13-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/src/httpRouletteGateway.test.ts — Added package tests for exact Roulette retries, private history, multi-replica state, disconnect recovery and account-scoped browser replay.
Edited: 2026-11-21T01:05:45-06:00 | Computer: Vinnabok | File: games/VideoPoker/client/FortuneForge.Games.VideoPoker.Client/src/VideoPokerGame.test.tsx — Allowed the real card reveal animation sufficient asynchronous assertion time on contended test machines.
Edited: 2026-11-21T01:05:53-06:00 | Computer: Vinnabok | File: games/Snake/client/FortuneForge.Games.Snake.Client/vite.config.ts — Use one canonical package root so browser test modules resolve through the supplied checkout junction.
Edited: 2026-11-21T01:05:53-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/vite.config.ts — Use one canonical package root so browser test modules resolve through the supplied checkout junction.
Edited: 2026-11-21T01:05:56-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsFreeRunHttpTests.cs — Verify existing authenticated HTTP wire contracts and replay recovery end to end.
Edited: 2026-11-21T01:06:49-06:00 | Computer: Vinnabok | File: games/Craps/tests/FortuneForge.Games.Craps.Tests/FortuneForge.Games.Craps.Tests.csproj — Added focused Craps signed-wallet tests for lost starts and rolls, replicas, odds refunds, exact declines and legacy round/receipt continuation with financial replay.
Edited: 2026-11-21T01:06:49-06:00 | Computer: Vinnabok | File: games/Craps/tests/FortuneForge.Games.Craps.Tests/CrapsWalletTests.cs — Added focused Craps signed-wallet tests for lost starts and rolls, replicas, odds refunds, exact declines and legacy round/receipt continuation with financial replay.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/contracts.ts — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/index.ts — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/httpCasinoWarGateway.ts — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/CasinoWarGame.tsx — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/httpCasinoWarGateway.test.ts — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.

Edited: 2026-11-21T01:07:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/CasinoWarGame.test.tsx — Recover Casino War from game-owned state across devices, preserve ambiguous retries, and release definitive credit declines.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/httpDragonTigerGateway.ts — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/httpDragonTigerGateway.test.ts — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/DragonTigerGame.tsx — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/DragonTigerGame.test.tsx — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client/src/httpAndarBaharGateway.ts — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client/src/httpAndarBaharGateway.test.ts — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client/src/useAndarBaharTable.test.tsx — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/DragonTigerRoute.tsx — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:35-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AndarBaharRoute.tsx — Bind paid requests to the opened account and verify journals survive session changes and paused recovery.
Edited: 2026-11-21T01:07:50-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRunEndpoints.cs — Preserve paid entry/replay HTTP contracts and recover committed window state.
Edited: 2026-11-21T01:08:12-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/DragonTigerRoute.test.tsx — Verify paid gateway identity follows the account that opened each game session.
Edited: 2026-11-21T01:08:12-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AndarBaharRoute.test.tsx — Verify paid gateway identity follows the account that opened each game session.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: games/Craps/client/FortuneForge.Games.Craps.Client/src/httpCrapsGateway.ts — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: games/Craps/client/FortuneForge.Games.Craps.Client/src/httpCrapsGateway.test.ts — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/CrapsRoute.tsx — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/httpBaccaratGateway.ts — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/httpBaccaratGateway.test.ts — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.
Edited: 2026-11-21T01:08:35-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/BaccaratRoute.tsx — Pinned pending Baccarat/Craps requests to their authenticated account so stale browser sessions cannot replay against another account.

Edited: 2026-11-21T01:08:40-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FlappyPaidRuns.cs — Own paid Flappy run schema, seed, replay verification, legacy recovery and terminal audit commit through shared competition admission.
Edited: 2026-11-21T01:09:30-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoMoney.cs — Separate number-game wager policy and service orchestration from wire contracts.
Edited: 2026-11-21T01:09:30-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoService.cs — Separate number-game wager policy and service orchestration from wire contracts.
Edited: 2026-11-21T01:09:30-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoMoney.cs — Separate number-game wager policy and service orchestration from wire contracts.
Edited: 2026-11-21T01:09:30-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoService.cs — Separate number-game wager policy and service orchestration from wire contracts.
Edited: 2026-11-21T01:09:31-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/liarsDiceHelpers.ts — Restore the visible round history from saved server outcomes after reconnecting.
Edited: 2026-11-21T01:09:39-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/CrapsWalletTransitions.cs — Kept Craps rules, saved state, and wallet transition persistence in cohesive package modules.
Edited: 2026-11-21T01:09:39-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/CrapsRoundRules.cs — Kept Craps rules, saved state, and wallet transition persistence in cohesive package modules.
Edited: 2026-11-21T01:09:39-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/CrapsRoundState.cs — Kept Craps rules, saved state, and wallet transition persistence in cohesive package modules.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemService.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemService.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemEscrow.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemEscrow.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemMatchPersistence.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemMatchPersistence.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemTableCoordinator.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemTableCoordinator.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/FirestoreCreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/FirestoreCreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/InMemoryCreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/InMemoryCreditHoldemStore.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/TexasHoldem/TexasHoldemManagedPlayers.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/ManagedPlayers/TexasHoldemManagedPlayers.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/TexasHoldem/TexasHoldemManagedActionPolicy.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/ManagedPlayers/TexasHoldemManagedActionPolicy.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/RoundAudit/HoldemRoundAudit.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/Auditing/HoldemRoundAudit.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/HoldemActionReplay.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/Auditing/HoldemActionReplay.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/HoldemFinancialAudit.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:43-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/Auditing/HoldemFinancialAudit.cs — Move authoritative Texas Hold'em lifecycle, managed play, replay and persistence into its backend package.
Edited: 2026-11-21T01:09:45-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/FortuneForge.Games.TexasHoldem.Server.csproj — Establish Texas Hold'em package backend ownership and canonical identity.
Edited: 2026-11-21T01:09:45-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemBackend.cs — Establish Texas Hold'em package backend ownership and canonical identity.
Edited: 2026-11-21T01:09:45-06:00 | Computer: Vinnabok | File: games/TexasHoldem/src/FortuneForge.Games.TexasHoldem/AssemblyInfo.cs — Establish Texas Hold'em package backend ownership and canonical identity.
Edited: 2026-11-21T01:09:47-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/liarsDiceHelpers.test.ts — Verify a remounted match restores completed rounds from server-owned outcomes.
Edited: 2026-11-21T01:09:47-06:00 | Computer: Vinnabok | File: games/LiarsDice/game-release.json — Declare the packaged Liar’s Dice worker and its deployable browser entry.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/InMemoryThreeCardPokerTableStore.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerMoney.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableCoordinator.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableModels.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableService.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableStore.cs — Moved authoritative Three Card Poker application state, projection and local rules orchestration into the game backend.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ManagedPlayers/ThreeCardPokerManagedActionPolicy.cs — Moved game-specific managed seat metadata and card strategy while reusing shared managed player policies.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ManagedPlayers/ThreeCardPokerManagedSeat.cs — Moved game-specific managed seat metadata and card strategy while reusing shared managed player policies.
Edited: 2026-11-21T01:10:00-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ManagedPlayers/ThreeCardPokerManagedTableState.cs — Moved game-specific managed seat metadata and card strategy while reusing shared managed player policies.

Edited: 2026-11-21T01:10:41-06:00 | Computer: Vinnabok | File: games/CasinoWar/tests/FortuneForge.Games.CasinoWar.Tests/CasinoWarEndpointTests.cs — Verify signed Casino War public routes, ambiguous-wallet recovery, exact replay and private history.
Edited: 2026-11-21T01:11:16-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemController.cs — Remove host paid lifecycle execution and preserve Texas Hold'em routes/status through package contracts.
Edited: 2026-11-21T01:11:16-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/TexasHoldem/Credit/CreditHoldemConfiguration.cs — Remove host paid lifecycle execution and preserve Texas Hold'em routes/status through package contracts.
Edited: 2026-11-21T01:11:16-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemServerContract.cs — Remove host paid lifecycle execution and preserve Texas Hold'em routes/status through package contracts.
Edited: 2026-11-21T01:11:41-06:00 | Computer: VINNABOK | File: games/Asteroids/client/FortuneForge.Games.Asteroids.Client/src/asteroidsPaidAdmission.ts — Freeze Asteroids paid admission intent for safe rollover recovery and isolate account sessions.
Edited: 2026-11-21T01:11:41-06:00 | Computer: VINNABOK | File: games/Asteroids/client/FortuneForge.Games.Asteroids.Client/src/asteroidsPaidAdmission.test.ts — Freeze Asteroids paid admission intent for safe rollover recovery and isolate account sessions.
Edited: 2026-11-21T01:11:41-06:00 | Computer: VINNABOK | File: games/Asteroids/client/FortuneForge.Games.Asteroids.Client/src/index.ts — Freeze Asteroids paid admission intent for safe rollover recovery and isolate account sessions.
Edited: 2026-11-21T01:11:41-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/AsteroidsRoute.tsx — Freeze Asteroids paid admission intent for safe rollover recovery and isolate account sessions.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/horseFlightCompetitionGateway.ts — Allow accepted paid-run replay recovery across competition rollover without current leaderboard lookup while preserving frozen mode and run identity.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/horseFlightCompetitionGateway.test.ts — Verify accepted-window completion through rollover and leaderboard outages while rejecting cross-mode run identities.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/horseFlightRecovery.ts — Restore verified accepted flights from their original competition window while retaining owner/mode checks and holding unknown admissions.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/HorseFlightGame.tsx — Resume accepted original-window runs and retry their terminal replay without admitting a second paid flight.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/horseFlightRecovery.test.ts — Verify persisted accepted-flight reconstruction across rollover with original window and immutable run ID.

Edited: 2026-11-21T01:11:52-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/HorseFlightGame.recovery.test.tsx — Verify UI retries accepted original-window paid results after rollover without a second admission or rewritten window.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/Mines/server/FortuneForge.Games.Mines.Server/README.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/Mines/HOST_INTEGRATION.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/Mines/INTEGRATION.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/Mines/README.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/README.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:12-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/README.md — Document authoritative signed wallet lifecycle, historical recovery, worker settings and separate deployment status.
Edited: 2026-11-21T01:12:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/games/asteroids/AsteroidsCompetitionPage.tsx — Retain immutable paid admission terms and recover ambiguous starts without charging a new competition window.
Edited: 2026-11-21T01:12:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/games/asteroids/asteroidsCompetitionHelpers.ts — Retain immutable paid admission terms and recover ambiguous starts without charging a new competition window.
Edited: 2026-11-21T01:13:26-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/games/asteroids/AsteroidsCompetitionPage.test.tsx — Verify the definitive unrecorded-entry recovery message.
Edited: 2026-11-21T01:13:49-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/README.md — Document game-owned free-play scope, durable recovery, legacy compatibility and signed roster requirements.
Edited: 2026-11-21T01:13:49-06:00 | Computer: Vinnabok | File: games/Snake/server/README.md — Document free-play backend ownership, preserved saved state and exact recovery behavior.
Edited: 2026-11-21T01:13:52-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemMutation.cs — Freeze typed Texas Hold'em table/session/journal changes and exact command responses before atomic wallet effects.

Edited: 2026-11-21T01:13:57-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightPaidRuns.cs — Own paid Horse Flight run schema, historical rules, authoritative replay and terminal audit commit through shared competition admission.
Edited: 2026-11-21T01:14:04-06:00 | Computer: Vinnabok | File: games/Craps/client/FortuneForge.Games.Craps.Client/src/useCrapsTable.ts — Retained ambiguous paid command identities after expired authentication, changed sessions and rate limits.
Edited: 2026-11-21T01:14:10-06:00 | Computer: Vinnabok | File: games/DragonTiger/server/README.md — Document current signed wallet lifecycle, paid worker configuration, and read-only historical adapters.
Edited: 2026-11-21T01:14:10-06:00 | Computer: Vinnabok | File: games/AndarBahar/server/INTEGRATION.md — Document current signed wallet lifecycle, paid worker configuration, and read-only historical adapters.
Edited: 2026-11-21T01:14:59-06:00 | Computer: Vinnabok | File: games/Craps/client/FortuneForge.Games.Craps.Client/src/useCrapsTable.behavior.test.tsx — Verified that pending paid actions retain their original identities through authentication changes and reloads.
Edited: 2026-11-21T01:14:59-06:00 | Computer: Vinnabok | File: games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/BaccaratGame.test.tsx — Verified that pending paid actions retain their original identities through authentication changes and reloads.
Edited: 2026-11-21T01:15:01-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemSessionProjection.cs — Persist and recover fenced Hold'em mutations with atomic signed wallet batches and immutable session receipts.
Edited: 2026-11-21T01:15:01-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemMutationJournal.cs — Persist and recover fenced Hold'em mutations with atomic signed wallet batches and immutable session receipts.

Edited: 2026-11-21T01:15:10-06:00 | Computer: Vinnabok | File: games/CasinoWar/src/FortuneForge.Games.CasinoWar/CasinoWarModule.cs — Centralize the Casino War identifier and API prefix for descriptor, worker and persistence callers.

Edited: 2026-11-21T01:16:04-06:00 | Computer: Vinnabok | File: games/Flappy/src/FortuneForge.Games.Flappy/FlappyModule.cs — Own the canonical game identifier once for engine, backend and worker.

Edited: 2026-11-21T01:16:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/src/FortuneForge.Games.HorseFlight/HorseFlightModule.cs — Own the canonical game identifier once for engine, backend and worker.

Edited: 2026-11-21T01:16:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/horseFlightSimulation.ts — Preserve archived version 1 world generation and collision geometry so historical accepted runs can resume under their saved rules.

Edited: 2026-11-21T01:16:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/contracts.ts — Carry the issued rules version in Horse Flight admission receipts for historical replay recovery.

Edited: 2026-11-21T01:16:04-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/httpHorseFlightGateway.ts — Validate optional server-issued historical rules versions on Horse Flight start receipts.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Baccarat/INTEGRATION.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Roulette/INTEGRATION.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Craps/INTEGRATION.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Baccarat/src/FortuneForge.Games.Baccarat/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Baccarat/samples/FortuneForge.Games.Baccarat.Web/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.
Edited: 2026-11-21T01:16:38-06:00 | Computer: Vinnabok | File: games/Craps/samples/FortuneForge.Games.Craps.Web/README.md — Documented package ownership, signed lifecycle, historical recovery, integration settings and pending local/deployment checks.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/CosmicFortune/game-release.json
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/CosmicFortuneEngine.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/game-release.json
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/src/FortuneForge.Games.OceanOdyssey/OceanOdysseyEngine.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortunePaidEndpoints.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortunePaidSlotOutcomes.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundModels.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundService.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStateCodec.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStore.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinDraws.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinEvidence.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinReport.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinResponse.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/FortuneForge.Games.CosmicFortune.Server.csproj
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/Program.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/CosmicFortune/server/GameWorkerModule.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:47-06:00 | Computer: VINNABOK | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneRoundStateCodecTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:42:57-06:00 | Computer: VINNABOK | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneRoundStoreTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:42:10-06:00 | Computer: VINNABOK | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneSpinReportingTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/FortuneForge.Games.CosmicFortune.Tests.csproj
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/FortuneForge.Games.OceanOdyssey.Server.csproj
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyPaidEndpoints.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyPaidSlotOutcomes.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:54-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyRoundModels.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:54-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyRoundPolicy.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyRoundService.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyRoundStateCodec.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:53-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseyRoundStore.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseySpinDraws.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseySpinEvidence.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseySpinReport.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:31-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/OceanOdysseySpinResponse.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/FortuneForge.Games.OceanOdyssey.Server/Program.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:07-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/server/GameWorkerModule.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:13-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/tests/FortuneForge.Games.OceanOdyssey.Tests/FortuneForge.Games.OceanOdyssey.Tests.csproj
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:41:47-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/tests/FortuneForge.Games.OceanOdyssey.Tests/OceanOdysseyRoundStateCodecTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:42:57-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/tests/FortuneForge.Games.OceanOdyssey.Tests/OceanOdysseyRoundStoreTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T00:42:10-06:00 | Computer: VINNABOK | File: games/OceanOdyssey/tests/FortuneForge.Games.OceanOdyssey.Tests/OceanOdysseySpinReportingTests.cs
  Summary: Implement the package-owned paid slot round, recovery, audit evidence, or release contract.

Edited: 2026-11-21T01:15:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference game-owned backends and neutral runtime libraries.

Edited: 2026-11-21T01:15:54-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include game-owned worker libraries and test projects in the solution.

Edited: 2026-11-21T00:53:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs
  Summary: Route package-owned paid slot state and commands through signed workers.

Edited: 2026-11-21T00:53:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs
  Summary: Verify the host cannot reopen worker-owned slot admissions.

Edited: 2026-11-21T00:53:07-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts
  Summary: Enable account-scoped recovery for newly packaged paid slots.

Edited: 2026-11-21T01:03:49-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts
  Summary: Refresh state after a legacy round closes during cutover.

Edited: 2026-11-21T01:14:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs
  Summary: Preserve signed exact-body card and arcade routing, durable identity affinity, and recovery during paid admission pauses.

Edited: 2026-11-21T01:14:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs
  Summary: Exercise forwarding configuration, signed conditional writes, and durable affinity.

Edited: 2026-11-21T00:48:01-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerAuthentication.cs
  Summary: Bind conditional version headers to worker authentication.

Edited: 2026-11-21T01:06:04-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerPublicRoute.cs
  Summary: Return a scoped migration conflict for host-owned classic slot admissions.

Edited: 2026-11-21T01:06:04-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotPaidRoundStore.cs
  Summary: Recognize an open legacy host admission without redrawing.

Edited: 2026-11-21T01:06:04-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/LegacySlotAdmissionPendingException.cs
  Summary: Define the worker and gateway contract for an open legacy slot admission.

Edited: 2026-11-21T01:02:46-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/Program.cs
  Summary: Accept valid maximum Horse Flight replay payloads.

Edited: 2026-11-21T01:06:04-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs
  Summary: Register scoped managed player roster and atomic wallet batch endpoints.

Edited: 2026-11-21T00:53:51-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionConfiguration.cs
  Summary: Compose neutral competition services and package free Solitaire routes.

Edited: 2026-11-21T01:02:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionController.cs
  Summary: Use package-owned Asteroids replay contracts.

Edited: 2026-11-21T01:02:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/ArcadeReplayAudit.cs
  Summary: Verify Asteroids evidence with its authoritative canonical parser.

Edited: 2026-11-21T01:02:47-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionPaidEntryServiceTests.cs
  Summary: Use the game package Asteroids run identity in the test adapter.

Edited: 2026-11-21T01:02:47-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/FirestoreArcadeCompetitionPaidEntryCoordinatorTests.cs
  Summary: Exercise the host legacy bridge with package-owned replay types.

Edited: 2026-11-21T01:02:47-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/FirestoreAsteroidsFreeRunServiceTests.cs
  Summary: Use the authoritative Asteroids evaluator in free-run tests.

Edited: 2026-11-21T01:03:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IAccountStore.cs
  Summary: Expose the guarded classic slot legacy admission reconciler.

Edited: 2026-11-21T01:03:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Services/AccountService.Slots.cs
  Summary: Connect the authenticated slot gateway to migration reconciliation.

Edited: 2026-11-21T01:06:04-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs
  Summary: Keep live legacy spins and reconcile expired admission evidence before any new wager.

Edited: 2026-11-21T01:08:26-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/RequestObservationMiddleware.cs
  Summary: Expose an exact buffered body digest to the host compatibility gateway.

Edited: 2026-11-21T01:08:26-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Snake/SnakeController.cs
  Summary: Bind the legacy Snake route to exact command receipts.

Edited: 2026-11-21T01:05:30-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Activity/GameAnalyticsContracts.cs
  Summary: Preserve host interface compatibility while delegating its contract to the neutral assembly.

Edited: 2026-11-21T01:05:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/IGameAnalyticsSink.cs
  Summary: Expose free and practice analytics as a neutral port.

Edited: 2026-11-21T01:10:53-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs
  Summary: Preserve old arcade entry ledger identities in signed wallet settlement.

Edited: 2026-11-21T01:15:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletSettlementEndpoints.cs
  Summary: Authorize scoped signed competition wallet operations and trusted optional name projection.

Edited: 2026-11-21T01:15:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/ArcadeCompetitionWalletEndpoints.cs
  Summary: Bind signed competition entry, legacy debit proof, and frozen-plan payouts to the platform wallet.

Edited: 2026-11-21T01:11:51-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletAuthentication.cs
  Summary: Sign exact private wallet POST paths and bytes.

Edited: 2026-11-21T01:12:27-06:00 | Computer: VINNABOK | File: firestore.indexes.json
  Summary: Add account and creation-time indexes for Video Poker and Casino War history.

Edited: 2026-11-21T01:03:49-06:00 | Computer: VINNABOK | File: package-lock.json
  Summary: Record the package-neutral puzzle client command recovery dependency.

Edited: 2026-11-21T01:15:54-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Recognize newly packaged workers and their game-specific admission flags.

Edited: 2026-11-21T01:14:13-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/arcade/arcadeCompetitionApi.ts
  Summary: Freeze paid competition window terms and carry retry control outside the body.

Edited: 2026-11-21T01:14:59-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/arcade/arcadeCompetitionApi.test.ts
  Summary: Verify identical restored body bytes and operation key across arcade retries.

Edited: 2026-11-21T01:12:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.LegacySlotRecovery.cs
  Summary: Verify and preserve exact legacy admissions or audited game evidence during cutover.

Edited: 2026-11-21T01:12:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/LegacySlotRecoveryFirestoreTests.cs
  Summary: Verify and preserve exact legacy admissions or audited game evidence during cutover.

Edited: 2026-11-21T01:10:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PracticeActionReplayAudit.cs
  Summary: Verify and preserve exact legacy admissions or audited game evidence during cutover.

Edited: 2026-11-21T01:14:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityIntegrityAuditor.cs
  Summary: Verify and preserve exact legacy admissions or audited game evidence during cutover.

Edited: 2026-11-21T01:14:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/PracticeActionReplayAuditTests.cs
  Summary: Verify and preserve exact legacy admissions or audited game evidence during cutover.

Edited: 2026-11-21T00:44:26-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreGameCommands.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:08-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreGameAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:49:45-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreGameJournal.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:08-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestorePuzzleSessions.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:55-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/ArcadeRunSecurityAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:13:45-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreFieldCodec.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityJournal.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Activity/AcceptedGameTransactionWitness.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/RoundAuditModels.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:55-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/ArcadeRunSecurityAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:00:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/FirestoreArcadeCompetitionPayoutCreditor.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:06:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/FirestoreArcadeCompetitionPaidEntryCoordinator.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:15:49-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletSettlement.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:52:28-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletReceiptVerifier.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:07:17-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletBatchSettlement.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:16:10-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletClient.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:07:17-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletBatchAuthentication.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:07:17-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletBatchClient.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:57:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:09:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletBatchEndpoints.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:07:53-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletBatchFirestoreStore.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:09:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletBatchRpcTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:16:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletSettlementRpcTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:51:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/FirestoreWalletRoundSettlementStoreTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:59:11-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletFirestoreTestClient.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:00:44-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardFinancialAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:04:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardEvidenceReplayAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:04:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/MinesSequenceReplayAudit.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:00:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/PaidCardFinancialAuditTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:59:11-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/HiLo/HiLoHistoryIntegrationTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:04:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/Mines/MinesHistoryIntegrationTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:04:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestEngineJournalFirestoreTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:04:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Configuration/FirestoreEmulatorIdentityIntegrationTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:12:08-06:00 | Computer: VINNABOK | File: games/tests/FortuneForge.Games.Tests/FortuneForge.Games.Tests.csproj
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T01:12:08-06:00 | Computer: VINNABOK | File: games/tests/FortuneForge.Games.Tests/Firestore/FirestoreFieldCodecTests.cs
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:54-06:00 | Computer: VINNABOK | File: packages/client-presentation/src/gameCommandRecovery.ts
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:54-06:00 | Computer: VINNABOK | File: packages/client-presentation/src/gameCommandRecovery.test.ts
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:04-06:00 | Computer: VINNABOK | File: packages/client-presentation/src/index.ts
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:46:04-06:00 | Computer: VINNABOK | File: packages/client-presentation/package.json
  Summary: Share signed wallet, command recovery, or historical evidence validation between game packages.

Edited: 2026-11-21T00:50:31-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerSkillLevels.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:50:31-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/DeterministicManagedPlayerRandom.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:52:33-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerRoster.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:36-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedTablePopulationDirector.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:38-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerTableStayPolicy.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:53-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerDecisionOptions.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:36-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/Matchmaking/ManagedQueueArrivalPolicy.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:59:52-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/Matchmaking/MultiplayerQueuePolicy.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:52:33-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameManagedPlayerRosterProtocol.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:54:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameManagedPlayerRosterClient.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:54:50-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/GameManagedPlayerRosterEndpoints.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:54:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/FirestoreManagedPlayerAssignmentStore.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:54:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/ManagedPlayerQueuer.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/ManagedPlayerProfiles.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:53-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/ManagedPlayerDecisionRuntime.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:36-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/ManagedTablePopulationDirector.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:58:36-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Matchmaking/ManagedQueueArrivalPolicy.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:59:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Matchmaking/MultiplayerMatchmaking.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T00:55:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Bots/ManagedPlayerIsolationFirestoreTests.cs
  Summary: Preserve managed-player identity, deterministic policies, and game-scoped leases.

Edited: 2026-11-21T01:03:34-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionEntryContracts.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionEntryFence.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionFirestoreTime.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:16:34-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionPaidEntries.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:16:34-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionPaidEntryRequest.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T00:54:51-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionPayoutCreditor.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:00:22-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionPayoutPlan.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionRules.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:13:33-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionRunDocuments.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:06:15-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionRuntimeConfiguration.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:10:11-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionService.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:12:35-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionSettlementExecutor.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionSettlementPlanningService.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:06:15-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionStandings.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionStore.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:13:33-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionWalletClient.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:12:35-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionWalletProtocol.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:08:58-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionWindowTerms.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:09:40-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/FirestoreArcadeCompetitionStore.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:12:35-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/FortuneForge.Games.Arcade.Competition.csproj
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:16:48-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition.Tests/ArcadeCompetitionPaidEntriesTests.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:14:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition.Tests/ArcadeCompetitionWalletClientTests.cs
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.

Edited: 2026-11-21T01:01:41-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition.Tests/FortuneForge.Games.Arcade.Competition.Tests.csproj
  Summary: Share arcade competition recovery, standings, signed entry accounting, payout planning, and verification.
Edited: 2026-11-21T01:18:51-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/slot-browser-recovery-smoke.config.ts — Add focused all-six slot browser API recovery smoke coverage using existing client test harness.
Edited: 2026-11-21T01:18:51-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/slot-browser-recovery-smoke.test.ts — Add focused all-six slot browser API recovery smoke coverage using existing client test harness.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableModels.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableModels.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableService.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableService.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableCoordinator.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableCoordinator.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/FirestoreBlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/InMemoryBlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/InMemoryBlackjackTableStore.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Blackjack/BlackjackManagedActionPolicy.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/ManagedPlayers/BlackjackManagedActionPolicy.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Blackjack/BlackjackManagedPlayers.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/ManagedPlayers/BlackjackManagedPlayers.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Blackjack/BlackjackManagedSeat.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/ManagedPlayers/BlackjackManagedSeat.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Blackjack/BlackjackManagedTableState.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/ManagedPlayers/BlackjackManagedTableState.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.Prepared.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableRoundAudit.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableEndpoints.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/Table/BlackjackTableStateTests.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackTableStateTests.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/Table/BlackjackTableSoloTests.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackTableSoloTests.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableConfiguration.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/Table/BlackjackTableController.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:05-06:00 | Computer: Vinnabok | File: games/Blackjack/server/GameComputeModule.cs — Move table/session/managed-seat ownership into Blackjack package with frozen account-and-shard recovery, signed net wallet effects and authoritative replay/history.
Edited: 2026-11-21T01:19:13-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemEndpoints.cs — Preserve authenticated Hold'em URLs, platform player names, paid admission gates and worker-owned deadline recovery.
Edited: 2026-11-21T01:19:45-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/slot-browser-recovery-smoke.config.mjs — Rename smoke config to native ESM for warning-free focused frontend validation.
Edited: 2026-11-21T01:19:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Asteroids/ArcadeCompetitionAsteroidsPaidEntryService.cs — Delegate legacy seed and run identity creation to the authoritative Asteroids package.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Claims.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.Claims.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.ManagedQueue.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.ManagedQueue.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Match.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.Match.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Queue.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.Queue.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.QueuedManagedSeats.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.QueuedManagedSeats.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Reads.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.Reads.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.SecurityJournal.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/FirestoreCompetitiveSolitaireStore.SecurityJournal.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/SolitaireService.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/SolitaireService.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/CompetitiveSolitaireOptions.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireOptions.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/CompetitiveSolitaireRoundAudit.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireRoundAudit.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/SolitaireEvidenceReplayAudit.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/SolitaireEvidenceReplayAudit.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/ManagedPlayers/SolitaireManagedPlayerPolicy.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Solitaire/SolitaireManagedPlayerPolicy.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/ManagedPlayers/SolitaireManagedPlayerProgression.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Solitaire/SolitaireManagedPlayerProgression.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FortuneForge.Games.Solitaire.Server.csproj — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.

Edited: 2026-11-21T01:20:00-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/AssemblyInfo.cs — Move Competitive Solitaire lifecycle, managed-seat policy and replay evidence into its game backend.
Edited: 2026-11-21T01:20:03-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemComputeEndpoints.cs — Host the complete Texas Hold'em backend with signed wallet batch/roster clients and retained compute compatibility routes.
Edited: 2026-11-21T01:20:03-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/GameWorkerModule.cs — Host the complete Texas Hold'em backend with signed wallet batch/roster clients and retained compute compatibility routes.
Edited: 2026-11-21T01:20:03-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/GameComputeModule.cs — Host the complete Texas Hold'em backend with signed wallet batch/roster clients and retained compute compatibility routes.
Edited: 2026-11-21T01:20:03-06:00 | Computer: Vinnabok | File: games/TexasHoldem/game-release.json — Host the complete Texas Hold'em backend with signed wallet batch/roster clients and retained compute compatibility routes.

Edited: 2026-11-21T01:20:07-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/games/flappy/flappyRecovery.ts — Persist immutable expected-window admission terms for new paid starts while retaining legacy pending-key compatibility.

Edited: 2026-11-21T01:20:07-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/games/flappy/useRecordedFlappy.ts — Freeze paid admission terms before transport and retry original terms; legacy ambiguous starts use shared recover-only lookup without a fresh wager.

Edited: 2026-11-21T01:20:07-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/games/flappy/FlappyFreeRunPage.test.tsx — Verify immutable paid admission retries after rollover and recover-only handling of legacy ambiguous starts without a new window wager.

Edited: 2026-11-21T01:20:07-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/games/flappy/flappyRecovery.test.ts — Verify exact admission-body field and timestamp preservation through paid cache reload.
Edited: 2026-11-21T01:21:07-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/browser-module-diagnostics.mjs — Temporarily expose the existing browser test loader failure cause for diagnosis.
Edited: 2026-11-21T01:22:35-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/jsdom-ssr-diagnostic-environment.mjs — Temporarily isolate the browser loader failure while retaining the jsdom DOM environment.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemBuyInAuditTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemBuyInAuditTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemEngineTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemEngineTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemDeadlineAuditTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemDeadlineAuditTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemEscrowStoreTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemEscrowStoreTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemEscrowEngineTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemEscrowEngineTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemMatchPersistenceTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemMatchPersistenceTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemStoreTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemStoreTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemTableLifecycleTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemTableLifecycleTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/TexasHoldemManagedPlayerTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:42-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/TexasHoldemManagedPlayerTests.cs — Move Texas Hold'em engine, escrow, replay and table lifecycle checks beside the game backend.
Edited: 2026-11-21T01:22:49-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/FortuneForge.Games.TexasHoldem.Tests.csproj — Establish independent Texas Hold'em lifecycle/recovery verification.

Edited: 2026-11-21T01:23:37-06:00 | Computer: Vinnabok | File: games/Solitaire/src/FortuneForge.Games.Solitaire/AssemblyInfo.cs — Expose internal competition state to its authoritative backend and package tests.

Edited: 2026-11-21T00:53:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs
  Summary: Dispatch archive verification to each slot package.

Edited: 2026-11-21T01:00:34-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Activity/AcceptedGameTransactionWitnessTests.cs
  Summary: Exercise authoritative acceptance evidence with signed wallet fixtures.

Edited: 2026-11-21T01:19:31-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionSettlementWorker.cs
  Summary: Keep host scheduling off game-owned paid competition windows.

Edited: 2026-11-21T01:19:33-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/ArcadeCompetitionWalletRpcTests.cs
  Summary: Verify signed entry replay, historical debit proof and immutable payout credit.

Edited: 2026-11-21T01:21:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestObservationTests.cs
  Summary: Check exact Snake request digest and safe audit-body projection.

Edited: 2026-11-21T01:23:48-06:00 | Computer: VINNABOK | File: games/catalog/games/asteroids.game.json
  Summary: Match the hidden Asteroids capability registry to the package.

Edited: 2026-11-21T01:23:48-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Register game-owned and shared arcade verification projects.

Edited: 2026-11-21T01:19:31-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document Cosmic and Ocean cutover and fail-closed classic admission recovery.

Edited: 2026-11-21T01:19:16-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/arcade/arcadeCompetitionApi.ts
  Summary: Preserve issued Horse Flight rules versions and full accepted replay length.

Edited: 2026-11-21T01:19:16-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/arcade/arcadeCompetitionApi.test.ts
  Summary: Test archived Horse Flight versions and replay length boundaries.

Edited: 2026-11-21T01:21:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs
  Summary: Verify signed recovery query and exact original bytes.

Edited: 2026-11-21T01:20:44-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotPaidRoundStore.cs
  Summary: Check for host admission recovery before rejecting paused starts.

Edited: 2026-11-21T01:23:32-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerRoster.cs
  Summary: Expose authoritative managed-lease renewal status.

Edited: 2026-11-21T01:22:00-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameManagedPlayerRosterProtocol.cs
  Summary: Bind managed lease renewal to its signed route.

Edited: 2026-11-21T01:22:00-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameManagedPlayerRosterClient.cs
  Summary: Distinguish confirmed lease loss from unavailable renewal.

Edited: 2026-11-21T01:22:00-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/GameManagedPlayerRosterEndpoints.cs
  Summary: Return atomic game-scoped lease renewal results.

Edited: 2026-11-21T01:22:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Bots/ManagedPlayerRosterClientTests.cs
  Summary: Verify exact signed managed lease renewal and fail-closed errors.

Edited: 2026-11-21T01:18:26-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition/ArcadeCompetitionPaidAdmission.cs
  Summary: Validate the preserved competition window before accepting a fresh entry.

Edited: 2026-11-21T01:18:26-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Arcade.Competition.Tests/ArcadeCompetitionPaidAdmissionTests.cs
  Summary: Verify canonical and stale daily and weekly admission windows.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/GameComputeModule.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/GameWorkerModule.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/FortuneForge.Games.ThreeCardPoker.Server.csproj — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ThreeCardPokerBackend.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ThreeCardPokerEndpoints.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.Commands.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.Operations.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.Persistence.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableReplyConverter.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/FortuneForge.Games.ThreeCardPoker.Tests.csproj — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerRecoveryTests.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerTableFirestoreEmulatorTests.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerTableStateTests.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerTestRoster.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.
Edited: 2026-11-21T01:24:46-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerTestWallet.cs — Added game-owned Three Card Poker worker, durable wallet lifecycle, legacy state persistence and recovery verification.

Edited: 2026-11-21T01:24:48-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs — Added six-game signed controller/native-worker/wallet integration coverage for retries, flags, replay and legacy reconciliation.
Edited: 2026-11-21T01:24:55-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/CreditHoldemPaidRecoveryTests.cs — Verify atomic historical blind admission, immutable declines, frozen lost-reply recovery, escrow refund, history and mixed-ledger audit.

Edited: 2026-11-21T01:26:11-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FlappyPaidRunService.cs — Own paid admission orchestration, original competition lookup across rollover, authenticated saved history and authoritative replay over generic wallet/runtime.

Edited: 2026-11-21T01:26:11-06:00 | Computer: Vinnabok | File: games/Flappy/server/FortuneForge.Games.Flappy.Server/FlappyPaidRunEndpoints.cs — Serve preserved paid competition URLs plus authenticated recovery, immutable expected-window body terms and recover-only query admission.

Edited: 2026-11-21T01:26:11-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightPaidRunService.cs — Own paid admission orchestration, original competition lookup across rollover, authenticated saved history and authoritative replay over generic wallet/runtime.

Edited: 2026-11-21T01:26:11-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightPaidRunEndpoints.cs — Serve preserved paid competition URLs plus authenticated recovery, immutable expected-window body terms and recover-only query admission.

Edited: 2026-11-21T01:26:59-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/SolitaireFinancialPlan.cs — Freeze Competitive Solitaire wallet-bearing mutations and immutable replies under a recoverable game-shard gate.

Edited: 2026-11-21T01:26:59-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Financial.cs — Freeze Competitive Solitaire wallet-bearing mutations and immutable replies under a recoverable game-shard gate.

Edited: 2026-11-21T01:29:14-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HorseFlightRoute.tsx — Bind Horse Flight recovery to the selected mode and authenticated account even while current competition status is unavailable.

Edited: 2026-11-21T01:30:06-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/HorseFlightLegacyBrowserParityTests.cs — Cross-check 15 browser version 1 world-generation and accepted-input vectors against the independent authoritative C# engine.

Edited: 2026-11-21T01:30:06-06:00 | Computer: Vinnabok | File: games/HorseFlight/client/FortuneForge.Games.HorseFlight.Client/src/horseFlightLegacyReplay.test.ts — Verify archived browser rule streams, exact generation state and accepted controls against shared cross-language fixtures.

Edited: 2026-11-21T01:30:06-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/fixtures/legacy-replay-vectors.json — Record 15 deterministic archived-rule replay vectors for browser/backend interoperability verification.

Edited: 2026-11-21T01:30:14-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/CompetitiveSolitaireEndpoints.cs — Host Competitive Solitaire queue/session/history routes and background lifecycle in its independently signed game worker.

Edited: 2026-11-21T01:30:14-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/CompetitiveSolitaireBackendRegistration.cs — Host Competitive Solitaire queue/session/history routes and background lifecycle in its independently signed game worker.

Edited: 2026-11-21T01:30:14-06:00 | Computer: Vinnabok | File: games/Solitaire/server/GameWorkerModule.cs — Host Competitive Solitaire queue/session/history routes and background lifecycle in its independently signed game worker.

Edited: 2026-11-21T01:30:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireConfiguration.cs — Host Competitive Solitaire queue/session/history routes and background lifecycle in its independently signed game worker.
Edited: 2026-11-21T01:31:13-06:00 | Computer: VINNABOK | File: games/Asteroids/server/README.md — Document verified Asteroids backend ownership, recovery contracts and shared deployment boundaries.
Edited: 2026-11-21T01:22:28-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackBackend.cs — Complete Blackjack paid-table package ownership, generic wallet integration fixtures, real recovery tests, and packaged worker release contract.
Edited: 2026-11-21T01:26:37-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackTableWalletRecoveryTests.cs — Complete Blackjack paid-table package ownership, generic wallet integration fixtures, real recovery tests, and packaged worker release contract.
Edited: 2026-11-21T01:29:35-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/BlackjackPlatformTests.cs — Complete Blackjack paid-table package ownership, generic wallet integration fixtures, real recovery tests, and packaged worker release contract.
Edited: 2026-11-21T01:21:59-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/Table/BlackjackTableFirestoreEmulatorTests.cs — Complete Blackjack paid-table package ownership, generic wallet integration fixtures, real recovery tests, and packaged worker release contract.
Edited: 2026-11-21T01:30:12-06:00 | Computer: Vinnabok | File: games/Blackjack/game-release.json — Complete Blackjack paid-table package ownership, generic wallet integration fixtures, real recovery tests, and packaged worker release contract.

Edited: 2026-11-21T01:32:05-06:00 | Computer: Vinnabok | File: games/Flappy/tests/FortuneForge.Games.Flappy.Tests/FlappyPaidRunTests.cs — Verify lost wallet-response recovery, one-time debit across rollover, never-admitted old intent refusal, authenticated history, exact replay retries, corrupt outcomes and historical fee/rules compatibility.

Edited: 2026-11-21T01:32:05-06:00 | Computer: Vinnabok | File: games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/HorseFlightPaidRunTests.cs — Verify lost wallet-response recovery, one-time debit across rollover, never-admitted old intent refusal, authenticated history, exact replay retries, corrupt outcomes and historical fee/rules compatibility.
Edited: 2026-11-21T01:32:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Cards/TexasHoldem/Credit/CreditHoldemFirestoreEmulatorTests.cs — Preserve and migrate all 22 production Hold'em Firestore lifecycle checks to signed wallet/roster adapters.
Edited: 2026-11-21T01:32:18-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/Legacy/CreditHoldemFirestoreEmulatorTests.cs — Preserve and migrate all 22 production Hold'em Firestore lifecycle checks to signed wallet/roster adapters.
Edited: 2026-11-21T01:32:19-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/FirestoreTestWallet.cs — Provide a durable atomic fake wallet for preserved production lifecycle integration checks.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: games/Solitaire/src/FortuneForge.Games.Solitaire/SolitaireModule.cs — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/FortuneForge.Games.Solitaire.Tests.csproj — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/ManagedPlayers/SolitaireManagedPlayerPolicyTests.cs — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Solitaire/SolitaireManagedPlayerPolicyTests.cs — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/ManagedPlayers/SolitaireManagedPlayerProgressionTests.cs — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:32:44-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Solitaire/SolitaireManagedPlayerProgressionTests.cs — Centralize Competitive Solitaire identity and move managed-policy verification beside the authoritative game backend.

Edited: 2026-11-21T01:33:57-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Solitaire/SolitaireApiContractTests.cs — Preserve the legacy Solitaire API contract while including the new authenticated free-run recovery read.

Edited: 2026-11-21T01:26:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Activity/GameAnalyticsContracts.cs
  Summary: Use the sole game-neutral analytics port.

Edited: 2026-11-21T01:26:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs
  Summary: Resolve the canonical analytics port in host compatibility setup.

Edited: 2026-11-21T01:33:29-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs
  Summary: Tag Competitive Solitaire as a separately routed package scope.

Edited: 2026-11-21T01:26:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the Competitive Solitaire backend and one neutral analytics port.

Edited: 2026-11-21T01:27:58-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Register the Competitive Solitaire server project.

Edited: 2026-11-21T01:26:49-06:00 | Computer: VINNABOK | File: firestore.indexes.json
  Summary: Index pending Texas Hold'em settlement recovery by status and account membership.

Edited: 2026-11-21T01:31:06-06:00 | Computer: VINNABOK | File: scripts/deploy-game.mjs
  Summary: Stage individual workers without a browser build and reject mixed publishing scope.

Edited: 2026-11-21T01:27:58-06:00 | Computer: VINNABOK | File: scripts/verify-game-navbar.mjs
  Summary: Support a registered backend-only title with an existing host browser route.

Edited: 2026-11-21T01:27:22-06:00 | Computer: VINNABOK | File: games/Asteroids/game-release.json
  Summary: Register the private Asteroids backend without the practice preview.

Edited: 2026-11-21T01:27:22-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document the dedicated Asteroids backend-only staging command.

Edited: 2026-11-21T01:29:38-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletBatchAuthentication.cs
  Summary: Restrict batch permission by game scope and accepted actor effects.

Edited: 2026-11-21T01:29:38-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWalletBatchClient.cs
  Summary: Use the same Texas and Solitaire settlement scope rules as the platform.

Edited: 2026-11-21T01:28:31-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletBatchEndpoints.cs
  Summary: Allow explicit distinct-key batches for each authorized game scope.

Edited: 2026-11-21T01:32:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletBatchFirestoreStore.cs
  Summary: Verify canonical settled wallet batches without permitting audit writes.

Edited: 2026-11-21T01:28:31-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/GameWalletBatchRpcTests.cs
  Summary: Retain signed Texas batch coverage with the shared scoped endpoint.

Edited: 2026-11-21T01:33:29-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletJournal.cs
  Summary: Aggregate ordered signed wallet effects and required prefunding without unchecked arithmetic.

Edited: 2026-11-21T01:33:39-06:00 | Computer: VINNABOK | File: games/tests/FortuneForge.Games.Tests/GameWalletJournalTests.cs
  Summary: Verify ordinal ordering, prefix minima and arithmetic overflow.

Edited: 2026-11-21T01:30:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs
  Summary: Expose one authoritative public-forwarding switch to compatibility schedulers.

Edited: 2026-11-21T01:32:37-06:00 | Computer: VINNABOK | File: games/Wukong/tests/FortuneForge.Games.Wukong.Tests/WukongRoundStoreTests.cs
  Summary: Verify legacy admission handoff with both open and paused new rounds.

Edited: 2026-11-21T01:32:37-06:00 | Computer: VINNABOK | File: games/RainbowRealm/tests/FortuneForge.Games.RainbowRealm.Tests/RainbowRoundStoreTests.cs
  Summary: Verify that open and paused worker starts both defer old admissions.

Edited: 2026-11-21T01:30:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreManagedPlayerPolicy.cs
  Summary: Share canonical managed-player policy checks with administrative controls.

Edited: 2026-11-21T01:30:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/FirestoreAdminManagementStore.cs
  Summary: Read managed-player policy through the shared decoder.

Edited: 2026-11-21T01:30:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/AdminManagementModels.cs
  Summary: Use one effective managed-player policy rule.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Admin/Management/AdminManagementFirestoreTests.cs
  Summary: Verify managed-player defaults and reuse package-backed Solitaire fixtures.

Edited: 2026-11-21T01:26:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/TableRoundAudit.cs
  Summary: Delegate Blackjack table capture to the authoritative package.

Edited: 2026-11-21T01:27:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardFinancialAudit.cs
  Summary: Dispatch Blackjack wallet verification while retaining historical card ledgers.

Edited: 2026-11-21T01:28:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/BlackjackWalletFinancialAudit.cs
  Summary: Verify native and historical Blackjack solo payments and immutable journal.

Edited: 2026-11-21T01:28:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/BlackjackWalletFinancialAuditTests.cs
  Summary: Exercise historical, mixed and native Blackjack audit verification.

Edited: 2026-11-21T01:32:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireConfiguration.cs
  Summary: Compose package-owned Competitive Solitaire and exclusive scheduling.

Edited: 2026-11-21T01:31:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitairePlatformAdapters.cs
  Summary: Use actor-bound wallet and roster leases in the compatibility gateway.

Edited: 2026-11-21T01:31:42-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/SolitaireController.cs
  Summary: Disable local mutations when the signed worker owns the route.

Edited: 2026-11-21T01:33:38-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/SolitaireFinancialAudit.cs
  Summary: Verify signed batch settlement while preserving historical wallet records.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Solitaire/CompetitiveSolitaireTestStore.cs
  Summary: Use correctly scoped canonical wallet and roster fixtures.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Solitaire/CompetitiveSolitaireBotFillFirestoreTests.cs
  Summary: Verify bot fill using the package-owned store and scoped ports.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Solitaire/SolitaireHistoryPaginationFirestoreTests.cs
  Summary: Exercise history pages with the migrated authoritative store.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/RequestEngineJournalFirestoreTests.cs
  Summary: Verify canonical Blackjack and Solitaire request receipts.

Edited: 2026-11-21T01:32:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Activity/AcceptedGameTransactionWitnessTests.cs
  Summary: Bind entitlement publication to a durable wallet receipt.

Edited: 2026-11-21T01:33:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/SolitaireFinancialAuditFirestoreTests.cs
  Summary: Verify historical and signed batch compatibility and read-only replay.
Edited: 2026-11-21T01:35:12-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRunService.cs — Bind paid Asteroids admission to the verified exact request body and test signed retry conflicts across rollover.
Edited: 2026-11-21T01:35:12-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRunEndpoints.cs — Bind paid Asteroids admission to the verified exact request body and test signed retry conflicts across rollover.
Edited: 2026-11-21T01:35:12-06:00 | Computer: VINNABOK | File: games/Asteroids/server/GameWorkerModule.cs — Bind paid Asteroids admission to the verified exact request body and test signed retry conflicts across rollover.
Edited: 2026-11-21T01:35:12-06:00 | Computer: VINNABOK | File: games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/AsteroidsPaidRunTests.cs — Bind paid Asteroids admission to the verified exact request body and test signed retry conflicts across rollover.
Edited: 2026-11-21T01:36:16-06:00 | Computer: VINNABOK | File: games/Asteroids/server/README.md — Document verified raw request digest propagation and intentional unsigned compatibility.

Edited: 2026-11-21T01:36:18-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/CompetitiveSolitaireBackendTests.cs — Verify Competitive Solitaire paid admission, exact replies, crash recovery, legacy reservations/refunds, claims and managed cohort funding.

Edited: 2026-11-21T01:36:18-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/SolitaireTestClients.cs — Verify Competitive Solitaire paid admission, exact replies, crash recovery, legacy reservations/refunds, claims and managed cohort funding.
Edited: 2026-11-21T01:36:28-06:00 | Computer: Vinnabok | File: games/TexasHoldem/src/FortuneForge.Games.TexasHoldem/TexasHoldemModule.cs — Make the pure game descriptor authoritative for the Texas Hold'em runtime identifier and API path.

Edited: 2026-11-21T01:37:15-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/CompetitiveSolitaireEndpointTests.cs — Verify signed legacy competitive routes, trusted display-name admission, private history and exact queued refund retries.
Edited: 2026-11-21T01:38:18-06:00 | Computer: Vinnabok | File: games/SicBo/client/FortuneForge.Games.SicBo.Client/src/useSicBoTable.ts — Reconcile live wallet funds after immutable paid-round recovery while preserving historical outcome.
Edited: 2026-11-21T01:38:18-06:00 | Computer: Vinnabok | File: games/SicBo/client/FortuneForge.Games.SicBo.Client/src/SicBoGame.test.tsx — Reconcile live wallet funds after immutable paid-round recovery while preserving historical outcome.
Edited: 2026-11-21T01:39:00-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/BaccaratRoute.test.tsx — Updated the authenticated route contract check for account-pinned Baccarat transport.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/CandyCarnival/tests/FortuneForge.Games.CandyCarnival.Tests/CandyCarnivalRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/PhantomManor/tests/FortuneForge.Games.PhantomManor.Tests/PhantomManorRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/NordicLegends/tests/FortuneForge.Games.NordicLegends.Tests/NordicLegendsRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/DesertTreasures/tests/FortuneForge.Games.DesertTreasures.Tests/DesertRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.

Edited: 2026-11-21T01:40:13-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs — Added focused package regression proving old host admission stays discoverable when new native rounds are paused.
Edited: 2026-11-21T01:40:14-06:00 | Computer: Vinnabok | File: games/Roulette/client/FortuneForge.Games.Roulette.Client/src/useRouletteTable.ts — Updated the controller contract comment for gateway-owned durable command replay.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: games/Flappy/game-release.json — Declare the complete game-owned private backend without changing catalogue visibility or activating production forwarding.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Flappy/ArcadeCompetitionFlappyPaidEntryService.cs — Delegate legacy paid seed and run identity creation to the authoritative game package while retaining the existing host adapter contract.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: games/Flappy/server/README.md — Document actual hidden-game paid/free ownership, immutable recovery, historical compatibility, verification and separate production activation.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: games/HorseFlight/game-release.json — Declare the complete game-owned private backend without changing catalogue visibility or activating production forwarding.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/HorseFlight/ArcadeCompetitionHorseFlightPaidEntryService.cs — Delegate legacy paid seed and run identity creation to the authoritative game package while retaining the existing host adapter contract.

Edited: 2026-11-21T01:45:14-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/README.md — Document actual hidden-game paid/free ownership, immutable recovery, historical compatibility, verification and separate production activation.
Edited: 2026-11-21T01:45:55-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/MinesRoute.test.tsx — Verify paid-route account binding survives URL modes and changes with the authenticated account.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/HeartsGameService.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/HeartsContracts.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/HeartsWorker.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.JoinBatch.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.Progression.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsContracts.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsProjection.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsRoundAudit.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-10-27T04:15:21-05:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Onboarding/HeartsOnboardingStore.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/ManagedPlayers/HeartsHousePlayers.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/ManagedPlayers/HeartsManagedActionPolicy.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:47:24-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/ManagedPlayers/HeartsManagedPlayers.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:44:47-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/FortuneForge.Games.Hearts.Server.csproj — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:46:10-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackFinancialProof.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:46:10-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloStore.FinancialProof.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T01:46:10-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.FinancialProof.cs — Extract game-owned domain policies or immutable wallet evidence gathering, reusing shared infrastructure.
Edited: 2026-11-21T15:55:23-06:00 | Computer: Vinnabok | File: FortuneForge.Client/src/app/routes/FlappyRoute.tsx | Bound arcade route gateways to the original authenticated account and reset sessions on account replacement.
Edited: 2026-11-21T15:55:23-06:00 | Computer: Vinnabok | File: FortuneForge.Client/src/app/routes/HorseFlightRoute.tsx | Bound arcade route gateways to the original authenticated account and reset sessions on account replacement.
Edited: 2026-11-21T15:55:23-06:00 | Computer: Vinnabok | File: FortuneForge.Client/src/app/routes/FlappyRoute.test.tsx | Bound arcade route gateways to the original authenticated account and reset sessions on account replacement.
Edited: 2026-11-21T15:55:23-06:00 | Computer: Vinnabok | File: FortuneForge.Client/src/app/routes/HorseFlightRoute.test.tsx | Bound arcade route gateways to the original authenticated account and reset sessions on account replacement.
Edited: 2026-11-21T15:56:39-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratModels.cs — Deferred Baccarat shoe creation until after durable admission and paused-start checks while retaining existing store adapters.
Edited: 2026-11-21T15:56:39-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratFirestoreStore.cs — Deferred Baccarat shoe creation until after durable admission and paused-start checks while retaining existing store adapters.
Edited: 2026-11-21T15:56:43-06:00 | Computer: Vinnabok | File: FortuneForge.Client/src/app/routes/FlappyHorseFlightRoutes.accountBinding.test.tsx | Added actual route regressions for captured account identity, exact paid bodies and keys, legacy empty-body recover-only requests, terminal replay and bearer/cookie fallback.
Edited: 2026-11-21T15:56:48-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AsteroidsRoute.test.tsx | Replaced stale route constructor assertion with original-account paid-intent coverage across bearer/cookie fallback and a route account switch.
Edited: 2026-11-21T15:57:24-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Solitaire/CompetitiveSolitaireTestStore.cs — Compose package tests with real generic wallet and roster adapters outside production configuration.
Edited: 2026-11-21T15:57:24-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/SecurityAudit/SolitaireFinancialAuditFirestoreTests.cs — Use test-only Competitive Solitaire composition with paid admission enabled.
Edited: 2026-11-21T15:57:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/account/services/accountsApi.ts — Added an account-bound game fetcher that preserves request options and pins the initiating account across authentication retries.
Edited: 2026-11-21T15:57:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AsteroidsRoute.tsx — Reused the shared account-bound fetcher for Asteroids competition requests.

Edited: 2026-11-21T15:56:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Baccarat/BaccaratController.cs
  Summary: Keep public routing and isolate local practice while paid requests require the authoritative worker.

Edited: 2026-11-21T15:56:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Baccarat/BaccaratFirestoreStore.cs
  Summary: Remove the duplicate host-owned paid store after transferring authority to the Baccarat package.

Edited: 2026-11-21T15:56:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Baccarat/BaccaratModels.cs
  Summary: Remove duplicate Baccarat contracts and service logic already owned by the game package.

Edited: 2026-11-21T15:56:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Baccarat/PracticeBaccaratStore.cs
  Summary: Remove the duplicate host practice store and use the package store.

Edited: 2026-11-21T15:56:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/BaccaratOutcomeAudit.cs
  Summary: Remove the duplicate host outcome verifier and use the package verifier.

Edited: 2026-11-21T15:56:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Baccarat/BaccaratContractTests.cs
  Summary: Check fail-closed paid fallback and preserve the practice balance and stake contract.

Edited: 2026-11-21T15:56:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Baccarat/BaccaratFirestoreEmulatorTests.cs
  Summary: Exercise the package store with canonical wallet settlement and verify V1 replay produces no additional debit.

Edited: 2026-11-21T15:56:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/PaidCardFinancialAuditTests.cs
  Summary: Move the Baccarat source-tamper fixture to a canonical signed-wallet settlement.

Edited: 2026-11-21T15:57:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs — Added non-secret signed wallet response diagnostics after integrated suite exposed common503 state-read failures.
Edited: 2026-11-21T15:58:10-06:00 | Computer: Vinnabok | File: games/Asteroids/tests/worker-smoke.mjs | Added isolated selected-worker local/signed raw-byte digest and free lifecycle smoke with unconfigured wallet and deterministic cleanup.

Edited: 2026-11-21T15:55:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/GameWalletSettlementEndpoints.cs
  Summary: Keep core wallet route registration independent of competition services.

Edited: 2026-11-21T15:55:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Wallet/ArcadeCompetitionWalletEndpoints.cs
  Summary: Expose explicit registration of the signed competition wallet routes.

Edited: 2026-11-21T15:55:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/ArcadeCompetitionWalletRpcTests.cs
  Summary: Mount the signed competition wallet routes with their exact test dependencies.

Edited: 2026-11-21T15:55:59-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs
  Summary: Mount the competition wallet explicitly in the platform host.

Edited: 2026-11-21T15:56:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs
  Summary: Compose the canonical auditing port and scoped Craps wallet, and retire the Competitive Solitaire host runtime.

Edited: 2026-11-21T15:56:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj
  Summary: Reference the actual Baccarat and Texas Hold'em backend libraries.

Edited: 2026-11-21T15:56:15-06:00 | Computer: VINNABOK | File: FortuneForge.slnx
  Summary: Include each standalone paid slot backend and the Baccarat server and tests.

Edited: 2026-11-21T15:57:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionController.cs
  Summary: Keep an arcade retry tied to its frozen competition window and authenticated account.

Edited: 2026-11-21T15:57:40-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionControllerTests.cs
  Summary: Verify that paid arcade recovery cannot place another legacy-host wager.

Edited: 2026-11-21T15:53:53-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreWalletEvidence.cs
  Summary: Decode canonical immutable ledger evidence for historical game audit.

Edited: 2026-11-21T15:54:13-06:00 | Computer: VINNABOK | File: games/tests/FortuneForge.Games.Tests/Firestore/FirestoreWalletEvidenceTests.cs
  Summary: Cover valid and malformed legacy ledger evidence.

Edited: 2026-11-21T01:48:18-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameSecurityEventSink.cs
  Summary: Expose the canonical game event and free-play evidence writer contracts.

Edited: 2026-11-21T15:56:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityEventSink.cs
  Summary: Use the neutral game event contracts for the host Firestore writer.

Edited: 2026-11-21T15:56:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightGameService.cs
  Summary: Use the authoritative game auditing interface.

Edited: 2026-11-21T15:56:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeGameService.cs
  Summary: Use the authoritative game auditing interface.

Edited: 2026-11-21T01:37:01-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameWorkerAuthentication.cs
  Summary: Capture exact emulator body bytes while retaining signed-only authority in hosted mode.

Edited: 2026-11-21T01:36:02-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/Program.cs
  Summary: Register exact command hashing explicitly in local emulator mode.

Edited: 2026-11-21T01:37:58-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/GameWorkerLocalDigestTests.cs
  Summary: Exercise exact local request digests and hosted signature boundaries.

Edited: 2026-11-21T01:44:15-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/RoundAuditFinancialProof.cs
  Summary: Append immutable ledger and wallet proof to game-owned round audit evidence.

Edited: 2026-11-21T01:45:07-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions.Tests/FortuneForge.Games.Abstractions.Tests.csproj
  Summary: Register the shared audit proof tests.

Edited: 2026-11-21T01:45:07-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions.Tests/RoundAuditFinancialProofTests.cs
  Summary: Check digest preservation, mixed ledger proof, and explicit incomplete history.

Edited: 2026-11-21T01:45:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardFinancialAudit.cs
  Summary: Verify scoped signed Baccarat settlements and use canonical legacy lookup helpers.

Edited: 2026-11-21T01:45:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/HorseFlight/HorseFlightContractTests.cs
  Summary: Compile route contract checks against the published Horse Flight types.

Edited: 2026-11-21T01:45:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/LiarsDice/LiarsDiceGameServiceTests.cs
  Summary: Use the canonical package bid response type.

Edited: 2026-11-21T01:46:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Blackjack/BlackjackPlatformTests.cs
  Summary: Construct package Blackjack with the authoritative population policy and real wallet reader.

Edited: 2026-11-21T01:46:27-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Admin/Management/AdminManagementFirestoreTests.cs
  Summary: Use actor-scoped wallet and roster ports for current card package fixtures.

Edited: 2026-11-21T01:46:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireConfiguration.cs
  Summary: Retire local competitive store registration and background scheduling.

Edited: 2026-11-21T01:48:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Cards/Solitaire/SolitaireController.cs
  Summary: Preserve browser routes while requiring the game worker for competitive play.

Edited: 2026-11-21T01:39:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Craps/CrapsCompatibilityWallet.cs
  Summary: Adapt the verified wallet scope to the package-owned Craps store.

Edited: 2026-11-21T01:39:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Craps/CrapsController.cs
  Summary: Return exact retry and conflict outcomes and fence the local compatibility path.

Edited: 2026-11-21T01:40:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/CrapsWalletFinancialAudit.cs
  Summary: Verify stored signed wallet receipts and actual ledger entries without writes.

Edited: 2026-11-21T01:40:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/GameSecurityIntegrityAuditor.cs
  Summary: Link package Craps replay to read-only platform financial proof.

Edited: 2026-11-21T01:39:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/Craps/FirestorePaidCrapsStoreTests.cs
  Summary: Exercise authoritative Craps with canonical wallet, legacy state and recovery fixtures.

Edited: 2026-11-21T01:40:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/PaidCrapsReplayAuditTests.cs
  Summary: Preserve legacy replay and native ledger tamper detection.

Edited: 2026-11-21T01:47:14-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.cs
  Summary: Exercise real signed gateway and wallet retry for Dragon Tiger and Andar Bahar.

Edited: 2026-11-21T01:42:26-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Players/FirestoreAdminPlayersStore.cs
  Summary: Bind native signed ledger entries to their actual Dragon Tiger or Andar Bahar round.

Edited: 2026-11-21T01:42:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/AndarBahar/AndarBaharHistoryIntegrationTests.cs
  Summary: Use the genuine signed wallet and verify administrative history links.

Edited: 2026-11-21T01:39:24-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Firestore/FirestoreFieldCodec.cs
  Summary: Encode authoritative UTC dates using the canonical Firestore timestamp shape.

Edited: 2026-11-21T01:39:36-06:00 | Computer: VINNABOK | File: games/tests/FortuneForge.Games.Tests/Firestore/FirestoreFieldCodecTests.cs
  Summary: Test analytics outbox timestamps and rejection of ambiguous local dates.

Edited: 2026-11-21T01:41:26-06:00 | Computer: VINNABOK | File: package-lock.json
  Summary: Declare Roulette's shared browser infrastructure dependency.

Edited: 2026-11-21T01:41:26-06:00 | Computer: VINNABOK | File: scripts/game-worker-release.mjs
  Summary: Register scoped table and card game workers.

Edited: 2026-11-21T01:41:26-06:00 | Computer: VINNABOK | File: docs/GAME_DEPLOYMENT.md
  Summary: Document the app-hosted Asteroids competition frontend and separately staged worker.

Edited: 2026-11-21T01:37:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Snake/SnakeController.cs
  Summary: Use one import for the verified pre-binding request fingerprint.

Edited: 2026-11-21T01:37:29-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCrapsReplayAudit.cs
  Summary: Remove the duplicate host auditor in favor of the game package.
Edited: 2026-11-21T15:59:34-06:00 | Computer: Vinnabok | File: games/Asteroids/server/README.md | Reconciled final34-case digest verification, account-bound route regression, selected Release build and isolated local/signed smoke; retained explicit shared integration gates.
Edited: 2026-11-21T15:59:46-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemFinancialProof.cs — Freeze signed and legacy wallet evidence into protected House Hold'em completions while preserving old prepared command hashes.
Edited: 2026-11-21T15:59:46-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemPreparedCommand.cs — Freeze signed and legacy wallet evidence into protected House Hold'em completions while preserving old prepared command hashes.
Edited: 2026-11-21T15:59:46-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemPreparedStore.cs — Freeze signed and legacy wallet evidence into protected House Hold'em completions while preserving old prepared command hashes.
Edited: 2026-11-21T15:59:46-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemRoundReporting.cs — Freeze signed and legacy wallet evidence into protected House Hold'em completions while preserving old prepared command hashes.
Edited: 2026-11-21T16:00:14-06:00 | Computer: Vinnabok | File: games/HouseHoldem/server/FortuneForge.Games.HouseHoldem.Server/HouseHoldemFirestoreStore.cs — Reuse the authoritative game command wallet request and shared receipt verifier for recovery/proof consistency.
Edited: 2026-11-21T16:00:27-06:00 | Computer: Vinnabok | File: games/Baccarat/INTEGRATION.md — Recorded the89-test backend pass and clean selected worker build, distinguishing injected-wallet tests from pending real signed integration.
Edited: 2026-11-21T16:01:10-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/LiarsDice/LiarsDiceSignedRosterIntegrationTests.cs | Added real signed worker/roster integration coverage for durable retries, lease isolation, completion release and failed admission recovery.
Edited: 2026-11-21T16:01:29-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemFinancialProof.cs — Gather immutable legacy and signed atomic-batch funding evidence for completed per-hand and escrow Hold'em rounds.
Edited: 2026-09-30T19:04:30-05:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsGameServiceTests.cs | Extracted Hearts domain ownership and durable free/trial state with exact command recovery.
Edited: 2026-11-21T16:01:20-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsFreePersistenceTests.cs | Extracted Hearts domain ownership and durable free/trial state with exact command recovery.
Edited: 2026-11-21T16:00:39-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/FortuneForge.Games.Hearts.Tests.csproj | Extracted Hearts domain ownership and durable free/trial state with exact command recovery.
Edited: 2026-11-21T15:58:46-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/HeartsGameService.Persistence.cs | Extracted Hearts domain ownership and durable free/trial state with exact command recovery.
Edited: 2026-11-21T16:00:04-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/FirestoreHeartsFreeStore.cs | Extracted Hearts domain ownership and durable free/trial state with exact command recovery.
Edited: 2026-11-21T16:02:22-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemBackend.cs — Freeze protected completion finance evidence and reject existing game/legacy financial create targets before wallet effects.
Edited: 2026-11-21T16:02:22-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemMutation.cs — Freeze protected completion finance evidence and reject existing game/legacy financial create targets before wallet effects.
Edited: 2026-11-21T16:02:22-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemMutationJournal.cs — Freeze protected completion finance evidence and reject existing game/legacy financial create targets before wallet effects.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Baccarat/server/FortuneForge.Games.Baccarat.Server/BaccaratEndpoints.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Baccarat/tests/FortuneForge.Games.Baccarat.Tests/BaccaratWalletTests.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteGameService.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/FirestoreRouletteService.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RouletteEndpoints.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:02:38-06:00 | Computer: Vinnabok | File: games/Roulette/tests/FortuneForge.Games.Roulette.Tests/RoulettePersistenceTests.cs — Kept persisted decode failures on retryable recovery responses, with signed-route corruption regressions, while preserving live request validation.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.FinancialProof.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Match.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Financial.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Claims.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/SolitaireFinancialPlan.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/CompetitiveSolitaireRoundAudit.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/CompetitiveSolitaireBackendTests.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:03:44-06:00 | Computer: Vinnabok | File: games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/ManagedPlayers/SolitaireManagedPlayerPolicyTests.cs — Preserve immutable competitive entry and separate claim financial evidence; verify replay and legacy compatibility.
Edited: 2026-11-21T16:04:11-06:00 | Computer: Vinnabok | File: games/HouseHoldem/tests/FortuneForge.Games.HouseHoldem.Tests/HouseHoldemRoundTests.cs — Verify protected mixed legacy/signed financial evidence survives source ledger mutation and lost-response recovery.
Edited: 2026-11-21T16:04:11-06:00 | Computer: Vinnabok | File: games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/CreditHoldemPaidRecoveryTests.cs — Verify protected mixed legacy/signed financial evidence survives source ledger mutation and lost-response recovery.
Edited: 2026-11-21T16:04:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj | Registered the standalone Liar's Dice backend for signed integration testing.
Edited: 2026-11-21T16:04:52-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Registered the standalone Liar's Dice backend for signed integration testing.
Edited: 2026-11-21T16:04:52-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Registered the standalone Liar's Dice backend for signed integration testing.
Edited: 2026-11-21T16:05:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarFinancialProof.cs — Freeze original opening ledger or signed receipt and protect full Casino War completion financial evidence.
Edited: 2026-11-21T16:05:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarPreparedAction.cs — Freeze original opening ledger or signed receipt and protect full Casino War completion financial evidence.
Edited: 2026-11-21T16:05:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarFirestoreStore.cs — Freeze original opening ledger or signed receipt and protect full Casino War completion financial evidence.
Edited: 2026-11-21T16:05:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/server/FortuneForge.Games.CasinoWar.Server/CasinoWarRoundReporting.cs — Freeze original opening ledger or signed receipt and protect full Casino War completion financial evidence.
Edited: 2026-11-21T16:05:20-06:00 | Computer: Vinnabok | File: games/CasinoWar/tests/FortuneForge.Games.CasinoWar.Tests/CasinoWarBackendTests.cs — Freeze original opening ledger or signed receipt and protect full Casino War completion financial evidence.
Edited: 2026-11-21T16:06:09-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedPuzzleWorkerIntegrationTests.cs — Add signed puzzle gateway loss/retry/replica/cutover proofs and real Snake controller raw-body receipt regression.
Edited: 2026-11-21T16:07:02-06:00 | Computer: Vinnabok | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.cs — Mark new claimable results with their package owner and original immutable completion.
Edited: 2026-11-21T16:07:02-06:00 | Computer: Vinnabok | File: games/Solitaire/INTEGRATION.md — Document package lifecycle, legacy shard serialization and immutable financial evidence integration.
Edited: 2026-11-21T16:07:02-06:00 | Computer: Vinnabok | File: games/CasinoWar/INTEGRATION.md — Document signed action ownership, recovery and protected original opening evidence.
Edited: 2026-11-21T16:07:13-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj | Linked the two puzzle server packages for signed integration fixtures.
Edited: 2026-11-21T16:07:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitaireConfiguration.cs | Removed the unused in-process Competitive Solitaire factory after moving test composition into test support.
Edited: 2026-11-21T16:07:21-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Solitaire/CompetitiveSolitairePlatformAdapters.cs | Removed the unused in-process Competitive Solitaire wallet and roster adapters.
Edited: 2026-11-21T16:07:09-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/RoundAuditFinancialProof.cs — Keep optional protected wallet batch evidence and compare its ordered effects and account receipts structurally.
Edited: 2026-11-21T16:07:47-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions.Tests/RoundAuditFinancialProofTests.cs — Cover compatible protected JSON, resealing, and conflicting duplicate wallet batch evidence.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/FortuneForge.Server.csproj — Reference the canonical Roulette server project.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.slnx — Register the canonical Roulette server and test projects.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs — Remove host Roulette state and lifecycle registrations.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Bots/Roulette/RouletteManagedPlayers.cs — Remove the duplicate host managed-player bet implementation.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Roulette/RouletteContracts.cs — Remove duplicate host result and request contracts.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Roulette/RouletteGameService.cs — Remove the obsolete process-local Roulette authority.
Edited: 2026-11-21T16:06:28-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Roulette/RouletteTableWorker.cs — Remove the obsolete host Roulette scheduler.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/Roulette/RouletteController.cs — Keep the public routes and limits for signed forwarding and fail closed locally.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/GameCompletionAnalyticsTests.cs — Exercise the canonical Roulette service for per-bettor completion analytics.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/InMemorySecurityJournalTests.cs — Verify the canonical Roulette snapshots through the existing security event fixture.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/CasinoPracticeActionReplayAuditTests.cs — Replay canonical Roulette audit snapshots through the existing tamper fixture.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/Roulette/RouletteSecurityEventRecorder.cs — Adapt canonical Roulette snapshots to the shared security fixture sink.
Edited: 2026-11-21T16:06:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Admin/Management/AdminManagementFirestoreTests.cs — Exercise the durable Roulette store with the shared managed-player setting.
Edited: 2026-11-21T16:07:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PracticeOutcomeAudit.cs — Delegate independent Roulette outcome verification to its package.
Edited: 2026-11-21T16:07:37-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Games/Roulette/RouletteGatewayContractTests.cs — Cover private-history routing and unforwarded 503 responses.
Edited: 2026-11-21T16:07:53-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AsteroidsRoute.test.tsx | Verified shared account-bound helper preserves gateway across same-account refresh and exact recovery query, body and key across account switch and bearer/cookie fallback.
Edited: 2026-11-21T16:09:33-06:00 | Computer: Vinnabok | File: games/Solitaire/game-release.json — Declare the standalone competitive package as the authoritative backend after baseline package and worker validation.
Edited: 2026-11-21T16:09:38-06:00 | Computer: Vinnabok | File: games/HiLo/client/FortuneForge.Games.HiLo.Client/src/httpHiLoGateway.test.ts — Return a fresh HTTP response for each account-bound retry in the regression fixture.
Edited: 2026-11-21T16:09:31-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEngine.cs | Reused the package-owned carry-wager bounds when replaying accepted legacy classic free rounds.
Edited: 2026-11-21T16:09:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Bonuses/OlympusTumbleDemo.cs | Reused the package-owned carry-wager bounds when replaying accepted legacy classic free rounds.
Edited: 2026-11-21T16:09:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Spins/SpinService.Core.cs | Reused the package-owned carry-wager bounds when replaying accepted legacy classic free rounds.
Edited: 2026-11-21T16:09:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs | Reused the package-owned carry-wager bounds when replaying accepted legacy classic free rounds.
Edited: 2026-11-21T16:10:13-06:00 | Computer: Vinnabok | File: games/Craps/tests/FortuneForge.Games.Craps.Tests/CrapsWalletTests.cs — Fix standalone signed-worker test compilation and Hosting reference.
Edited: 2026-11-21T16:10:13-06:00 | Computer: Vinnabok | File: games/Craps/tests/FortuneForge.Games.Craps.Tests/FortuneForge.Games.Craps.Tests.csproj — Fix standalone signed-worker test compilation and Hosting reference.
Edited: 2026-11-21T16:10:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionController.cs — Registered arcade run status as a worker-owned public GET and retained a structured unavailable response when forwarding is disabled.
Edited: 2026-11-21T16:10:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionControllerTests.cs — Verified the public run status GET route and its worker-required fallback for supported games and periods.
Edited: 2026-11-21T16:05:38-06:00 | Computer: Vinnabok | File: games/Hearts/server/GameWorkerModule.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:05:38-06:00 | Computer: Vinnabok | File: games/Hearts/server/GameComputeModule.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:09:04-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Free/HeartsFreeEndpoints.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:10:14-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsTrialTests.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:10:14-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsPracticeLifecycleTests.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:10:24-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsHousePlayersTests.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:10:37-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsOnboardingFirestoreTests.cs | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:11:32-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HeartsRoute.tsx | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:11:59-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HeartsRoute.accountIsolation.test.tsx | Added durable Hearts worker routing, private lifecycle coverage or account-scoped client recovery.
Edited: 2026-11-21T16:12:46-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/httpLiarsDiceGateway.ts | Recovered matching direct legacy commands through validated GET, remembered explicit match reads, and covered durable replay plus pending-command safety.
Edited: 2026-11-21T16:12:46-06:00 | Computer: Vinnabok | File: games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/httpLiarsDiceGateway.test.ts | Recovered matching direct legacy commands through validated GET, remembered explicit match reads, and covered durable replay plus pending-command safety.
Edited: 2026-11-21T16:12:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs — Added isolated real gateway, game worker, and signed wallet coverage for arcade paid admission, status recovery, exact body and account, and rollover.
Edited: 2026-11-21T16:13:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SnakeRoute.tsx | Bound dedicated Snake/Liar routes to the captured account and verified bearer/cookie retry isolation with exact pending commands.
Edited: 2026-11-21T16:13:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/LiarsDiceRoute.tsx | Bound dedicated Snake/Liar routes to the captured account and verified bearer/cookie retry isolation with exact pending commands.
Edited: 2026-11-21T16:13:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SnakeLiarsDiceRoutes.accountBinding.test.tsx | Bound dedicated Snake/Liar routes to the captured account and verified bearer/cookie retry isolation with exact pending commands.
Edited: 2026-11-21T16:12:56-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HeartsRoute.test.tsx | Updated existing route boundary assertion for account-scoped durable Hearts recovery.
Edited: 2026-11-21T16:13:26-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Registered the authoritative Three Card Poker table package and delegated its historical table audit to that package.
Edited: 2026-11-21T16:13:26-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Registered the authoritative Three Card Poker table package and delegated its historical table audit to that package.
Edited: 2026-11-21T16:13:26-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Registered the authoritative Three Card Poker table package and delegated its historical table audit to that package.
Edited: 2026-11-21T16:13:26-06:00 | Computer: Vinnabok | File: FortuneForge.Server/RoundAudit/TableRoundAudit.cs | Registered the authoritative Three Card Poker table package and delegated its historical table audit to that package.
Edited: 2026-11-21T16:13:58-06:00 | Computer: Vinnabok | File: games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/BaccaratGame.tsx — Keep immutable receipt funds separate from refreshed current wallet funds and block new wagers until refresh.
Edited: 2026-11-21T16:13:58-06:00 | Computer: Vinnabok | File: games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/BaccaratGame.test.tsx — Keep immutable receipt funds separate from refreshed current wallet funds and block new wagers until refresh.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.Progression.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.JoinBatch.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.Journal.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsMutation.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsFinancialProof.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:08-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsEndpoints.cs - Move paid Hearts lifecycle to game-owned frozen atomic wallet staging, immutable replies and protected funding proof; preserve legacy state and recover pending settlements.
Edited: 2026-11-21T16:14:38-06:00 | Computer: Vinnabok | File: games/Roulette/INTEGRATION.md — Record package verification, signed lifecycle and remaining real gateway/platform proof.
Edited: 2026-11-21T16:14:38-06:00 | Computer: Vinnabok | File: games/Craps/INTEGRATION.md — Record package verification, signed lifecycle and remaining real gateway/platform proof.
Edited: 2026-11-21T16:14:36-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedFreeSolitaireWorkerIntegrationTests.cs — Verify real public signed Free Solitaire gateway routing, retries, ownership, and durable result recovery.
Edited: 2026-11-21T16:15:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs — Keep signed Dragon Tiger and Andar Bahar recovery routed during admission pauses, require their workers for paid requests, and validate development practice markers.
Edited: 2026-11-21T16:14:31-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs — Cover signed table worker selection during admission pauses and exact practice routing.
Edited: 2026-11-21T16:15:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.cs — Exercise paid table recovery with paused admissions, absent rollout opt-in or worker binding, and malformed practice headers.
Edited: 2026-11-21T16:15:31-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/GameWalletBatchAuthentication.cs | Scoped signed Hearts batch wallet access to actor debits and bounded multi-account credits.
Edited: 2026-11-21T16:15:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Accounts/GameWalletBatchRpcTests.cs | Scoped signed Hearts batch wallet access to actor debits and bounded multi-account credits.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Bots/ThreeCardPoker/ThreeCardPokerManagedPlayerTests.cs — Retain shared profile compatibility test; move game strategy tests and retire unused local supply test.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/ThreeCardPoker/ThreeCardPokerTableFirestoreEmulatorTests.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/ThreeCardPoker/ThreeCardPokerTableStateTests.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/ThreeCardPoker/ThreeCardPokerManagedActionPolicy.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/ThreeCardPoker/ThreeCardPokerManagedPlayers.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/ThreeCardPoker/ThreeCardPokerManagedSeat.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/ThreeCardPoker/ThreeCardPokerManagedTableState.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/FirestoreThreeCardPokerTableStore.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/InMemoryThreeCardPokerTableStore.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerMoney.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableConfiguration.cs — Remove host table state and deadline worker ownership; retain no-op startup compatibility shim.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableController.cs — Retain compatibility auth facade with package status and fail-closed missed forwarding.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableCoordinator.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableModels.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableService.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/ThreeCardPoker/Table/ThreeCardPokerTableStore.cs — Remove duplicate host game implementation or migrated tests; package owns authoritative behavior.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/contracts.ts — Expose optional authoritative session recovery result for practice and paid modes.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/HeartsGame.recovery.test.tsx — Verify recovery blocks fresh setup and resumes saved trial or uncertain card without starting another hand.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/HeartsGame.tsx — Restore saved session before setup and recover uncertain practice commands before further actions.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/httpHeartsGateway.recovery.test.ts — Cover exact restart retries for every command, authentication expiry, account isolation, and definitive errors.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/httpHeartsGateway.ts — Journal exact per-account commands and recover them before fresh session or onboarding with account guard headers.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/game-release.json — Select the dedicated packaged game worker backend.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.FinancialProof.cs — Use shared legacy decoder and immutable proof envelope for original ledger and verified signed receipts.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.Operations.cs — Freeze prior financial proof before wallet calls and preserve old pending hashes during recovery.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.Persistence.cs — Write exact wallet settlement references with game ledger and protect completed captures.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/ThreeCardPokerTableRoundAudit.cs — Keep authoritative Three Card Poker capture and rules in its package.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/ThreeCardPokerBackend.cs — Own protected evidence contract and signed worker registration.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/server/GameComputeModule.cs — Remove superseded compute module; packaged worker preserves existing compute routes.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerHttpTests.cs — Verify signed auth, trusted player name, feature pauses, legacy routes, and explicit DELETE body.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerManagedPlayerTests.cs — Move original managed strategy and hidden-information independence coverage into package.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerRecoveryTests.cs — Verify available and archived legacy paid hands settle once with immutable financial anchors.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerTableFirestoreEmulatorTests.cs — Verify protected completion ledger IDs, prior operation hashes, and exact account receipts.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerBackend.cs — Define game-owned backend identity and protected financial evidence contract.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerFirestoreStore.cs — Gather opening evidence before settlement and protect completion with actual receipts.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerFirestoreStore.FinancialProof.cs — Gather legacy or signed opening evidence through shared decoders and seal exact wallet proof.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerPreparedOperation.cs — Freeze optional financial evidence while preserving pre-upgrade command hashes.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerRoundCodec.cs — Recover original accepted deal for old pending draw evidence without changing its frozen hash.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/FortuneForge.Games.VideoPoker.Server/VideoPokerRoundRecording.cs — Attach immutable wallet financial proof before shared completion recording.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/server/GameWorkerModule.cs — Reuse authoritative game backend identifier.
Edited: 2026-11-21T16:16:18-06:00 | Computer: Vinnabok | File: games/VideoPoker/tests/FortuneForge.Games.VideoPoker.Tests/VideoPokerPaidRoundTests.cs — Verify exact protected receipts, archived legacy wagers, and pre-upgrade pending draw recovery.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsTakeoverFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsDeadlineFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsSettlementTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsTestBank.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/PaidHeartsFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/PaidHeartsTakeoverFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/PaidHeartsDeadlineFirestoreTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/PaidHeartsSettlementTests.cs - Move dedicated paid Hearts suites to game package and replace host matcher/wallet dependencies with remote roster and durable signed-bank test double.
Edited: 2026-11-21T16:16:39-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs — Extended signed arcade coverage to legacy admission fencing, terminal replay and history, and one real refund across a lost payout reply.
Edited: 2026-11-21T16:17:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs — Registered the concrete paid-run adapters needed by each package service in the integration fixture.
Edited: 2026-11-21T16:19:05-06:00 | Computer: Vinnabok | File: games/TexasHoldem/INTEGRATION.md - Record verified package ownership and local sign-off, or test immutable lost-response/historical paid recovery.
Edited: 2026-11-21T16:19:05-06:00 | Computer: Vinnabok | File: games/HouseHoldem/INTEGRATION.md - Record verified package ownership and local sign-off, or test immutable lost-response/historical paid recovery.
Edited: 2026-11-21T16:19:05-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsRecoveryTests.cs - Record verified package ownership and local sign-off, or test immutable lost-response/historical paid recovery.
Edited: 2026-11-21T16:19:09-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs — Covered an oversized valid Horse Flight replay through the public gateway while keeping the default 32 KiB Kestrel cap for other routes.
Edited: 2026-11-21T16:18:30-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Security/GameAccountBinding.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:18:30-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:18:30-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Controllers/SlotsController.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:19:09-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionController.cs | Reused expected-account validation and applied the package-owned size limit to Horse Flight replay routes.
Edited: 2026-11-21T16:18:30-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Snake/SnakeController.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:18:30-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/LiarsDice/LiarsDiceController.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:18:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/DropMerge/DropMergeController.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:18:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightController.cs | Used the same expected-account validation in the signed gateway and compatible host routes.
Edited: 2026-11-21T16:19:09-06:00 | Computer: Vinnabok | File: games/HorseFlight/src/FortuneForge.Games.HorseFlight/HorseFlightRunContracts.cs | Kept the larger Horse Flight replay limit under the game package and removed the shared worker exception.
Edited: 2026-11-21T16:19:09-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/GameWorkerModule.cs | Kept the larger Horse Flight replay limit under the game package and removed the shared worker exception.
Edited: 2026-11-21T16:19:09-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.PackagedGame.Server/Program.cs | Kept the larger Horse Flight replay limit under the game package and removed the shared worker exception.
Edited: 2026-11-21T16:19:49-06:00 | Computer: Vinnabok | File: games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client/src/useAndarBaharTable.ts — Keep recovered receipt balances historical, refresh live funds after confirmation, and retain recovery until that read succeeds.
Edited: 2026-11-21T16:19:49-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/DragonTigerGame.tsx — Keep recovered receipt balances historical, refresh live funds after confirmation, and retain recovery until that read succeeds.
Edited: 2026-11-21T16:19:44-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/HeartsBackend.cs | Centralized Hearts backend owner/proof metadata while reusing pure module identifiers and API path.
Edited: 2026-11-21T16:20:16-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsRoundAudit.cs - Use shared game-owned Hearts backend identity for the preserved paid settlement capture.
Edited: 2026-11-21T16:20:31-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsContracts.cs - Preserve package-owned paid contracts and projection during backend identity consolidation.
Edited: 2026-11-21T16:20:31-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/PaidHeartsProjection.cs - Preserve package-owned paid contracts and projection during backend identity consolidation.
Edited: 2026-11-21T16:20:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/BlackjackWalletFinancialAudit.cs — Audit protected solo and table command and ledger links from immutable round proof while retaining legacy fallback.
Edited: 2026-11-21T16:20:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/BlackjackWalletFinancialAuditTests.cs — Exercise protected solo and table auditing with stale metadata, unavailable historical evidence, and tampered ledger links.
Edited: 2026-11-21T16:22:09-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/FlappyRoute.tsx | Refresh paid flight balances in the mounted same-account session and preserve the Horse Flight adapter through balance changes.
Edited: 2026-11-21T16:22:09-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HorseFlightRoute.tsx | Refresh paid flight balances in the mounted same-account session and preserve the Horse Flight adapter through balance changes.
Edited: 2026-11-21T16:22:09-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/horseFlightCompetitionGateway.ts | Refresh paid flight balances in the mounted same-account session and preserve the Horse Flight adapter through balance changes.
Edited: 2026-11-21T16:22:14-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.cs — Reuse the signed card smoke harness for the canonical Baccarat worker package.
Edited: 2026-11-21T16:22:14-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Baccarat.cs — Test the public MVC Baccarat route through signed worker and wallet, including a lost commit reply, owner-only reads and durable audit evidence.
Edited: 2026-11-21T16:22:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs | Reused the verified signed slot harness for six older titles, including the Wukong storage alias and Olympus classic/tumble history.
Edited: 2026-11-21T16:23:18-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/FlappyHorseFlightRoutes.accountBinding.test.tsx | Exercise actual paid balance callbacks and Horse Flight start/completion, proving uninterrupted games, stable gateways/mode, latest balances and stale/account-change reply guards.
Edited: 2026-11-21T16:23:31-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/FlappyRoute.test.tsx | Keep authenticated route source check aligned with captured initial account identity during local balance refresh.
Edited: 2026-11-21T16:23:31-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/HorseFlightRoute.test.tsx | Keep authenticated route source check aligned with captured initial account identity during local balance refresh.
Edited: 2026-11-21T16:23:56-06:00 | Computer: Vinnabok | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/DragonTigerGame.test.tsx — Verify restored receipts retain immutable results while current wallet funds control play, including failed refresh and account switches.
Edited: 2026-11-21T16:23:56-06:00 | Computer: Vinnabok | File: games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client/src/useAndarBaharTable.test.tsx — Verify restored receipts retain immutable results while current wallet funds control play, including failed refresh and account switches.
Edited: 2026-11-21T16:23:34-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/HeartsFreeHttpTests.cs | Added free/trial HTTP account, idempotency body, pause and cancellation recovery coverage.
Edited: 2026-11-21T16:24:26-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableEndpoints.cs | Bind the signed Blackjack queue DELETE version explicitly from its JSON body.
Edited: 2026-11-21T16:24:57-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Baccarat.cs | Imported the public gateway fixture HTTP context types.
Edited: 2026-11-21T16:25:22-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/FirestorePaidHeartsStore.Advance.cs - Add immutable paid advance lifecycle and HTTP auth/trusted-name/paused replay coverage.
Edited: 2026-11-21T16:25:22-06:00 | Computer: Vinnabok | File: games/Hearts/tests/FortuneForge.Games.Hearts.Tests/PaidHeartsHttpTests.cs - Add immutable paid advance lifecycle and HTTP auth/trusted-name/paused replay coverage.
Edited: 2026-11-21T16:25:24-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.HiLo.cs — Verify Hi-Lo's signed public reservation recovery and cashout across worker restart, including account scope and durable wallet and audit evidence.
Edited: 2026-11-21T16:25:51-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Added the six verified slot server and test projects to the games-only CI solution.
Edited: 2026-11-21T16:26:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Blackjack.cs — Exercise signed Blackjack MVC and worker wallet recovery, private archive, and exact queue cancellation against Firestore.
Edited: 2026-11-21T16:28:15-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj | Removed the unused host analytics source link from the standalone Craps worker build.
Edited: 2026-11-21T16:29:13-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Mines.cs — Verify Mines signed paid admission and terminal reservation recovery through the public gateway and real Firestore wallet with immutable requests, account privacy and audit evidence.
Edited: 2026-11-21T16:29:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs | Added historical Olympus v2/v3 cash completion, audit replay, paused cutover and signed current-worker takeover using the existing harness.
Edited: 2026-11-21T16:29:34-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.NumberGames.cs — Exercise signed public Keno and Sic Bo recovery, wallet settlement, and private round history through the package endpoints.
Edited: 2026-11-21T16:30:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/NumericEvidenceReplayAudit.cs | Reconciled frozen Keno and Sic Bo evidence against the signed wallet ledger and covered signed ledger tamper detection.
Edited: 2026-11-21T16:29:58-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/GameSecurityIntegrityAuditor.cs | Reconciled frozen Keno and Sic Bo evidence against the signed wallet ledger and covered signed ledger tamper detection.
Edited: 2026-11-21T16:30:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.NumberGames.cs | Reconciled frozen Keno and Sic Bo evidence against the signed wallet ledger and covered signed ledger tamper detection.
Edited: 2026-11-21T16:30:33-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionController.cs — Kept public standings and preserved arcade command URLs as worker-required routing boundaries.
Edited: 2026-11-21T16:27:06-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/HorseFlight/HorseFlightController.cs — Kept direct Horse Flight free API and replay size as worker routing boundaries with the existing maintenance gate.
Edited: 2026-11-21T16:27:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionConfiguration.cs — Removed embedded arcade gameplay and settlement service registration while retaining wallet verification and public standings.
Edited: 2026-11-21T16:27:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/ArcadeReplayAudit.cs — Delegated Flappy replay canonicalization and terminal naming directly to the game package.
Edited: 2026-11-21T16:27:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs — Kept public arcade standings anonymous and routed both Horse Flight free APIs to the same worker under its maintenance gate.
Edited: 2026-11-21T16:28:49-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionControllerTests.cs — Verified worker-only command route templates and failure behavior while retaining public standings coverage.
Edited: 2026-11-21T16:29:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Competition/ArcadeCompetitionSettlementWorkerTests.cs — Verified the host composes settlement read and credit services without a processing scheduler.
Edited: 2026-11-21T16:29:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/HorseFlight/HorseFlightContractTests.cs — Verified maintenance responses on the direct Horse Flight worker boundary.
Edited: 2026-11-21T16:29:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs — Verified anonymous standings, worker-only admission fallback, and both direct and competition Horse Flight free APIs using the signed worker and wallet.
Edited: 2026-11-21T16:30:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs — Verified exact anonymous standings routing and the configured Horse Flight free route and feature gate.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/ArcadeCompetitionAsteroidsPaidEntryService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/ArcadeCompetitionFlappyPaidEntryService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/ArcadeCompetitionHorseFlightPaidEntryService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/ArcadeCompetitionPaidEntryService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/ArcadeRunSecurityAudit.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/AsteroidsReplayCore.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/FirestoreArcadeCompetitionPaidEntryCoordinator.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/FirestoreAsteroidsFreeRunService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/FirestoreFlappyFreeRunService.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:28:18-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Arcade/Legacy/HorseFlightFirestoreStore.cs — Moved the obsolete host implementation into test-only historical compatibility support.
Edited: 2026-11-21T16:30:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Arcade/Competition/ArcadeCompetitionSettlementWorker.cs — Retired the platform scheduler now that game package workers own settlement processing.
Edited: 2026-11-21T16:31:16-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.cs | Added partial signed arcade test coverage for first paid-intent recovery after rollover with frozen bytes, seed/window and exactly one real debit.
Edited: 2026-11-21T16:31:16-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedArcadeCompetitionIntegrationTests.Rollover.cs | Added partial signed arcade test coverage for first paid-intent recovery after rollover with frozen bytes, seed/window and exactly one real debit.
Edited: 2026-11-21T16:31:48-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/GameWorkerModule.cs - Register deadline recovery only when a Firestore database is configured, preserving clean health-only worker startup.
Edited: 2026-11-21T16:32:46-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Roulette.cs — Verify Roulette public MVC forwarding, signed profile-only admission, production roster leases, private replay and zero wallet balance or ledger effects.
Edited: 2026-11-21T16:32:51-06:00 | Computer: Vinnabok | File: games/Craps/server/GameWorkerModule.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:32:51-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/CrapsWorkerEndpoints.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:32:51-06:00 | Computer: Vinnabok | File: games/Craps/server/FortuneForge.Games.Craps.Server/FortuneForge.Games.Craps.Server.csproj | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Craps/CrapsController.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/CrapsWalletFinancialAudit.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/NumericEvidenceReplayAudit.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Craps/CrapsGameService.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Craps/CrapsCompatibilityWallet.cs | Made the Craps worker the sole gameplay route and retained wallet audit verification through shared direct settlement terms.
Edited: 2026-11-21T16:33:51-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Craps/CrapsGameService.cs | Moved the legacy compatibility implementation to test-only support.
Edited: 2026-11-21T16:33:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Craps/CrapsCompatibilityWallet.cs | Moved the legacy compatibility implementation to test-only support.
Edited: 2026-11-21T16:34:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Craps.cs — Verify Craps public MVC and production worker routing through signed wallet recovery, frozen dice, point-period odds refund and terminal audit.
Edited: 2026-11-21T16:35:08-06:00 | Computer: VINNABOK | File: games/HiLo/INTEGRATION.md | Updated signed worker/wallet ownership, account-bound client setup, reservation recovery and real integration verification; removed stale direct-wallet and past release instructions.
Edited: 2026-11-21T16:35:25-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/RoulettePopulation.cs — Verify signed managed lease ownership; preserve humans on confirmed loss and saved state on transport uncertainty.
Edited: 2026-11-21T16:35:25-06:00 | Computer: Vinnabok | File: games/Roulette/server/FortuneForge.Games.Roulette.Server/SignedRoulettePopulation.cs — Verify signed managed lease ownership; preserve humans on confirmed loss and saved state on transport uncertainty.
Edited: 2026-11-21T16:35:25-06:00 | Computer: Vinnabok | File: games/Roulette/tests/FortuneForge.Games.Roulette.Tests/RouletteManagedLeaseTests.cs — Verify signed managed lease ownership; preserve humans on confirmed loss and saved state on transport uncertainty.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/BlackjackSoloStore.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.ManagedPlayers.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableCoordinator.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableEndpoints.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/BlackjackTableService.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackSoloRecoveryTests.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackTableWalletRecoveryTests.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackManagedLeaseTests.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/BlackjackTableEndpointTests.cs — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:36-06:00 | Computer: Vinnabok | File: games/Blackjack/tests/FortuneForge.Games.Blackjack.Tests/FortuneForge.Games.Blackjack.Tests.csproj — Prioritize immutable Blackjack retries, protect roster reservation handoff and retire confirmed lost managed leases with regressions.
Edited: 2026-11-21T16:35:56-06:00 | Computer: Vinnabok | File: games/Hearts/server/FortuneForge.Games.Hearts.Server/Paid/README.md - Document authoritative paid lifecycle, atomic funding limits, immutable replay/proof and explicit legacy cleanup semantics.
Edited: 2026-11-21T16:37:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.HouseHoldem.cs — Verify signed House Holdem public wager recovery, paused completion, private history, and protected financial proof.
Edited: 2026-11-21T16:38:05-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCompetitiveSolitaireWorkerIntegrationTests.cs — Added signed competitive Solitaire admission and retry coverage with real wallet batch evidence and a real managed-player lease.
Edited: 2026-11-21T16:38:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.TexasHoldem.cs — Verify signed public Hold'em escrow, lost committed debit and DELETE refund replies, paused recovery, and scoped immutable batch and ledger evidence.
Edited: 2026-11-21T16:40:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.HiLo.cs | Added protected outcome replay and financial reconciliation on the actual signed wallet capture; added worker opt-in rollback recovery and missing-worker fail-closed proof.
Edited: 2026-11-21T16:40:41-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Mines.cs — Added sequence replay and financial reconciliation assertions against the actual signed Mines terminal protected capture and persisted journal.
Edited: 2026-11-21T16:41:17-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/ProtectedCardWalletAudit.cs — Reconcile protected single-player card wallet evidence from canonical Firestore settlements.
Edited: 2026-11-21T16:39:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardFinancialAudit.cs — Use protected wallet reconciliation for signed House Holdem, Video Poker, and Casino War captures.
Edited: 2026-11-21T16:39:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/IWalletRoundSettlementStore.cs — Retain valid optional balance floors when reconstructing a direct wallet request.
Edited: 2026-11-21T16:40:43-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.HouseHoldem.cs — Assert signed financial audit accepts exact proof and rejects a changed wallet leg.
Edited: 2026-11-21T16:39:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/BlackjackWalletFinancialAudit.cs — Compare replayed and frozen Blackjack timestamps at persisted Firestore precision.
Edited: 2026-11-21T16:39:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/BlackjackWalletFinancialAuditTests.cs — Exercise signed Blackjack with a deliberate sub-microsecond opening clock value.
Edited: 2026-11-21T16:41:35-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Baccarat.cs — Recheck the actual signed protected capture through domain outcome and financial replay inspectors.
Edited: 2026-11-21T16:41:35-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.Craps.cs — Recheck the actual signed protected capture through domain outcome and financial replay inspectors.
Edited: 2026-11-21T16:40:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Numbers/Keno/KenoController.cs — Keep Keno public routes and rate limits as fail-closed signed-worker forwarding shells.
Edited: 2026-11-21T16:40:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Numbers/Keno/KenoFirestoreStore.cs — Retire the legacy direct-wallet Keno writer.
Edited: 2026-11-21T16:40:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Numbers/Keno/KenoModels.cs — Remove duplicate Keno rules, service and API contracts from the host.
Edited: 2026-11-21T16:40:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Numbers/Keno/KenoOutcomeAudit.cs — Use the game-package Keno outcome auditor.
Edited: 2026-11-21T16:42:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Dice/SicBo/SicBoController.cs — Keep Sic Bo routes and limits, fail closed on paid fallback and delegate authenticated development practice.
Edited: 2026-11-21T16:41:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Dice/SicBo/SicBoFirestoreStore.cs — Retire the legacy direct-wallet Sic Bo writer.
Edited: 2026-11-21T16:41:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Dice/SicBo/SicBoModels.cs — Remove duplicate Sic Bo rules, service and API contracts from the host.
Edited: 2026-11-21T16:41:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Dice/SicBo/SicBoOutcomeAudit.cs — Use the game-package Sic Bo outcome auditor.
Edited: 2026-11-21T16:41:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Dice/SicBo/PracticeSicBoStore.cs — Move the virtual-only practice store to the Sic Bo package.
Edited: 2026-11-21T16:41:32-06:00 | Computer: VINNABOK | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/PracticeSicBoStore.cs — Own the isolated Sic Bo virtual practice store alongside the game server.
Edited: 2026-11-21T16:42:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs — Register the package Sic Bo practice store only in Development.
Edited: 2026-11-21T16:41:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/NumericEvidenceReplayAudit.cs — Use package-owned draw game contracts and outcome checks for signed and legacy evidence.
Edited: 2026-11-21T16:40:12-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Keno/KenoContractTests.cs — Exercise package contracts and confirm 503 for every unforwarded Keno route.
Edited: 2026-11-21T16:42:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SicBo/SicBoContractTests.cs — Exercise package contracts and confirm 503 for unforwarded paid Sic Bo routes.
Edited: 2026-11-21T16:42:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Practice/WalletPracticeStoreTests.cs — Keep the isolated Sic Bo practice balance test on the package store.
Edited: 2026-11-21T16:42:01-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Activity/AcceptedGameActivityTests.cs — Build numerical game activity with package-owned public responses.
Edited: 2026-11-21T16:41:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Activity/AcceptedGameTransactionWitnessTests.cs — Build numerical game activity evidence with canonical package round models.
Edited: 2026-11-21T16:41:10-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/NumericEvidenceReplayAuditTests.cs — Verify numerical game replay against canonical package contracts.
Edited: 2026-11-21T16:40:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/NumericEvidenceReplayFirestoreTests.cs — Seed original source, ledger and audit schemas for legacy financial tampering tests.
Edited: 2026-11-21T16:35:21-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Enrolled the six reverified classic worker and test projects in the games solution.
Edited: 2026-11-21T16:39:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs | Required game workers for paid Hi-Lo and Mines even while new wagers are paused.
Edited: 2026-11-21T16:39:12-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs | Required game workers for paid Hi-Lo and Mines even while new wagers are paused.
Edited: 2026-11-21T16:39:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.cs | Reused real signed gateway fixtures for paused Video Poker and Casino War recovery.
Edited: 2026-11-21T16:39:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.NumberGames.cs | Reused real signed gateway fixtures for paused Video Poker and Casino War recovery.
Edited: 2026-11-21T16:40:37-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.DrawGames.cs | Reused real signed gateway fixtures for paused Video Poker and Casino War recovery.
Edited: 2026-11-21T16:35:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Craps/CrapsController.cs | Imported the platform endpoint rate-limit policy for the Craps gateway.
Edited: 2026-11-21T16:42:51-06:00 | Computer: Vinnabok | File: games/HorseFlight/server/README.md | Clarify the preserved default-disabled maintenance gate, explicit hosted worker revision setting, and independent host forwarding/catalogue visibility.
Edited: 2026-11-21T16:43:05-06:00 | Computer: Vinnabok | File: artifacts/backend-completion/SignedHeartsWorkerIntegrationTests.cs — Prepared a signed Hearts paid join and refund integration fixture outside the test project pending the host package reference.
Edited: 2026-11-21T16:44:39-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedCompetitiveSolitaireWorkerIntegrationTests.cs — Corrected signed integration assertions to distinguish open UI seats from persisted or leased players and advance beyond the scheduled join threshold.
Edited: 2026-11-21T16:45:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/MinesSequenceReplayAudit.cs — Match round-summary timestamps at exact original or Firestore microsecond precision while preserving exact private replay and financial checks.
Edited: 2026-11-21T16:45:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/MinesSequenceReplayAuditTests.cs — Cover exact and microsecond-truncated summary timestamps and reject an actual timestamp change.
Edited: 2026-11-21T16:45:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.ThreeCardPoker.cs — Verify signed Three Card Poker profile identity, exact wager debit recovery, paused settlement, and protected history.
Edited: 2026-11-21T16:45:51-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/FirestoreCreditHoldemStore.cs - Prioritize exact immutable command receipts over later pending wallet work; resolve validated trusted names only for fresh queue admission.
Edited: 2026-11-21T16:45:51-06:00 | Computer: Vinnabok | File: games/TexasHoldem/server/FortuneForge.Games.TexasHoldem.Server/CreditHoldemEndpoints.cs - Prioritize exact immutable command receipts over later pending wallet work; resolve validated trusted names only for fresh queue admission.
Edited: 2026-11-21T16:45:57-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.NumberGames.cs | Added same-payout source-outcome tamper and restoration proof against real signed numeric wallet captures, retaining wallet receipt/ledger timestamps and balances.
Edited: 2026-11-21T16:46:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsPlatformTests.cs | Added game-specific host test composition using authoritative generic wallet batch storage and legacy roster.
Edited: 2026-11-21T16:46:53-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.Prepared.cs — Fence new managed-profile adoptions against permanent retired pairs while preserving paid frozen finalization.
Edited: 2026-11-21T16:47:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.TexasHoldem.cs — Revoke live signed name scope after a committed buy-in reply is lost and require exact paused POST recovery before polling.
Edited: 2026-11-21T16:45:11-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoMoney.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:47:55-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoWalletStore.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:45:11-06:00 | Computer: Vinnabok | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/PracticeSicBoStore.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:45:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/SicBo/SicBoContractTests.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:47:55-06:00 | Computer: Vinnabok | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoWalletStore.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:47:55-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/NumericEvidenceReplayAudit.cs | Preserved numeric historical parsing and checked current paid/practice admission or the exact saved outcome during signed wallet audit.
Edited: 2026-11-21T16:48:52-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/ThreeCardPokerWalletFinancialAudit.cs — Reconcile protected Three Card Poker table ledgers, package receipts, and platform wallet effects.
Edited: 2026-11-21T16:48:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/PaidCardFinancialAudit.cs — Dispatch protected Three Card Poker completion evidence to its financial auditor.
Edited: 2026-11-21T16:48:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.ThreeCardPoker.cs — Verify protected audit acceptance and changed settlement-link rejection through the signed fixture.
Edited: 2026-11-21T16:47:09-06:00 | Computer: VINNABOK | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.FinancialProof.cs — Expose read-only canonical frozen operation and receipt projections for independent wallet inspection.
Edited: 2026-11-21T16:47:09-06:00 | Computer: VINNABOK | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.cs — Share the existing canonical wallet receipt document path with the read-only audit reader.
Edited: 2026-11-21T16:52:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/ThreeCardPokerWalletFinancialAudit.cs — Accept safe scheduled payout document IDs and leave exact identity validation with the game package.
Edited: 2026-11-21T16:52:55-06:00 | Computer: VINNABOK | File: games/ThreeCardPoker/server/FortuneForge.Games.ThreeCardPoker.Server/Table/FirestoreThreeCardPokerTableStore.FinancialProof.cs — Validate hashed manual operation IDs and canonical no-key scheduled operation IDs independently.
Edited: 2026-11-21T16:52:48-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedCardWorkerSmokeTests.ThreeCardPoker.cs — Make the real signed fold fixture reproduce a Pair Plus credit and its scheduled wallet proof every run.
Edited: 2026-11-21T16:48:22-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/Blackjack/BlackjackControllers.cs | Removed unused local runtime dependencies from the Blackjack gateway routes.
Edited: 2026-11-21T16:48:22-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Cards/VideoPoker/VideoPokerController.cs | Removed unused local runtime dependencies from the Video Poker gateway routes.
Edited: 2026-11-21T16:48:22-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/BlackjackContractTests.cs | Updated gateway fixtures to use the thin Blackjack gateway constructor.
Edited: 2026-11-21T16:48:22-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/VideoPoker/VideoPokerContractTests.cs | Updated gateway fixtures to use the thin Video Poker gateway constructor.
Edited: 2026-11-21T16:49:34-06:00 | Computer: Vinnabok | File: games/Blackjack/server/FortuneForge.Games.Blackjack.Server/Table/FirestoreBlackjackTableStore.ManagedPlayers.cs | Included the shared wallet exception for the managed-player settlement fence.
Edited: 2026-11-21T16:49:34-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Enrolled the Hearts backend and test projects.
Edited: 2026-11-21T16:49:34-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Enrolled the Hearts backend and test projects.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsGameService.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsContracts.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsWorker.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsOnboardingStore.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/FirestorePaidHeartsStore.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/FirestorePaidHeartsStore.JoinBatch.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/FirestorePaidHeartsStore.Progression.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsContracts.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsProjection.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsRoundAudit.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Hearts/HeartsHousePlayers.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Hearts/HeartsManagedActionPolicy.cs | Removed duplicate host Hearts domain source after package verification; package is authoritative.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: games/Hearts/game-release.json | Select the authoritative Hearts worker through the existing game-specific release path.
Edited: 2026-11-21T16:55:17-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedHeartsWorkerIntegrationTests.cs | Added real signed public Hearts join and refund recovery checks against the platform wallet and Firestore emulator.
Edited: 2026-11-21T16:55:01-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Referenced the authoritative Hearts package for routing contracts and evidence projection.
Edited: 2026-11-21T16:55:01-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs | Removed Hearts game runtime, managed-player, onboarding and scheduling registrations from the shared host.
Edited: 2026-11-21T16:55:01-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs | Registered existing Hearts public endpoints with signed worker routing.
Edited: 2026-11-21T16:55:01-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Admin/Management/AdminManagementFirestoreTests.cs | Preserved the Hearts disabled-bots test through real shared batch wallet accounting and the authoritative game store.
Edited: 2026-11-21T16:55:01-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Admin/Management/ManagedPlayerAdmissionTests.cs | Asserted the packaged Hearts roster-unavailable error for free and trial play.
Edited: 2026-11-21T16:55:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsGameServiceTests.cs | Moved Hearts game-policy tests into authoritative package; host retains only forwarding/auth boundary coverage.
Edited: 2026-11-21T16:55:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsTrialTests.cs | Moved Hearts game-policy tests into authoritative package; host retains only forwarding/auth boundary coverage.
Edited: 2026-11-21T16:55:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsPracticeLifecycleTests.cs | Moved Hearts game-policy tests into authoritative package; host retains only forwarding/auth boundary coverage.
Edited: 2026-11-21T16:55:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsHousePlayersTests.cs | Moved Hearts game-policy tests into authoritative package; host retains only forwarding/auth boundary coverage.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsController.cs | Preserved legacy Hearts routes with one forwarding-only auth/failure boundary and package ownership.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsOnboardingController.cs | Preserved legacy Hearts routes with one forwarding-only auth/failure boundary and package ownership.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsController.cs | Preserved legacy Hearts routes with one forwarding-only auth/failure boundary and package ownership.
Edited: 2026-11-21T16:54:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsHostFallback.cs | Preserved legacy Hearts routes with one forwarding-only auth/failure boundary and package ownership.
Edited: 2026-11-21T16:55:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Hearts/HeartsOnboardingFirestoreTests.cs | Preserved legacy Hearts routes with one forwarding-only auth/failure boundary and package ownership.
Edited: 2026-11-21T16:56:41-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/HeartsEvidenceReplayAudit.cs — Verify Hearts package batch evidence against semantic match effects and original platform wallet receipts while preserving historical replay.
Edited: 2026-11-21T16:56:09-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/SecurityAudit/HeartsFinancialReplayFirestoreTests.cs — Exercise actual protected Hearts captures and platform batches in Firestore, including rehashed semantic tampering and a changed platform ledger leg.
Edited: 2026-11-21T16:58:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs — Select durable workers by default for fresh puzzle starts while keeping existing GUID-v4 session affinity.
Edited: 2026-11-21T16:58:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightController.cs — Return worker-unavailable on an unforwarded 2048 start without creating a local session.
Edited: 2026-11-21T16:58:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeController.cs — Return worker-unavailable on an unforwarded Drop Merge start without creating a local session.
Edited: 2026-11-21T16:58:06-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs — Cover default puzzle worker selection and fail-closed local start actions for both games.
Edited: 2026-11-21T16:58:19-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/SignedPuzzleWorkerIntegrationTests.cs — Prove real signed puzzle starts without optional routing flags and 503 before any round exists when a worker binding is missing.
Edited: 2026-11-21T17:01:28-06:00 | Computer: Vinnabok | File: packages/client-presentation/src/gameCommandRecovery.ts — Preserve an optional exact caller key for newly prepared commands and retain the original key during recovery.
Edited: 2026-11-21T17:01:28-06:00 | Computer: Vinnabok | File: packages/client-presentation/src/gameCommandRecovery.test.ts — Cover explicit key replay, original pending body and key, blank validation, and factory fallback.
Edited: 2026-11-21T17:01:09-06:00 | Computer: VINNABOK | File: games/TwentyFortyEight/server/FortuneForge.Games.TwentyFortyEight.Server/LegacyTwentyFortyEightGameService.cs — Own the legacy per-process 2048 session lifecycle in its server package.
Edited: 2026-11-21T17:01:09-06:00 | Computer: VINNABOK | File: games/DropMerge/server/FortuneForge.Games.DropMerge.Server/LegacyDropMergeGameService.cs — Own the legacy per-process Drop Merge session lifecycle in its server package.
Edited: 2026-11-21T17:01:09-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/TwentyFortyEight/TwentyFortyEightGameService.cs — Adapt the existing 2048 singleton to package-owned legacy lifecycle and signed compute.
Edited: 2026-11-21T17:01:09-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/DropMerge/DropMergeGameService.cs — Adapt the existing Drop Merge singleton to package-owned legacy lifecycle and signed compute.
Edited: 2026-11-21T17:01:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Games/LiarsDice/LiarsDiceController.cs — Fail closed for fresh Liar's Dice matches that reach the legacy controller.
Edited: 2026-11-21T17:01:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/PackagedGameWorkerForwarding.cs — Always forward fresh enabled Liar's Dice starts and preserve legacy versus durable match affinity.
Edited: 2026-11-21T17:01:45-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/PackagedGameWorkerForwardingTests.cs — Check Liar's Dice start authority, legacy affinity, and the existing feature gate.
Edited: 2026-11-21T17:02:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/httpHeartsGateway.ts — Preserve explicit paid caller keys through the shared preferred-key journal API and verify completed/pending retry compatibility.
Edited: 2026-11-21T17:02:18-06:00 | Computer: Vinnabok | File: games/Hearts/client/FortuneForge.Games.Hearts.Client/src/httpHeartsGateway.recovery.test.ts — Preserve explicit paid caller keys through the shared preferred-key journal API and verify completed/pending retry compatibility.
Edited: 2026-11-21T17:00:52-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Controllers/SlotsController.cs | Required workers for new paid slot rounds and Neon features; retained exact bound Arcane and Neon recovery.
Edited: 2026-11-21T17:01:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs | Marked the Arcane feature writes as authenticated worker-owned routes.
Edited: 2026-11-21T17:01:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/SlotServicesConfiguration.cs | Retired the shared host Arcane feature store registration.
Edited: 2026-11-21T17:01:15-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Referenced package-owned historical puzzle adapters.
Edited: 2026-11-21T16:58:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsController.cs | Restored the shared rate-limit policy imports on thin Hearts paths.
Edited: 2026-11-21T16:58:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsController.cs | Restored the shared rate-limit policy imports on thin paid Hearts paths.
Edited: 2026-11-21T16:58:29-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/HeartsOnboardingController.cs | Restored the shared rate-limit policy imports on thin onboarding paths.
Edited: 2026-11-21T16:57:28-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/SignedHeartsWorkerIntegrationTests.cs | Verified signed free exact replay and account isolation alongside signed paid recovery.
Edited: 2026-11-21T17:01:15-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Enrolled the existing Craps test project in the main solution.
Edited: 2026-11-21T17:01:15-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Enrolled server and test projects for Baccarat, Roulette and Craps.
Edited: 2026-11-21T16:57:16-06:00 | Computer: Vinnabok | File: scripts/game-worker-release.mjs | Added hosted worker settings for Blackjack, Three Card Poker and Hearts and enabled the required Horse Flight worker setting.
Edited: 2026-11-21T17:03:38-06:00 | Computer: Vinnabok | File: docs/GAME_DEPLOYMENT.md | Documented worker binding and legacy admission drainage before switching the gateway release.
Edited: 2026-11-21T17:02:35-06:00 | Computer: Vinnabok | File: games/LiarsDice/server/README.md | Documented required fresh-match worker routing and retained legacy session behavior.
Edited: 2026-11-21T17:03:50-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Games/Hearts/PaidHeartsConfiguration.cs | Recorded removal of the retired host Hearts scheduler and store registration.
Edited: 2026-11-21T17:03:50-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Bots/Hearts/HeartsManagedPlayers.cs | Recorded removal of the duplicate host Hearts roster implementation.
Edited: 2026-11-21T17:03:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/LegacySlotSettlementHarness.cs — Add a test-only legacy admission and settlement harness using the real Firestore store and spin engine.
Edited: 2026-11-21T17:03:49-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/MidnightVaultPaidFirestoreTests.cs — Verify historical Midnight Vault payouts, bonus queues, exact ledger, cooldown and frozen receipts directly through legacy settlement interfaces.
Edited: 2026-11-21T17:02:32-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/OlympusPaidFirestoreTests.cs — Verify the outstanding Olympus bonus guard and exact saved odd-wager settlement without admitting new work through the public controller.
Edited: 2026-11-21T17:02:56-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs — Prove disabled slot routing blocks unkeyed paid and free starts before the worker owns the account without mutating historical credits or the ledger.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Arcade/Legacy/HorseFlightFirestoreStore.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/Asteroids/server/FortuneForge.Games.Asteroids.Server/AsteroidsPaidRunService.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/HorseFlight/server/FortuneForge.Games.HorseFlight.Server/HorseFlightFreeRunStore.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoMoney.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/Keno/server/FortuneForge.Games.Keno.Server/KenoServerContracts.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/Keno/tests/FortuneForge.Games.Keno.Server.Tests/KenoEmulator.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoContracts.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/SicBo/server/FortuneForge.Games.SicBo.Server/SicBoMoney.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/SicBo/tests/FortuneForge.Games.SicBo.Server.Tests/SicBoEmulator.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/ThreeCardPoker/tests/FortuneForge.Games.ThreeCardPoker.Tests/ThreeCardPokerManagedPlayerTests.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:15:54-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/ManagedPlayers/ManagedPlayerTableStayPolicy.cs - Remove extra final blank line before release.
Edited: 2026-11-21T17:20:31-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Apply validated host runtime switches and distinct pinned signing references to the non-serving candidate API revision, lock input bytes, and verify config before traffic.
Edited: 2026-11-21T17:20:31-06:00 | Computer: VINNABOK | File: scripts/tests/deploy-guardrails.tests.ps1 | Exercise runtime configuration whitelist, collisions, pinned secrets, byte locks, candidate configuration mismatches and traffic guard order.
Edited: 2026-11-21T17:22:01-06:00 | Computer: Vinnabok | File: scripts/game-worker-release.mjs — Added private candidate service selection, reviewed runtime settings with pinned distinct signing secrets, and revision verification before activation.
Edited: 2026-11-21T17:22:01-06:00 | Computer: Vinnabok | File: scripts/tests/game-worker-release.test.mjs — Added fake-cloud coverage for candidate enrollment, protected settings, key separation, immutable settings, and exact revision activation.
Edited: 2026-11-21T17:22:01-06:00 | Computer: Vinnabok | File: docs/GAME_DEPLOYMENT.md — Documented candidate services, supported runtime settings, admission policy preservation, and settings verification.
Edited: 2026-11-21T17:23:08-06:00 | Computer: Vinnabok | File: scripts/ReleaseGuardrails.psm1 — Added worker publisher tooling to immutable release inputs and its fake-cloud policy suite to non-site release verification.
Edited: 2026-11-21T17:23:08-06:00 | Computer: Vinnabok | File: scripts/tests/release-scope.tests.ps1 — Verified publisher policy tests are mandatory normally, excluded only by the explicit test-free waiver, and protected as committed tooling.
Edited: 2026-11-21T17:24:10-06:00 | Computer: VINNABOK | File: artifacts/backend-completion/provision-production.ps1 - Prepare and verify scoped runtime identities and pinned signing credentials for the full deployment.
Edited: 2026-11-21T18:02:21-06:00 | Computer: VINNABOK | File: games/Solitaire/server/FortuneForge.Games.Solitaire.Server/FirestoreCompetitiveSolitaireStore.Financial.cs | Reuse bounded financial-gate recovery and transaction retries when preparing exact-key Solitaire wallet plans.
Edited: 2026-11-21T18:02:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Solitaire/CompetitiveSolitaireBotFillFirestoreTests.cs | Require one exact 500-cent operation-bound wallet debit and remaining balance per human after concurrent joins.
Edited: 2026-11-21T18:02:21-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/SecurityAudit/InvestigationFirestoreTests.cs | Generate the escrow ticket with the authoritative hash so debit and cash-out audit assertions exercise valid persisted ticket identities.
Edited: 2026-11-21T18:03:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs - Preserve frozen energy settlement policy for original host evidence while honoring native package profiles.
Edited: 2026-11-21T18:03:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/RainbowRealmModelTests.cs - Reject an unmultiplied historical Rainbow payout while preserving its original settled award.
Edited: 2026-11-21T18:03:07-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/SlotRoundAuditTests.cs - Verify modern High Noon and Olympus audit profiles reject dormant-energy payout inflation.
Edited: 2026-11-21T18:03:07-06:00 | Computer: VINNABOK | File: games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client/src/DragonTigerGame.test.tsx - Assert chip reset and Rebet preserve the refreshed live wallet instead of historical round funds.
Edited: 2026-11-21T18:04:47-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Bots/ManagedPlayerGamePackageBoundaryTests.cs | Replace whole-package word ban with platform ownership and direct-lease checks; retain neutral rule engines and cover signed adapters plus forbidden owner/write fixtures.
Edited: 2026-11-21T18:03:57-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/Table/BlackjackTableFirestoreEmulatorTests.cs | Seed Blackjack emulator players through the real account store and preserve authoritative wallet metadata while setting requested test funds.
Edited: 2026-11-21T18:09:22-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotContracts/Program.cs | Export guide definitions directly through the actual package-backed provider and derive complete slot source ownership from project references.
Edited: 2026-11-21T18:09:22-06:00 | Computer: VINNABOK | File: tools/verify-slot-contracts.mjs | Verify provider-only guide captures and all twenty slot owner projects while retaining scenario-capture and current-source hash validation.
Edited: 2026-11-21T18:09:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/scripts/export-slot-guide-contracts.mjs | Import verified provider-only guide captures and retain every slot package owner in fixture provenance.
Edited: 2026-11-21T18:09:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/engineGuide.test.ts | Require all twenty package owner projects in addition to exact retained source hashes and independent guide math assertions.
Edited: 2026-11-21T18:09:22-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotContracts/README.md | Document the direct provider guide export independently of historical controller scenario captures.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Activity/AcceptedGameActivity.cs — Included authoritative prepared slot routes in accepted activity and participation attribution.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/AcceptedGameActivityRealContractTests.cs — Verified every runtime slot across its actual legacy and prepared aliases with stable receipt identity.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Activity/GameActivityMiddlewareTests.cs — Covered signed result attribution, account scope, participation, and cross-route retry deduplication.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Mines/MinesHistoryIntegrationTests.cs — Exercised actual host forwarding through signed Mines worker and wallet bridge while preserving auth, admission, practice and wallet assertions.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs — Added read-only standard ledger verification using authoritative receipt, reservation, phase, and exact leg validation.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Players/FirestoreAdminPlayersStore.cs — Included verified generic wallet cashflow in bounded player totals while preserving unknown-scope and legacy classification.
Edited: 2026-11-21T18:10:31-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/HiLo/HiLoHistoryIntegrationTests.cs — Retained exact-money settlement coverage and rejected corrupted receipts, account/scope/phase mismatches, ledger tampering, and unknown scopes.
Edited: 2026-11-21T18:13:57-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs — Bound every non-null wallet leg to its canonical Firestore document ID while retaining absent zero-leg semantics.
Edited: 2026-11-21T18:13:57-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/HiLo/HiLoHistoryIntegrationTests.cs — Rejected cloned wager/payout documents with identical stored fields and preserved original exact-money admin totals.
Edited: 2026-11-21T18:14:41-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Players/FirestoreAdminPlayersStore.cs — Required verified persisted wallet receipts for generic money classification, preserving historical typed events and structural attribution separately.
Edited: 2026-11-21T18:15:43-06:00 | Computer: VINNABOK | File: tools/import-slot-contracts.mjs | Preserve the explicit source hash algorithm when importing canonical or historical raw-byte slot contract metadata.
Edited: 2026-11-21T18:16:16-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/HiLo/HiLoHistoryIntegrationTests.cs — Used authoritative 64-hex direct-card round lookups and established valid exact-money totals before testing missing/corrupt receipt rejection.
Edited: 2026-11-21T18:17:04-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Regenerate independent provider evidence with twenty package owners and versioned canonical source hashes; all nineteen math contracts and playable guide data remain unchanged.
Edited: 2026-11-21T18:23:20-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Games/Mines/MinesHistoryIntegrationTests.cs — Matched the production singleton AccountService lifetime so singleton authorizers and activity resolvers validate without disabling scope checks.
Edited: 2026-11-21T18:24:56-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Blackjack/Table/BlackjackTableFirestoreEmulatorTests.cs | Align concurrent native join retries with immutable original queue receipts; verify current table separately and retain shard, seat, ticket, pending-lock and money invariants.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/IGameBackendPackage.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Hosting/GameBackendPackageCommand.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/GameBackendPackage.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/Program.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.ClassicSlot.Server/GameBackendPackage.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.ClassicSlot.Server/Program.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.ClassicSlot.Server/FortuneForge.Games.ClassicSlot.Server.csproj | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.GameCompute.Server/GameBackendPackage.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.GameCompute.Server/Program.cs | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:20:30-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.GameCompute.Server/FortuneForge.Games.GameCompute.Server.csproj | Expose package-owned application composition and runtime descriptors while retaining standalone startup and security pipelines.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageComposition.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageLoadContext.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageRuntime.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageConfiguration.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/IGamePackageTransport.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/GameWorkerClient.cs — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:21:27-06:00 | Computer: Vinnabok | File: config/backend-host.json — Compose immutable isolated game packages inside the shared backend while preserving signed platform boundaries.
Edited: 2026-11-21T19:23:03-06:00 | Computer: Vinnabok | File: scripts/deploy-game.mjs | Emit authoritative in-process backend descriptors from published module metadata and require unchanged clean source revision.
Edited: 2026-11-21T19:23:03-06:00 | Computer: Vinnabok | File: scripts/shared-backend-packages.mjs | Pin, verify and materialize complete immutable per-game publish trees for one shared backend image.
Edited: 2026-11-21T19:23:03-06:00 | Computer: Vinnabok | File: scripts/tests/shared-backend-packages.test.mjs | Verify isolated sibling preservation, exact hashes, runtime aliases, path safety and packaging CLI behavior.
Edited: 2026-11-21T19:23:03-06:00 | Computer: Vinnabok | File: docs/deployment/shared-backend-packages.md | Document one-game package updates, committed composition ownership and complete artifact retention.
Edited: 2026-11-21T19:24:10-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/GamePackageCompositionTests.cs — Verify selected package bytes, descriptor identity, duplicate runtime ownership and path boundaries.
Edited: 2026-11-21T19:26:26-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/InProcess/InProcessHttpEndpoint.cs — Added isolated in-process HTTP execution with framework context creation, per-request scopes, cancellation, graceful drain, and error-safe response disposal.
Edited: 2026-11-21T19:26:26-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/InProcess/InProcessHttpFeatures.cs — Preserved request/response HTTP features, exact buffered bytes, multi-value headers, callbacks, body limits, and response lifecycle semantics.
Edited: 2026-11-21T19:26:26-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/InProcess/InProcessGameServer.cs — Captured ASP.NET IHttpApplication through IServer without opening a network listener.
Edited: 2026-11-21T19:26:26-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/InProcess/InProcessHttpMessageHandler.cs — Routed HttpClient requests through the bound application without bypassing its authentication pipeline.
Edited: 2026-11-21T19:26:26-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/InProcessHttpEndpointTests.cs — Added transport parity, scope/callback cleanup, cancellation, parallel ambient isolation, real server factory, drain, and failure behavior regressions.
Edited: 2026-11-21T19:28:53-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Deploy a verified immutable shared package composition through locked exact uploads, required manifest digest/readiness checks, request-based scaling, and removal of stale external worker references.
Edited: 2026-11-21T19:28:53-06:00 | Computer: VINNABOK | File: scripts/ReleaseGuardrails.psm1 | Require committed package materialization tooling, tests and Docker boundary files as trusted release tooling.
Edited: 2026-11-21T19:28:53-06:00 | Computer: VINNABOK | File: scripts/tests/deploy-guardrails.tests.ps1 | Verify shared package byte locks, exact upload scope, configuration and readiness failures, request-based scaling, and selective legacy binding removal.
Edited: 2026-11-21T19:28:53-06:00 | Computer: VINNABOK | File: Dockerfile | Copy the frozen selected complete game publish outputs and host policy into the shared runtime image and require its composition.
Edited: 2026-11-21T19:28:53-06:00 | Computer: VINNABOK | File: .dockerignore | Admit only the fixed guarded shared package materialization and host policy in addition to existing build sources.
Edited: 2026-11-21T19:28:54-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Hosting/InProcess/InProcessHttpEndpoint.cs — Created root delegate HttpContext synchronously so IHttpContextAccessor remains bound in the request execution context across awaits.
Edited: 2026-11-21T19:29:39-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/FortuneForge.BackendVerification.csproj | Reference the real root backend and ASP.NET framework for emulator-only shared package verification.
Edited: 2026-11-21T19:29:39-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/Program.cs | Verify all 45 loaded packages and authenticated in-process card-wallet flows with exact money, retry, account and durable audit invariants.
Edited: 2026-11-21T19:31:45-06:00 | Computer: VINNABOK | File: .github/workflows/ci.yml | Compile the shared backend host build target in CI without private frozen artifact cache or deployment credentials.
Edited: 2026-11-21T19:31:45-06:00 | Computer: VINNABOK | File: docs/RELEASE_SAFETY.md | Document immutable package cache requirements, exact locked upload exception, final runtime payloads, request-based scaling and host-only CI.
Edited: 2026-11-21T19:32:03-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/Program.cs | Read retry response bytes before span comparison so no ReadOnlySpan crosses an await boundary.
Edited: 2026-11-21T19:32:43-06:00 | Computer: VINNABOK | File: scripts/tests/release-scope.tests.ps1 | Require immutable shared package tests in the normal guarded command plan and retain explicit test-free waiver behavior.
Edited: 2026-11-21T19:46:45-06:00 | Computer: Vinnabok | File: config/backend-packages.lock.json — Pin exact immutable versions, runtime identities and full file hashes for all 45 game backend packages.
Edited: 2026-11-21T19:46:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageLoadContext.cs — Reject missing package-owned game dependencies instead of falling back to legacy assemblies in the host.
Edited: 2026-11-21T19:49:17-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageRuntime.cs — Preserve the simple application assembly name when composing child web applications.
Edited: 2026-11-21T19:49:36-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/Program.cs | Use the root backend simple assembly name so the real static-assets manifest resolves correctly.
Edited: 2026-11-21T19:49:36-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/FortuneForge.BackendVerification.csproj | Copy the generated root static-assets endpoint manifest from its resolved project build output into verifier output.
Edited: 2026-11-21T19:52:06-06:00 | Computer: Vinnabok | File: tools/FortuneForge.BackendVerification/Program.cs | Supply unused emulator-only Firebase constructor settings and report actual unauthenticated response status for triage.
Edited: 2026-11-21T19:53:58-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Create a locked full-service candidate, validate identical bytes with regional v1 dry-run, preserve resourceVersion CAS for one PUT and old public traffic, and poll exact shared revision readiness.
Edited: 2026-11-21T19:53:58-06:00 | Computer: VINNABOK | File: scripts/tests/deploy-guardrails.tests.ps1 | Verify candidate configuration preservation, source labels, traffic pinning, payload locks, identical dry-run/mutation bytes and sanitized no-retry HTTP failures using local fakes.
Edited: 2026-11-21T20:36:32-06:00 | Computer: VINNABOK | File: .gcloudignore | Exclude the local Hypothesis example cache from source uploads without deleting or reading it.
Edited: 2026-11-21T20:36:32-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Check the SDK base source inventory against tracked paths in local preflight and immediately under source locks before full QA, while retaining the final locked upload recheck.
Edited: 2026-11-21T20:36:32-06:00 | Computer: VINNABOK | File: scripts/tests/deploy-guardrails.tests.ps1 | Verify metadata-only early source privacy checks reject stray cache files and run before QA and live preflight reads.
Edited: 2026-11-21T20:36:32-06:00 | Computer: VINNABOK | File: scripts/tests/release-scope.tests.ps1 | Require Hypothesis cache exclusion and reject any untracked cache path from the source upload allowlist.
Edited: 2026-11-21T22:27:33-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Activate zero-traffic shared candidates with an owned revision tag and conflict-safe cleanup; include isolated activation regressions in normal deployment guard checks.
Edited: 2026-11-21T22:26:48-06:00 | Computer: VINNABOK | File: scripts/tests/shared-backend-candidate-activation.tests.ps1 | Add isolated local regression checks for tagged startup, failure cleanup, and changed tag ownership.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/types/slots.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:50:57-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Render the isolated game-owned feature panel and identify settled feature awards without describing them as reel-line wins.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/catalog.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/App.tsx | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-loading/loadingPresentationsC.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:09:30-06:00 | Computer: VINNABOK | File: scripts/game-smoke-inventory.mjs | Integrate Sunstone Eclipse identity, canonical routes and isolated authoritative feature presentation.
Edited: 2026-11-22T06:13:21-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/robot-revolution.artwork.json | Select original Sunstone Eclipse catalogue art, retain prior cover generation history, and record the validated 768x384 WebP asset.
Edited: 2026-11-22T06:11:59-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/catalog.ts | Expose Sunstone Eclipse catalogue metadata while keeping the existing package export compatible.
Edited: 2026-11-22T06:34:08-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/definition.ts | Replace the entire public robot theme with Sunstone Eclipse symbols, temple styling, actual Sunwheel rules and Eclipse bonus copy.
Edited: 2026-11-22T06:17:24-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/manifest.ts | Register the bespoke Sunwheel presentation, illustrated guide and new public routes without changing persisted game identifiers.
Edited: 2026-11-22T06:15:26-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Consume original temple, guardian, sunstone, eclipse and distinct triple-sun raster assets.
Edited: 2026-11-22T06:09:05-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sounds.ts | Replace the consumed audio suite with original temple percussion, flute, stone and sun-bell cues.
Edited: 2026-11-22T06:34:08-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sunwheel.ts | Decode and validate authoritative Sunwheel geometry, identities, award sums and versioned feature bounds.
Edited: 2026-11-22T06:32:59-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunwheelPanel.tsx | Present rotating guardian rings, actual matched paths, accessible results and optional board overlays on pointer or keyboard inspection.
Edited: 2026-11-22T06:34:08-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunstoneIntroduction.tsx | Provide a responsive illustrated rules guide with current payline values, rounded guardian awards and clear Eclipse conditions.
Edited: 2026-11-22T06:27:58-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Build the bespoke carved sunwheel, guardian panels and illustrated temple guide with mobile and reduced-motion support.
Edited: 2026-11-22T07:02:46-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Give Sunstone Eclipse its own viewport-safe cabinet, separate mobile utility rail and empty-wrapper hit behavior; raise the compact two-icon utility sibling above the filtered stage stacking context.
Edited: 2026-11-22T06:08:46-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/generate-sunstone-audio.mjs | Provide deterministic original additive synthesis for all thirteen temple-themed audio cues.
Edited: 2026-11-22T06:34:08-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/sunwheel.test.mjs | Add focused base/free feature fixtures, legacy absence and malformed geometry and award validation tests.
Edited: 2026-11-22T06:15:26-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/README.md | Document the new public theme, authoritative feature display, preserved game identity and original asset ownership.
Edited: 2026-11-22T06:18:17-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/sunstoneRetheme.test.ts | Verify new catalogue identity, legacy paid/demo bookmarks, stable activity identifiers and sibling themes.
Edited: 2026-11-22T06:19:52-06:00 | Computer: VINNABOK | File: fortuneforge.client/playwright.sunstone.config.ts | Add an isolated built-host browser configuration for the new Sunstone Eclipse integration and viewport checks.
Edited: 2026-11-22T06:27:56-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/sunstone-eclipse.spec.ts | Verify genuine native Eclipse feature transport, wager terms, feature-line isolation, complete viewport containment, guardian keyboard paths, illustrated rules and legacy routing.
Edited: 2026-11-22T06:22:50-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json | Regenerate authoritative slot guides from current package definitions for Sunstone Eclipse payout scaling and versioned bonus mode.
Edited: 2026-11-22T06:22:50-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Regenerate authoritative slot guides from current package definitions for Sunstone Eclipse payout scaling and versioned bonus mode.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/README.md | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidEndpoints.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidSlotOutcomes.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundModels.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundPolicy.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStateCodec.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStore.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinDraws.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinEvidence.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinReport.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinResponse.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/SunstoneEclipse.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:29:08-06:00 | Computer: VINNABOK | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/SunstoneEclipseTests.cs | Verify guardian geometry/caps, versioned settled proofs, old/new owed carries, exact mode equivalence, and naturally sampled paid/free native visual fixtures.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/Program.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:23:01.7114366-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/SunstoneEclipseSimulation.cs | Implement Sunstone Eclipse guardian-path awards, authoritative feature evidence, frozen legacy math/carry compatibility, targeted checks, and native math qualification as appropriate to this module.
Edited: 2026-11-22T06:40:46-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/sunstone-eclipse/base-eclipse.json | Capture final revised native 0.5/1/2 guardian tiers, 20/40 feature caps and 0.874 core calibration with genuine draw replay and public receipt for browser verification.
Edited: 2026-11-22T06:30:36-06:00 | Computer: VINNABOK | File: tools/FortuneForge.BackendVerification/Program.cs | Add Sunstone-only combined-host HTTP, signed legacy-slot wallet, authoritative feature, ledger deduplication and original audit-intent verification without repeating card gameplay checks.
Edited: 2026-11-22T06:40:46-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/sunstone-eclipse/free-eclipse.json | Capture final revised native 0.5/1/2 guardian tiers, 20/40 feature caps and 0.874 core calibration with genuine draw replay and public receipt for browser verification.
Edited: 2026-11-22T06:31:03-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Submit an exact verified immutable source archive instead of asking the SDK to reevaluate thousands of ignore rules; preserve approved paths, source/package locks and archive provenance.
Edited: 2026-11-22T06:29:00-06:00 | Computer: VINNABOK | File: scripts/create-release-upload-archive.py | Create a gzip tar from only approved locked source records and verify every member type, path, byte size and SHA256 without directory traversal.
Edited: 2026-11-22T06:31:49-06:00 | Computer: VINNABOK | File: scripts/tests/release-upload-archive.tests.ps1 | Check exact archive membership, excluded stray files, immutable archive handles, traversal, mismatched hashes, reparse ancestors and duplicate members using tiny synthetic fixtures.
Edited: 2026-11-22T06:31:23-06:00 | Computer: VINNABOK | File: scripts/tests/release-scope.tests.ps1 | Update the source-submission command expectation to the verified archive path while retaining the existing source-selection fixture.
Edited: 2026-11-22T06:34:00-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/SunstoneEclipse.cs | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T06:36:53-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T06:36:53-06:00 | Computer: VINNABOK | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/SunstoneEclipseTests.cs | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T06:36:53-06:00 | Computer: VINNABOK | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T06:39:17-06:00 | Computer: VINNABOK | File: games/RobotRevolution/README.md | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T06:34:00-06:00 | Computer: VINNABOK | File: tools/FortuneForge.SlotMath/SunstoneEclipseSimulation.cs | Correct Sunwheel tiers to pay every allowed minimum bet, set 20x/40x feature caps, calibrate only new base math, and qualify new rules at minimum and reference stakes; preserve historical math.
Edited: 2026-11-22T07:07:25-06:00 | Computer: VINNABOK | File: config/backend-packages.lock.json | Pin the new Sunstone Eclipse backend package; preserve all 44 other immutable game selections.
Edited: 2026-11-22T07:14:37-06:00 | Computer: VINNABOK | File: tools/FortuneForge.BackendVerification/Program.cs | Correct the verifier journal payload accessor from data to authoritative state; retain full failure diagnostics, emulator project identity and received Sunstone response for integration evidence.
Edited: 2026-11-22T07:47:28-06:00 | Computer: VINNABOK | File: scripts/deploy.ps1 | Route candidate tag activation and cleanup CLI output to the host so readiness returns only the verified revision string.
Edited: 2026-11-22T07:47:28-06:00 | Computer: VINNABOK | File: scripts/tests/shared-backend-candidate-activation.tests.ps1 | Add a selectable stdout regression that emits CLI output during both tag mutations and requires a strict scalar revision result; previous cases were not rerun.
Edited: 2026-11-22T09:28:16-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts | Add opt-in presentation ownership for ambient outcomes, payment alerts and symbol-guide mode.
Edited: 2026-11-22T09:28:16-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SpinButton.tsx | Keep cabinet keyboard shortcuts available while the spin control retains mouse focus.
Edited: 2026-11-22T09:28:16-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts | Preserve text input and dialog controls while allowing spin-focused cabinet keyboard commands.
Edited: 2026-11-22T09:28:16-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Support a collapsed themed symbol modal, palette-aware settings, plain win text and game-owned alert visibility.
Edited: 2026-11-22T09:31:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx | Add a focused regression covering wager and single-spin keyboard commands after clicking the real Spin button.
Edited: 2026-11-22T09:45:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunstoneIntroduction.tsx | Replace the rules essay with an illustrated Fifth Sun introduction and accessible deterministic guardian-path practice using authoritative game geometry, validated symbol lookup, symbol values and payout tiers; explain three/five-symbol Wild substitution accurately.
Edited: 2026-11-22T09:49:20-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/FifthSunIntroduction.css | Add a warm dark page-tinted introduction portal, stone guardian cards, compact responsive path diagram, preserved accessible dialog name and strong game-font entry control; scope heading typography above shared dialog styles and retain original Eclipse art without new effects.
Edited: 2026-11-22T09:40:47-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/config/symbolSets.ts | Declare game-owned historical symbol identities for owed bonus and saved-spin compatibility, while ordinary paid spins keep strict current identity.
Edited: 2026-11-22T09:42:33-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/assets/catalogue/slots/robot-revolution.artwork.json | Select the original Fifth Sun catalogue cover with exact generator prompt, master provenance and measured WebP dimensions/bytes; preserve all prior selected artwork and prompt history.

Edited: 2026-11-22T09:44:46-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Fifth Sun named symbols, original Eclipse, stone guardians, glyphs and explicit historical rendering compatibility.
Edited: 2026-11-22T09:29:18-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/definition.ts | Authoritative Fifth Sun identity and distinct obsidian cabinet palette.
Edited: 2026-11-22T09:29:18-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/catalog.ts | Game-owned Fifth Sun catalogue identity with stable routes and identifiers.
Edited: 2026-11-22T09:44:46-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/manifest.ts | Direct Fifth Sun manifest, R3–R75 wagers, new symbol vocabulary, modal values and ambient outcome presentation.
Edited: 2026-11-22T09:35:06-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sunwheel.ts | Strict version-correlated Fifth Sun and historical feature decoding, shared guide geometry and payout tiers.
Edited: 2026-11-22T09:38:12-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunwheelPanel.tsx | Fifth Sun stone guardian portraits and real Sunwheel calendar orientation.
Edited: 2026-11-22T09:52:21-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Rebuilt Sunwheel sidebar styling and licensed local Fifth Sun display font, without old introduction overrides.
Edited: 2026-11-22T10:08:32-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Larger desktop cabinet beside equal-height Sunwheel, controls beneath, warm utility rails and accessible mobile stacking.
Edited: 2026-11-22T09:43:04-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/README.md | Document Fifth Sun identity, native glyph ownership, historical frames and bundled font license.
Edited: 2026-11-22T09:43:24-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/fifth-sun.test.mjs | Focused new feature-version, geometry and money-evidence decoder regression checks.
Edited: 2026-11-22T09:43:04-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/catalog.ts | Publish Fifth Sun cover and title under existing game identity.
Edited: 2026-11-22T09:43:04-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Use Fifth Sun title while preserving canonical and legacy addresses.
Edited: 2026-11-22T09:43:04-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/sunstoneRetheme.test.ts | Update directly affected identity expectation to Fifth Sun and new cover.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/flame.svg | Original bold stepped flame glyph.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/rain.svg | Original dot-and-bar rain glyph.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/wind.svg | Original carved spiral wind glyph.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/earth.svg | Original stepped earth glyph.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/reed.svg | Original vertical reed glyph.
Edited: 2026-11-22T09:27:52-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/wild.svg | Original angular temple Wild glyph distinct from Sunstone.
Edited: 2026-11-22T09:45:19-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/engineGuide.ts | Preserve Fifth Sun's game-owned vocabulary and independently exported guide instead of rewriting it with classic symbol assumptions.
Edited: 2026-11-22T09:45:19-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/engineGuide.test.ts | Verify Fifth Sun's five glyphs, higher-valued Sunstone, actual scatter and Wild rules against its new native exported contract.

Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: fortuneforge.client/scripts/export-slot-guide-contracts.mjs | Support the game-owned Fifth Sun paytable and wild rules without changing shared base values.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Refresh only Fifth Sun native definition row and affected definition source hashes; retain siblings.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/README.md | Document Fifth Sun symbols, authoritative wagers, wheel rules, preserved versions and local math evidence.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidEndpoints.cs | Update public game error copy to Fifth Sun without changing routes or identifiers.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidSlotOutcomes.cs | Update unsupported-rules error copy to Fifth Sun.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundModels.cs | Update public game error copy to Fifth Sun.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundPolicy.cs | Validate saved prepared wagers and advance bonuses under their recorded engine version.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStateCodec.cs | Use Fifth Sun display name while preserving legacy state ownership and wager import.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStore.cs | Use Fifth Sun display name while preserving persistence identities.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinDraws.cs | Update replay failure copy to Fifth Sun.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinEvidence.cs | Validate feature proof using its recorded engine version and update public game copy.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinReport.cs | Update report-validation error copy to Fifth Sun.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinResponse.cs | Capture new or historical wheel metadata through the version-aware engine contract.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunDefinitions.cs | Own Fifth Sun symbols, reel strips, paytable and R3–R75 paid wagering.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunWheel.cs | Apply Fifth Sun wire vocabulary to the existing authoritative Sunwheel mechanic.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs | Add versioned Fifth Sun engine at 1.14 while preserving frozen Sunstone and Robot math.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/SunstoneEclipse.cs | Share game-owned wheel geometry across explicit frozen and new symbol vocabularies.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunMathTests.cs | Qualify only new native paid cycles and export genuine verified paid/free visual responses.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunTests.cs | Add focused vocabulary, wager, three-version replay, tamper, carry-marker and native guide checks.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs | Adapt affected current paid persistence fixtures to new minimum wager and native scatter strips.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionSpinReportingTests.cs | Verify current symbols on all23 paths while keeping imported historical bonus strips frozen.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/SunstoneEclipseTests.cs | Pin historical Sunstone fixtures to their explicit frozen version and mode.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json | Refresh only Robot catalogue row with native Fifth Sun paytable, multiplier and bonus marker.
Edited: 2026-11-22T09:45:36-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotMath/SunstoneEclipseSimulation.cs | Resolve current native wheel and bonus mode through the version-aware engine contract.
Edited: 2026-11-22T10:12:30-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/fifth-sun-review.spec.ts | Add seven focused Fifth Sun review cases for desktop/laptop/phone geometry, symbol containment, introduction/modal/keyboard controls, exact native paid/free and historical carry fixtures, actual animation keyframes/reduced motion, and peak-size footer clearance; intercept every account and wallet API and cache unique green evidence.
Edited: 2026-11-22T09:47:57-06:00 | Computer: VINNABOK | File: fortuneforge.client/playwright.fifth-sun.config.ts | Isolate the new redesign browser cases and evidence directory against the root-owned local Vite server; never start or stop another server.
Edited: 2026-11-22T09:55:50-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-loading/loadingPresentationsC.ts | Use the new Fifth Sun name and selected cover while its lazy game package loads.
Edited: 2026-11-22T09:55:50-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Keep the managed catalogue display name aligned with Fifth Sun while preserving the game and route identifiers.
Edited: 2026-11-22T09:56:03-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/fifth-sun/native-paid-spin.json | Admit the exact frozen native Fifth Sun browser fixture or origin metadata byte-for-byte; preserve captured terms, symbols, response fields and source proof without resampling or payout changes.
Edited: 2026-11-22T09:56:03-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/fifth-sun/native-paid-origin.json | Admit the exact frozen native Fifth Sun browser fixture or origin metadata byte-for-byte; preserve captured terms, symbols, response fields and source proof without resampling or payout changes.
Edited: 2026-11-22T09:56:03-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/fifth-sun/native-free-spin.json | Admit the exact frozen native Fifth Sun browser fixture or origin metadata byte-for-byte; preserve captured terms, symbols, response fields and source proof without resampling or payout changes.
Edited: 2026-11-22T09:56:03-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/fifth-sun/native-free-origin.json | Admit the exact frozen native Fifth Sun browser fixture or origin metadata byte-for-byte; preserve captured terms, symbols, response fields and source proof without resampling or payout changes.
Edited: 2026-11-22T10:01:21-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts | Validate reel and paid-match symbols against the same game-owned definitions, preserving strict legacy fallback validation.
Edited: 2026-11-22T10:01:21-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.fifthSun.test.ts | Verify a genuine native Fifth Sun ordinary-plus-guardian payout and reject an undeclared paid-match symbol.
Edited: 2026-11-22T10:44:46-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Reduce desktop Fifth Sun footprint to a centered 68rem group, 28rem board and 17–19rem Sunwheel while preserving phone layout.
Edited: 2026-11-22T10:46:58-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Compact only desktop Sunwheel header, guardian rows and result spacing to restore a readable calendar inside the reduced 448px panel.
Edited: 2026-11-22T10:49:05-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Fit the complete cabinet artwork to the frame so all carved edges remain visible without changing the layout size.
Edited: 2026-11-22T10:49:21-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/assets/art/fifth-sun-glyphs/wild.svg | Replace literal W with an original obsidian diamond emblem, stepped gold shoulders and turquoise inlay; preserve Wild identity and behavior.
Edited: 2026-11-22T10:57:38-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Remove the extra yellow outline and inset frame while preserving the full carved cabinet artwork.
Edited: 2026-11-22T11:00:16-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Rebuild Sunwheel as carved slate continuation of the cabinet with proportional nine-slice artwork, larger title/calendar and readable themed text.
Edited: 2026-11-22T11:00:16-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunwheelPanel.tsx | Remove idle dashes and repeated filler, show only earned awards, and condense truthful accessible Sunwheel status without changing feature behavior.
Edited: 2026-11-22T11:03:27-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Select the three new sculptural stone v4 guardian rasters through existing symbol ownership; preserve all other symbols, mappings, gameplay and presentation code.
Edited: 2026-11-22T11:03:27-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Select the three new sculptural stone v4 guardian rasters through existing symbol ownership; preserve all other symbols, mappings, gameplay and presentation code.
Edited: 2026-11-22T11:08:45-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Center guardian portraits and labels together within each Sunwheel row, including optional earned awards and path previews.
Edited: 2026-11-22T11:16:14-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Select museum-reference-driven v5 stone guardian art: profile eagle head, continuous carved jaguar mask and incised serpent; preserve every other symbol, gameplay and UI/style owner.
Edited: 2026-11-22T11:16:14-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Select museum-reference-driven v5 stone guardian art: profile eagle head, continuous carved jaguar mask and incised serpent; preserve every other symbol, gameplay and UI/style owner.
Edited: 2026-11-22T11:41:49-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Integrate the exact user-approved jaguar v10 and serpent/eagle v6 masters as padded transparent approved-v1 assets; change only guardian image imports and preserve feather, glyphs, roster, mechanics and styling.
Edited: 2026-11-22T11:41:49-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Integrate the exact user-approved jaguar v10 and serpent/eagle v6 masters as padded transparent approved-v1 assets; change only guardian image imports and preserve feather, glyphs, roster, mechanics and styling.
Edited: 2026-11-22T11:45:19-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/flame.svg | Author review-only flat carved jade flame glyph with muted incision edges and broad readable silhouette.
Edited: 2026-11-22T11:45:19-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/rain.svg | Author review-only flat carved jade rain glyph with muted incision edges and broad readable silhouette.
Edited: 2026-11-22T11:46:01-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/wind.svg | Author review-only flat carved jade wind glyph with muted incision edges and broad readable silhouette.
Edited: 2026-11-22T11:46:01-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/earth.svg | Author review-only flat carved jade earth glyph with muted incision edges and broad readable silhouette.
Edited: 2026-11-22T11:45:19-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/reed.svg | Author review-only flat carved jade reed glyph with muted incision edges and broad readable silhouette.
Edited: 2026-11-22T11:45:19-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/glyph-study-v1/render-preview.mjs | Render review-only jade glyph study with large views and actual 80px icons using existing Sharp; no live integration.
Edited: 2026-11-22T11:57:22-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/jade-symbol-previews-v2/render-preview-board.mjs | Add a preview-only five-column jade-symbol board renderer using exact unchanged master copies, large symbols and actual80px thumbnails; wait for the genuine Temple master and never modify game assets or the parent-owned prompt record.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/10.svg | Author legible jade-faced 10 rank-character vector with cut corners, heavy serifs and muted carved edges for review only.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/j.svg | Author legible jade-faced J rank-character vector with cut corners, heavy serifs and muted carved edges for review only.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/q.svg | Author legible jade-faced Q rank-character vector with cut corners, heavy serifs and muted carved edges for review only.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/k.svg | Author legible jade-faced K rank-character vector with cut corners, heavy serifs and muted carved edges for review only.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/a.svg | Author legible jade-faced A rank-character vector with cut corners, heavy serifs and muted carved edges for review only.
Edited: 2026-11-22T11:59:37-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/rank-character-study-v1/render-preview.mjs | Render optional rank-character direction with large views and actual 80px row using existing Sharp, outside the live game.
Edited: 2026-11-22T12:02:06-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Select only the five exact user-approved common-color jade low-symbol images; preserve all stable identities, labels, pay rules, weights, other artwork and game behavior.
Edited: 2026-11-22T12:02:06-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Select only the five exact user-approved common-color jade low-symbol images; preserve all stable identities, labels, pay rules, weights, other artwork and game behavior.
Edited: 2026-11-22T12:02:06-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-redesign/export-approved-jade-symbols.mjs | Export exact approved jade master copies to padded384px lossless-alpha WebP, verify unchanged input hashes, copy source/dist assets and retain approval/prompt provenance without regeneration or color changes.
Edited: 2026-11-22T12:05:10-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Integrate the exact approved Quetzal headdress with its shared historical artwork label, and restore original pointed Golden Sunstone artwork as the existing Golden Sun Wild; preserve IDs, roster, calendar Sunstone, other art and all game math.
Edited: 2026-11-22T12:05:10-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Integrate the exact approved Quetzal headdress with its shared historical artwork label, and restore original pointed Golden Sunstone artwork as the existing Golden Sun Wild; preserve IDs, roster, calendar Sunstone, other art and all game math.
Edited: 2026-11-22T12:26:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Add exact approved Lunar artwork to the current Eclipse visual family alongside unchanged Solar, retaining historical Solar-only aliases and all native IDs/math; state the current combined scatter rule.
Edited: 2026-11-22T12:26:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunstoneIntroduction.tsx | Illustrate both approved Eclipse appearances using the shared artwork helper and existing pair layout; explain their combined current seven-spin award without changing game behavior.
Edited: 2026-11-22T12:28:45-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Emit only the changed approved Eclipse artwork-family configuration and illustrated current-rule introduction after the isolated game TypeScript check passed against fresh core artwork declarations.
Edited: 2026-11-22T12:28:45-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/SunstoneIntroduction.js | Emit only the changed approved Eclipse artwork-family configuration and illustrated current-rule introduction after the isolated game TypeScript check passed against fresh core artwork declarations.
Edited: 2026-11-22T12:26:12-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/config/symbolSets.ts | Add optional full artwork variants and pure deterministic coordinate selection while preserving legacy symbol identity and defaults.
Edited: 2026-11-22T12:26:12-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SlotSymbol.tsx | Render only selected artwork image, animation, responsive sources and accessible label; preserve logical symbol and win/value behavior.
Edited: 2026-11-22T12:26:12-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SymbolValueGuide.tsx | Show all configured forms in one logical guide entry with one payout and independent accessible artwork labels.
Edited: 2026-11-22T12:26:12-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/styles/page.css | Add opt-in multi-artwork guide layout with readable paired images while preserving singleton styles.
Edited: 2026-11-22T12:27:09-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/symbolArtworkVariants.test.tsx | Add seven focused optional artwork tests for stability, identity, metadata isolation, accessibility, legacy defaults and one-row guide display.
Edited: 2026-11-22T13:08:11-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/experience.ts | Carry game-owned paid mode state, lock paid wagers, and coordinate feature presentation without treating rounds as free spins.
Edited: 2026-11-22T13:08:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/types/slots.ts | Carry game-owned paid mode state, lock paid wagers, and coordinate feature presentation without treating rounds as free spins.
Edited: 2026-11-22T13:08:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/services/slotsApi.ts | Carry game-owned paid mode state, lock paid wagers, and coordinate feature presentation without treating rounds as free spins.
Edited: 2026-11-22T13:08:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts | Carry game-owned paid mode state, lock paid wagers, and coordinate feature presentation without treating rounds as free spins.
Edited: 2026-11-22T13:08:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Carry game-owned paid mode state, lock paid wagers, and coordinate feature presentation without treating rounds as free spins.
Edited: 2026-11-22T13:08:50.8467182-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunTotalityMathTests.cs | Added isolated paid-Eclipse and complete-cycle native math qualification with wager-volume denominators and batch confidence intervals.
Edited: 2026-11-22T13:11:52.1127584-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunTotalityDefinitions.cs | Added versioned paid Eclipse definitions and static strips; calibrated ordinary and Eclipse return multipliers.
Edited: 2026-11-22T13:13:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx | Verify paid-mode charging, wager and presentation locks; derive client Eclipse guide metadata from the native game constants.
Edited: 2026-11-22T13:13:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/services/slotsApi.fifthSun.test.ts | Verify paid-mode charging, wager and presentation locks; derive client Eclipse guide metadata from the native game constants.
Edited: 2026-11-22T13:13:45-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/export-eclipse-math.mjs | Verify paid-mode charging, wager and presentation locks; derive client Eclipse guide metadata from the native game constants.
Edited: 2026-11-22T13:13:45-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/eclipseMath.generated.ts | Verify paid-mode charging, wager and presentation locks; derive client Eclipse guide metadata from the native game constants.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sunwheel.ts | Validate versioned paid Eclipse alignment sequences, native scales, guardian memory and settlement totals while preserving saved bonus contracts.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunwheelPanel.tsx | Present Solar and Lunar paid modes, animate three Sunwheel stops, illuminate guardian seals and report presentation completion.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Use distinct centre-reel Solar and Lunar symbols, preserve historical identities and disclose mode-specific award values.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/manifest.ts | Register the Totality presentation, native synthetic award paths, paid-mode rules and historical bonus labels.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunstoneIntroduction.tsx | Explain one-symbol centre triggers, paid Eclipse rounds, linked alignments and separate ordinary/Eclipse paytables.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Style Eclipse sequence progress, rotating rings and awakened cabinet guardian carvings with reduced-motion support.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Transform the existing temple and cabinet lighting for Solar and Lunar Eclipse modes.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/package.json | Regenerate native Eclipse math metadata before package checks and builds.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/sunwheel.test.mjs | Cover native v3 Solar/Lunar sequence integrity, award scale checks and historical feature compatibility.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/sunwheel.js | Emit the validated paid Eclipse feature decoder.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/SunwheelPanel.js | Emit the sequenced Eclipse presentation.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/symbols.js | Emit distinct Eclipse identities and mode award guide entries.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/manifest.js | Emit the registered paid Eclipse experience.
Edited: 2026-11-22T13:17:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/dist/SunstoneIntroduction.js | Emit the updated Totality instructions and paytables.
Edited: 2026-11-22T13:18:20-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/README.md | Document the paid Totality sequence, host locking, native math guide ownership and historical feature compatibility.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotRoundStateCodec.cs | Add opt-in opaque native state transport and equality, preserving omitted historical state.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs | Version paid Totality engine separately and preserve all historical engines and replay terms.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunTotalityWheel.cs | Resolve three Eclipse alignments, round guardian memory and a single heart-sensitive Totality award.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/SunstoneEclipse.cs | Extend feature evidence with opt-in v3 alignment fields while omitting fields from historical evidence.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/FifthSunPaidModeState.cs | Own strict versioned paid Eclipse state validation and canonical persistence.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStateCodec.cs | Enable only Fifth Sun native paid state in the shared opt-in storage codec.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundModels.cs | Expose paid mode independently of historical free spins and describe locked mode stake conflicts.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundPolicy.cs | Enforce paid Eclipse stake and progress exactly five charged rounds without retriggers.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidEndpoints.cs | Return the explicit paid-mode-wager-locked conflict for protected Eclipse wagers.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinResponse.cs | Include authoritative paid mode state on settled responses and protected captures.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinEvidence.cs | Verify archived paid mode state against the protected native progression.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinReport.cs | Retain paid mode state in frozen settlement recovery.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunTests.cs | Pin historical Fifth Sun replay and bonus contract tests to their original v1 math.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunMathTests.cs | Pin existing qualified simulation and historical visual fixtures to v1 without repeating their math run.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunTotalityTests.cs | Verify paid Eclipse persistence, pricing, replay and native read-only visual fixtures.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/RobotRevolutionRoundStoreTests.cs | Adapt the paid trigger persistence case and cover idempotent paid-mode reply recovery.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun/update-guide.mjs | Synchronize the native Totality guide and build a transaction-free preview of real native Eclipse outcomes.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json | Synchronize the native Totality guide and build a transaction-free preview of real native Eclipse outcomes.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Synchronize the native Totality guide and build a transaction-free preview of real native Eclipse outcomes.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun-totality/preview.html | Synchronize the native Totality guide and build a transaction-free preview of real native Eclipse outcomes.
Edited: 2026-11-22T13:20:14-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun-totality/preview.mjs | Synchronize the native Totality guide and build a transaction-free preview of real native Eclipse outcomes.
Edited: 2026-11-22T13:23:53-06:00 | Computer: Vinnabok | File: games/RobotRevolution/README.md | Describe single centre-reel triggers, paid Totality rounds, historical compatibility and measured conditional versus complete-game return.
Edited: 2026-11-22T13:26:00-06:00 | Computer: Vinnabok | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunTotalityWheel.cs | Include the calibrated Eclipse wheel scale in reported match multiplier metadata; payout calculation is unchanged.
Edited: 2026-11-22T13:29:55-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun-totality/stage-local.mjs | Stage only the new local Fifth Sun package, verify all frozen sibling binaries and preserve the production manifest.
Edited: 2026-11-22T13:34:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/FifthSunDemo.cs | Reuse native paid Eclipse validation, progression and spin feature responses for nonfinancial demo play.
Edited: 2026-11-22T13:34:36-06:00 | Computer: Vinnabok | File: games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests/FifthSunDemoTests.cs | Verify centre triggers, all five paid demo rounds, mode wager locking and malformed carry rejection.
Edited: 2026-11-22T13:34:36-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Controllers/DemoSlotsController.cs | Dispatch current Fifth Sun paid demos to the game-owned adapter while retaining historical free demo handling.
Edited: 2026-11-22T13:37:22-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Retain the three- and five-symbol Wild substitution explanation alongside the revised Eclipse guide.
Edited: 2026-11-22T13:38:41-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun-totality/launch-local.ps1 | Run the single local backend against the owned loopback emulator, mock payments and verified local package composition.
Edited: 2026-11-22T13:41:48-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Games/FifthSunDemoEndpoints.cs | Expose only Fifth Sun native nonfinancial demo status/spins with paid-mode state and scoped validation.
Edited: 2026-11-22T13:41:48-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs | Allow metadata-marked Fifth Sun demo endpoints through the existing demo block while retaining other game demo restrictions.
Edited: 2026-11-22T13:46:25-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/__fixtures__/controller-eclipse-native.json | Capture actual native trigger and mode responses for demo paid-state transport verification.
Edited: 2026-11-22T13:46:25-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs | Restrict the native nonfinancial verification bridge to Development and preserve retired public demo routes.
Edited: 2026-11-22T14:26:04-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Reduce cabinet footprint, keep the wheel above centred guardian cards, and fit the preview with visible spin controls.
Edited: 2026-11-22T14:26:04-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Reduce cabinet footprint, keep the wheel above centred guardian cards, and fit the preview with visible spin controls.
Edited: 2026-11-22T14:26:04-06:00 | Computer: Vinnabok | File: artifacts/fifth-sun-totality/preview.mjs | Reduce cabinet footprint, keep the wheel above centred guardian cards, and fit the preview with visible spin controls.
Edited: 2026-11-22T15:05:40-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Lower the cabinet, Sunwheel and controls by up to 32px, retaining space on shorter viewports.
Edited: 2026-11-22T16:08:08-06:00 | Computer: Vinnabok | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Match Neon's Auto spin, Bet, Spin control order while retaining the fitted layout and existing keyboard order.
Edited: 2026-11-22T16:15:21-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-releases/productionGameRelease.ts | Enable Fifth Sun alongside Neon in the existing independent production browser release loader.
Edited: 2026-11-22T16:15:21-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-releases/useGameRelease.ts | Enable Fifth Sun alongside Neon in the existing independent production browser release loader.
Edited: 2026-11-22T16:16:32-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/SunstoneIntroduction.tsx | Use explicit JavaScript module specifiers so the release dependency scanner resolves generated Eclipse constants.
Edited: 2026-11-22T16:16:32-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sunwheel.ts | Use explicit JavaScript module specifiers so the release dependency scanner resolves generated Eclipse constants.
Edited: 2026-11-22T16:16:32-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/symbols.ts | Use explicit JavaScript module specifiers so the release dependency scanner resolves generated Eclipse constants.
Edited: 2026-11-22T16:17:56-06:00 | Computer: VINNABOK | File: config/backend-packages.lock.json | Select Fifth Sun release 582eb047-c87e-4c30-a605-10e74c712413 while preserving all 44 sibling backend packages.
Edited: 2026-11-22T16:19:42-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/export-eclipse-math.mjs | Preserve unchanged generated metadata during immutable release builds; rewrite only when native constants change.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/RobotRevolutionEngine.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/src/FortuneForge.Games.RobotRevolution/FifthSunDefinitions.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Games/FifthSunDemoEndpoints.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinResponse.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinReport.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinEvidence.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionSpinDraws.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStore.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundStateCodec.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionRoundModels.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidSlotOutcomes.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/RobotRevolutionPaidEndpoints.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/server/FortuneForge.Games.RobotRevolution.Server/FifthSunPaidModeState.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/README.md | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/scripts/fifth-sun.test.mjs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/README.md | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/FifthSunIntroduction.css | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/manifest.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/definition.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/runtimeOverrides.css | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/styles.css | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/fifth-sun-review.spec.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/sunstoneRetheme.test.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/catalog.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.fifthSun.test.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-loading/loadingPresentationsC.ts | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:11:22-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/assets/catalogue/slots/robot-revolution.artwork.json | Standardize current game branding, messages and selected artwork on Sunstone Eclipse while preserving persisted identifiers and artwork history.
Edited: 2026-11-22T17:13:42-06:00 | Computer: Vinnabok | File: package.json | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:42-06:00 | Computer: Vinnabok | File: fortuneforge.client/package.json | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:42-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:42-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalog.ts | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/manifestLoaders/high-noon-fortune.ts | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/FrontierFortuneHost.tsx | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/FrontierFortuneBundled.ts | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/game-release.json | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: scripts/game-smoke-inventory.mjs | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:13:43-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Register the fresh Frontier Fortune client, canonical routes, and catalogue while retaining account identifiers.
Edited: 2026-11-22T17:14:06-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Refresh only two native source hashes after reviewed branding-only exception/comment edits; retain prior exported native payloads, capture time and assembly identity.
Edited: 2026-11-22T17:01:23-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts | Add neutral persistent game feature panel and transport contracts.
Edited: 2026-11-22T17:01:23-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/types/slots.ts | Carry opaque game-owned feature state and required choice state in spin replies.
Edited: 2026-11-22T17:03:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts | Add authenticated and stateful demo feature transport with prepared Crooked Road spins and shared state decoding.
Edited: 2026-11-22T17:04:31-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts | Coordinate persistent feature hydration/actions, stateful demos, required choices and synchronous spin exclusion.
Edited: 2026-11-22T17:05:21-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Render game-owned persistent interactions and disable all spin controls during actions and required choices.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: package.json | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/package.json | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/vite.config.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeRegistry.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalog.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.test.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/__fixtures__/verifyLoadingB.mjs | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/cabinetThemes.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/soundSets.test.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: scripts/game-smoke-inventory.mjs | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: scripts/prepare-slot-title-card.mjs | Replace retired Samurai client registration with the independent Crooked Road game.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/themedSlotAudio.ts | Remove the retired game from legacy themed audio routing; Crooked Road owns fresh audio.
Edited: 2026-11-22T17:08:26-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/additionalSymbols.test.tsx | Use an active classic game as the shared-builder regression fixture after Samurai retirement.
Edited: 2026-11-22T17:08:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/manifestLoaders/crooked-road.ts | Load the fresh Crooked Road manifest lazily.
Edited: 2026-11-22T17:08:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/manifestLoaders/samurai-fortune.ts | Remove the retired Samurai manifest loader.
Edited: 2026-11-22T17:08:27-06:00 | Computer: VINNABOK | File: games/CrookedRoad/game-release.json | Register independent Crooked Road browser and backend package release ownership.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: scripts/game-worker-release.mjs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Activity/AcceptedGameActivity.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Abstractions/GameAnalyticsSettlement.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Abstractions/AcceptedGameParticipationDocuments.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/CatalogContract.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Bonuses/SlotSpecialRoundProfiles.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotSpecialRoundProfilesTests.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotMathAuditTests.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: config/backend-host.json | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:12:34-06:00 | Computer: Vinnabok | File: config/backend-packages.lock.json | Retire Samurai bindings and register independent Crooked Road ownership; preserve the other 44 published backend selections.
Edited: 2026-11-22T17:13:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgeStartup.cs | Compose game-owned Crooked Road demo routes and authenticated feature proxying through shared host plumbing.
Edited: 2026-11-22T17:13:08-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Configuration/FortuneForgePipeline.cs | Compose game-owned Crooked Road demo routes and authenticated feature proxying through shared host plumbing.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/package.json | Create independent Crooked Road client package and public exports.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tsconfig.json | Define strict browser TypeScript checking for the fresh client.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tsconfig.build.json | Define declaration and module output for the fresh client.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/scripts/copy-static.mjs | Synchronize the generated canonical rule contract and copy package-owned artwork, audio and CSS to distribution.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/symbols.ts | Own approved Irish symbol and feature artwork imports with fresh exports.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/catalog.ts | Derive fresh catalogue metadata from the Crooked Road definition.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/styles.css | Add clover celebration and import scoped Irish theme overrides.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/runtimeOverrides.css | Style the fresh cabinet in emerald, brass and moss with mobile sizing.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/README.md | Document fresh client boundaries, build commands and audio reproduction.
Edited: 2026-11-22T17:00:08-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/assets/art/irish/README.md | Document approved art exports and inactive shared-builder aliases.
Edited: 2026-11-22T17:03:44-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/definition.ts | Define fresh Irish presentation and derive its guide from the authoritative server paytable.
Edited: 2026-11-22T17:09:06-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/sounds.ts | Map original Irish harp, stone and coin cues through shared host audio controls.
Edited: 2026-11-22T17:09:06-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/assets/audio/README.md | Document reproducible original synthesis and audio QA.
Edited: 2026-11-22T17:08:05-06:00 | Computer: Vinnabok | File: games/CrookedRoad/tools/render-audio.py | Render an original deterministic harp, stone and coin suite without recordings or external samples.
Edited: 2026-11-22T17:08:57-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadPanel.tsx | Add authoritative road edits, Finbar traversal, ticket-gated journeys and three distinct bonus displays.
Edited: 2026-11-22T17:08:14-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadPanel.css | Scope responsive road and bonus styles to approved art with reduced motion.
Edited: 2026-11-22T17:08:57-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/road.ts | Own public contracts, strict decoders and canonical reciprocal connectivity.
Edited: 2026-11-22T17:08:57-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadHelp.tsx | Explain the fresh five-by-three ten-line game and journey rules.
Edited: 2026-11-22T17:05:22-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadHelp.css | Style the game-owned guide, payout tables and payline diagrams.
Edited: 2026-11-22T17:09:58-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/manifest.ts | Compose canonical wagering, nine active symbols, panel, help and neutral paid/demo transport.
Edited: 2026-11-22T17:03:43-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/index.ts | Export the independent game client modules.
Edited: 2026-11-22T17:07:27-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/road.test.mjs | Test connectivity, versioned snapshots and hidden prize validation.
Edited: 2026-11-22T17:11:21-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/CrookedRoadPanel.test.tsx | Test nonoptimistic actions, duplicate guards, keyboard access, ticket gates and distinct bonus controls.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: scripts/verify-slot-package-boundaries.mjs | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/worker.props | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Bonuses/SlotSpecialRoundProfiles.cs | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:14:46-06:00 | Computer: Vinnabok | File: package-lock.json | Register fresh Frontier backend ownership and workspace; retire the old generic spinner from active routing.
Edited: 2026-11-22T17:15:43-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-totality/preview.html | Match the local native visual preview document title to Sunstone Eclipse.
Edited: 2026-11-22T17:16:10-06:00 | Computer: VINNABOK | File: artifacts/fifth-sun-totality/preview.mjs | Match the visual preview toolbar branding to Sunstone Eclipse.
Edited: 2026-11-22T17:16:13-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Spins/SpinService.Execution.cs | Route Frontier through its native worker and reject generic classic outcome fallbacks.
Edited: 2026-11-22T17:16:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs | Route Frontier through its native worker and reject generic classic outcome fallbacks.
Edited: 2026-11-22T17:16:14-06:00 | Computer: Vinnabok | File: config/backend-host.json | Route Frontier through its native worker and reject generic classic outcome fallbacks.
Edited: 2026-11-22T17:16:14-06:00 | Computer: Vinnabok | File: scripts/game-worker-release.mjs | Route Frontier through its native worker and reject generic classic outcome fallbacks.
Edited: 2026-11-22T17:16:33-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Update the current Sunwheel contract test description to Sunstone Eclipse without changing its assertions or concurrent game work.
Edited: 2026-11-22T17:17:37-06:00 | Computer: Vinnabok | File: fortuneforge.client/build/slotPackageIsolation.ts | Use Frontier artwork and explicit package-owned loading boundaries.
Edited: 2026-11-22T17:17:37-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsA.ts | Use Frontier artwork and explicit package-owned loading boundaries.
Edited: 2026-11-22T17:17:37-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts | Use Frontier artwork and explicit package-owned loading boundaries.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/frontier-rules.json | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/FortuneForge.Games.FrontierFortune.csproj | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/FrontierRules.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/FrontierBounties.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/FrontierFortuneEngine.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FortuneForge.Games.FrontierFortune.Server.csproj | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundModels.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundPolicy.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierLegacyGate.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundStore.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundService.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierPaidEndpoints.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:17:58-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/GameWorkerModule.cs | Summary: Implement fresh Frontier rules, tactical bonus, authoritative prepared settlement and worker APIs.
Edited: 2026-11-22T17:18:14-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/FrontierOwnershipTests.cs | Add regression coverage that Frontier metadata resolves to its fresh symbols while generic host spins and classic paid worker transport stay closed.
Edited: 2026-11-22T17:18:37-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Delegate Frontier native proof replay to its game-owned inspector while retaining historical audits.
Edited: 2026-11-22T17:20:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/README.md | Document fresh Frontier ownership, shared rules, Deadeye Bounties, native worker recovery, stable ledger identities, preserved legacy obligations and focused verification commands.
Edited: 2026-11-22T17:21:27-06:00 | Computer: Vinnabok | File: tools/artwork/frontier-runtime.mjs | Preserve original generated Frontier art and derive optimized runtime assets with alpha.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/package.json | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tsconfig.json | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tsconfig.build.json | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/copy-assets.mjs | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/sync-rules.mjs | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/api.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/art.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/BonusBoard.tsx | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/browser.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierFortuneGame.tsx | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/index.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/manifest.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/rules.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/types.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/useFrontier.ts | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tests/api.test.mjs | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/README.md | Author fresh paid-server-only Frontier Fortune native client, tactical bounty interface, metadata, recovery and contract validation.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/demo.ts | Remove temporary client outcome prototype after paid-only scope instruction.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/demo-model.mjs | Remove temporary client outcome prototype after paid-only scope instruction.
Edited: 2026-11-22T17:23:11-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/preview/frontier-fortune/frontier-model.mjs | Move temporary prototype into new game package, then remove it for paid-only production gameplay.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierEvidence.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FortuneForge.Games.FrontierFortune.Tests.csproj | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FrontierEngineTests.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FrontierSettlementTests.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FrontierEvidenceTests.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/FortuneForge.Games.FrontierFortune.LocalHarness.csproj | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/FrontierHarnessWallet.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:24:18-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/Program.cs | Summary: Verify native Frontier replay, transactional recovery and same-service paid local harness with durable isolated test wallet.
Edited: 2026-11-22T17:25:06-06:00 | Computer: Vinnabok | File: scripts/verify-game-route-inventory.mjs | Verify paid-only slot routes without requiring a public demo alias.
Edited: 2026-11-22T17:25:39-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/FortuneForge.Games.FrontierFortune.Math.csproj | Add isolated automated economics qualification using native line evaluation and bounty transitions, seeded layout sampling and beam planning.
Edited: 2026-11-22T17:25:39-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/Program.cs | Add isolated automated economics qualification using native line evaluation and bounty transitions, seeded layout sampling and beam planning.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/App.test.ts | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.test.ts | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/index.html | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/main.tsx | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/shell.css | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:25:47-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/vite.config.mjs | Verify Frontier native paid-only contracts and supply local production-API infrastructure mount without alternate gameplay.
Edited: 2026-11-22T17:29:46-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/firebase.json | Summary: Keep isolated paid validation emulator ports in reproducible test-owned configuration.
Edited: 2026-11-22T17:29:55-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/high-noon-fortune.artwork.json | Select newly generated Frontier Fortune catalogue artwork and record its actual optimized dimensions and bytes.
Edited: 2026-11-22T17:29:57-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tests/paid-browser.smoke.mjs | Verify real paid spins, natural bounty triggers, server previews, reload persistence and banking using isolated test funds.
Edited: 2026-11-22T17:31:50-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/soundSets.test.ts | Exclude retired HighNoon sound package and verify Frontier native audio metadata does not inherit legacy effects.
Edited: 2026-11-22T17:32:46-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs | Update the active former High Noon ownership case to Frontier worker identity and owner field while preserving sibling and historical tests.
Edited: 2026-11-22T17:33:50-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/account/AccountHomePage.tsx | Reorder featured slots so Sunstone Eclipse, The Crooked Road and Frontier Fortune precede Gold Foundry, Midnight Vault follows it, and Pirates Fortune and Reel Riches precede Rainbow Realm.
Edited: 2026-11-22T17:33:50-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/account/AccountHomePage.test.tsx | Reorder featured slots so Sunstone Eclipse, The Crooked Road and Frontier Fortune precede Gold Foundry, Midnight Vault follows it, and Pirates Fortune and Reel Riches precede Rainbow Realm.
Edited: 2026-11-22T17:35:07-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.catalogue.test.tsx | Preserve paid-only route fixture literal types after nullable demo/play contract migration.
Edited: 2026-11-22T17:40:37-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/SpinButton.tsx | Prevent primary mouse presses from leaving Spin focused for the mute-key focus ring, while preserving keyboard focus, activation and context-menu behavior.
Edited: 2026-11-22T17:40:37-06:00 | Computer: VINNABOK | File: packages/slot-client-core/src/components/spinButtonFocus.test.tsx | Prevent primary mouse presses from leaving Spin focused for the mute-key focus ring, while preserving keyboard focus, activation and context-menu behavior.
Edited: 2026-11-22T17:41:01-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/assets/catalogue/slots/treasure-cove-title-v1.webp | Select built-in imagegen Treasure Cove cover with exact updated title while preserving previous catalogue artwork and generation history.
Edited: 2026-11-22T17:41:01-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/assets/catalogue/slots/pirates-fortune.artwork.json | Select built-in imagegen Treasure Cove cover with exact updated title while preserving previous catalogue artwork and generation history.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/identity.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/catalog.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/cabinetTheme.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/catalog.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/game-loading/loadingPresentationsA.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:41:05-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Rename current Pirates presentation to Treasure Cove and select the updated catalogue cover while retaining stable routes and game identity.
Edited: 2026-11-22T17:42:18-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/manifest.ts | Add the on-demand Treasure Cove guide with themed illustrated rules, native-contract symbol values and gem bonus explanations; keep symbols modal and disable payment alerts.
Edited: 2026-11-22T17:42:18-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/TreasureCoveIntroduction.tsx | Add the on-demand Treasure Cove guide with themed illustrated rules, native-contract symbol values and gem bonus explanations; keep symbols modal and disable payment alerts.
Edited: 2026-11-22T17:42:18-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/TreasureCoveIntroduction.css | Add the on-demand Treasure Cove guide with themed illustrated rules, native-contract symbol values and gem bonus explanations; keep symbols modal and disable payment alerts.
Edited: 2026-11-22T17:43:32-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Replace the permanent Pirates help dock with on-demand help, show Treasure Cove above the cabinet, align themed controls below it, and place utility buttons clear of reporting.
Edited: 2026-11-22T17:43:32-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/runtimeOverrides.css | Replace the permanent Pirates help dock with on-demand help, show Treasure Cove above the cabinet, align themed controls below it, and place utility buttons clear of reporting.
Edited: 2026-11-22T17:43:32-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/piratesSpinControls.css | Replace the permanent Pirates help dock with on-demand help, show Treasure Cove above the cabinet, align themed controls below it, and place utility buttons clear of reporting.
Edited: 2026-11-22T17:17:41-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/manifest.ts | Make The Crooked Road paid-only and keep local tests on the same paid game contracts.
Edited: 2026-11-22T17:17:41-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/slotGameManifest.ts | Make The Crooked Road paid-only and keep local tests on the same paid game contracts.
Edited: 2026-11-22T17:17:41-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalogTypes.ts | Make The Crooked Road paid-only and keep local tests on the same paid game contracts.
Edited: 2026-11-22T17:19:34-06:00 | Computer: VINNABOK | File: C:/Users/Lctiv/.codex/AGENTS.md | Add the owner's plain-English rule requiring paid-only products and identical production gameplay during isolated local testing.
Edited: 2026-11-22T17:21:06-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotPaidRoundStore.cs | Allow fresh paid games to omit legacy free-game settlement terms while rejecting legacy state and preserving existing game defaults.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/assets/catalogue/slots/samurai-fortune.artwork.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/README.md | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/package.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/scripts/copy-static.mjs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/assets/audio/manifest.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/catalog.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/definition.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/index.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/manifest.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/runtimeOverrides.css | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/sounds.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/styles.css | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/src/symbols.ts | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/tsconfig.build.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/client/FortuneForge.Games.SamuraiFortune.Client/tsconfig.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/game-release.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/FortuneForge.Games.SamuraiFortune.Server.csproj | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/Program.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortunePaidEndpoints.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortunePaidSlotOutcomes.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundModels.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundPolicy.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundService.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundStateCodec.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneRoundStore.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinDraws.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinEvidence.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinReport.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/FortuneForge.Games.SamuraiFortune.Server/SamuraiFortuneSpinResponse.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/server/GameWorkerModule.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/src/FortuneForge.Games.SamuraiFortune/FortuneForge.Games.SamuraiFortune.csproj | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/src/FortuneForge.Games.SamuraiFortune/SamuraiFortuneEngine.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/FortuneForge.Games.SamuraiFortune.Tests.csproj | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiPolicyTests.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiRoundStoreTests.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: games/SamuraiFortune/tests/FortuneForge.Games.SamuraiFortune.Tests/SamuraiTestSupport.cs | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/base-loss.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/base-win.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/collection-paw.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/collection-rand.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/collection-rows.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/collection-sync.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/contract.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:27:29-06:00 | Computer: VINNABOK | File: tools/fixtures/slot-contracts/samurai-fortune/scatter.json | Remove the retired Samurai implementation and its obsolete contracts through explicit file patches.
Edited: 2026-11-22T17:29:13-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json | Remove retired Samurai classic contracts; Crooked Road owns its independent canonical rules.
Edited: 2026-11-22T17:29:13-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Remove retired Samurai classic contracts; Crooked Road owns its independent canonical rules.
Edited: 2026-11-22T17:29:13-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/index.json | Remove retired Samurai classic contracts; Crooked Road owns its independent canonical rules.
Edited: 2026-11-22T17:29:13-06:00 | Computer: Vinnabok | File: tools/audio-suites/render-themed-suites.mjs | Remove the retired Samurai preset; Crooked Road owns original game audio.
Edited: 2026-11-22T17:43:23-06:00 | Computer: VINNABOK | File: fortuneforge.client/playwright.crooked-road.config.ts | Configure isolated local browser qualification against unchanged paid game handlers.
Edited: 2026-11-22T17:43:23-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/crooked-road.paid.spec.ts | Exercise naturally funded paid journeys, real bonus choices, wallet conservation and responsive game screens.
Edited: 2026-11-22T17:58:39-06:00 | Computer: VINNABOK | File: games/shared/FortuneForge.Games.Abstractions/GameWalletTransactionIds.cs | Add authoritative standard and legacy wallet ledger ID contracts.
Edited: 2026-11-22T17:58:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreWalletRoundSettlementStore.cs | Use the shared wallet ledger ID contract without changing standard or legacy transaction layouts.
Edited: 2026-11-22T18:02:00-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Accounts/CrookedRoadStandardWalletTests.cs | Verify native paid finalization, exact retries and audit against the real standard Firestore wallet.
Edited: 2026-11-22T18:04:47-06:00 | Computer: VINNABOK | File: games/CrookedRoad/README.md | Document native paid rules, architecture, math evidence, local paid testing and unpublished release status.
Edited: 2026-11-22T17:59:18-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/slots/SlotsLibraryPage.tsx | Exclude paid-only games from the public demo library while retaining them in the paid collection.
Edited: 2026-11-22T17:44:06-06:00 | Computer: Vinnabok | File: fortuneforge.client/e2e/slots.visual.spec.ts | Retire Samurai-specific demo snapshots; Crooked Road is qualified through its unchanged paid flow.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: tools/FortuneForge.CrookedRoadMath/README.md | Qualify the canonical game math with exact expectation and isolated reproducible simulations.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: tools/FortuneForge.CrookedRoadMath/Program.cs | Qualify the canonical game math with exact expectation and isolated reproducible simulations.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: tools/FortuneForge.CrookedRoadMath/FortuneForge.CrookedRoadMath.csproj | Qualify the canonical game math with exact expectation and isolated reproducible simulations.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests/CrookedRoadPaidLifecycleTests.cs | Verify native rules and durable paid lifecycle using isolated funds and persistence.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests/CrookedRoadTests.cs | Verify native rules and durable paid lifecycle using isolated funds and persistence.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests/FortuneForge.Games.CrookedRoad.Tests.csproj | Verify native rules and durable paid lifecycle using isolated funds and persistence.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/GameWorkerModule.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests/Support/CrookedRoadTestWallet.cs | Verify native rules and durable paid lifecycle using isolated funds and persistence.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.FixtureHost/FortuneForge.Games.CrookedRoad.FixtureHost.csproj | Host the exact paid game handlers with isolated local authentication, test wallet and loopback-only Firestore.
Edited: 2026-11-22T17:29:23-06:00 | Computer: VINNABOK | File: games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.FixtureHost/Program.cs | Host the exact paid game handlers with isolated local authentication, test wallet and loopback-only Firestore.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/FortuneForge.Games.CrookedRoad.csproj | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/FortuneForge.Games.CrookedRoad.Server.csproj | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadRoundStore.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadReport.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadEvidence.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadEndpoints.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadContracts.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/CrookedRoadActionStore.cs | Own native paid actions, durable spin settlement, replay evidence and worker composition.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadMath.cs | Qualify the canonical game math with exact expectation and isolated reproducible simulations.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadMap.cs | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadGame.cs | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadRules.cs | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadModels.cs | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/CrookedRoadState.cs | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:25:56-06:00 | Computer: VINNABOK | File: games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json | Implement fresh Crooked Road rules, road progression, randomness and three native journey bonuses.
Edited: 2026-11-22T17:22:56-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/useSlotsPageController.features.test.tsx | Verify required choices, synchronous spin/action exclusion and stale account/game isolation.
Edited: 2026-11-22T17:19:59-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/services/slotsApi.features.test.ts | Verify authenticated actions, unique idempotency, state validation and pending paid spin exclusion.
Edited: 2026-11-22T17:29:31-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.catalogue.test.tsx | Verify paid-only route authentication and absent demo route.
Edited: 2026-11-22T17:29:31-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.loading.test.tsx | Handle nullable demo metadata while retaining paid route authentication.
Edited: 2026-11-22T17:29:31-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/useGamePresence.test.tsx | Verify paid presence with nullable demo metadata.
Edited: 2026-11-22T17:58:34-06:00 | Computer: Vinnabok | File: tools/artwork/frontier-runtime.mjs | Export the thin cabinet at scene resolution, support selected asset variants, and exclude retired bounty-board prop masters from runtime regeneration.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentScarabs.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/CosmicFortuneEngine.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotModels.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotEngine.Features.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T17:58:42-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinDraws.cs | Added game-owned Scarab Trail math and versioned Pharaoh's Ascent replay while preserving legacy rules.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/PharaohsAscentFeature.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinResponse.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundModels.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinEvidence.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentScarabTests.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T18:00:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneSpinReportingTests.cs | Added wallet-settled Pharaoh's Ascent feature DTOs, Scarab Trail and replay/settlement tests.
Edited: 2026-11-22T17:59:42-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs | Select active Outlaw Hand v2 metadata and replace retired target-board catalogue copy.
Edited: 2026-11-22T17:59:42-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalog.ts | Select active Outlaw Hand v2 metadata and replace retired target-board catalogue copy.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalog.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/landing/LandingPage.tsx | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/landing/LandingPage.test.tsx | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.loading.test.tsx | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.catalogue.test.tsx | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.test.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/pharaohsAscentRetheme.test.ts | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/CatalogContract.cs | Replace Cosmic Fortune public identity with paid-only Pharaohs Ascent and verify routing/artwork compatibility.
Edited: 2026-11-22T18:01:54-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/cosmic-fortune.artwork.json | Point the stable catalogue metadata key at the new Egyptian title artwork.
Edited: 2026-11-22T18:01:36-06:00 | Computer: Vinnabok | File: package-lock.json | Register the native Frontier client dependency on shared house controls.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/frontier-outlaw-rules.json | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/FortuneForge.Games.FrontierFortune.csproj | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/src/FortuneForge.Games.FrontierFortune/OutlawHand.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundModels.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/OutlawRoundPolicy.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundPolicy.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundStore.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierRoundService.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierPaidEndpoints.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/server/FortuneForge.Games.FrontierFortune.Server/FrontierEvidence.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/Program.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FortuneForge.Games.FrontierFortune.Tests.csproj | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/OutlawHandTests.cs | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:02:33-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/Fixtures/frontier-v1-frozen-round.json | Summary: Replace current feature with persisted Outlaw hand, paid-wager-funded ante, versioned recovery and frozen-v1 replay compatibility.
Edited: 2026-11-22T18:04:02-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/scripts/generate-pharaoh-audio.mjs | Summary: Add reproducible original Egyptian adventure audio synthesis, Ogg encoding and decoded bounds/provenance verification.
Edited: 2026-11-22T18:04:02-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/sounds.ts | Summary: Replace the space sound suite with twenty game-owned Pharaoh's Ascent music, guardian, reel and reward cues.
Edited: 2026-11-22T18:04:51-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/themedSlotAudio.ts | Keep package-owned Egyptian audio from being replaced by the retired space sound suite.
Edited: 2026-11-22T18:04:51-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/soundSets.test.ts | Keep package-owned Egyptian audio from being replaced by the retired space sound suite.
Edited: 2026-11-22T18:05:08-06:00 | Computer: VINNABOK | File: scripts/verify-slot-package-boundaries.mjs | Describe the actual import/build verification scope and explicitly exclude unverified standalone gameplay ownership.
Edited: 2026-11-22T18:05:08-06:00 | Computer: VINNABOK | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/README.md | Correct the standalone claim and document current host-owned screen, controller and transport dependencies.
Edited: 2026-11-22T18:05:29-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortunePaidEndpoints.cs | Updated paid round user-facing game name while preserving game and storage identifiers.
Edited: 2026-11-22T18:05:29-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortunePaidSlotOutcomes.cs | Updated paid round user-facing game name while preserving game and storage identifiers.
Edited: 2026-11-22T18:05:29-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStateCodec.cs | Updated paid round user-facing game name while preserving game and storage identifiers.
Edited: 2026-11-22T18:05:29-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStore.cs | Updated paid round user-facing game name while preserving game and storage identifiers.
Edited: 2026-11-22T18:05:29-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneRoundStoreTests.cs | Verified new Thoth collection awards row expansion while preserving funded settlement behavior.
Edited: 2026-11-22T18:05:32-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Include the independently owned Frontier engine, paid server, and tests in the games solution.
Edited: 2026-11-22T18:05:50-06:00 | Computer: Vinnabok | File: scripts/game-smoke-inventory.mjs | Smoke the canonical Egyptian game route while retaining its original server game contract.
Edited: 2026-11-22T18:07:09-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/Program.cs | Qualify Outlaw v2 through native deal, confirm and classifier APIs with shared exact base evaluation, analytic Grand bounds and seeded automated strategy sampling.
Edited: 2026-11-22T18:07:09-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/EconomicsMath.cs | Qualify Outlaw v2 through native deal, confirm and classifier APIs with shared exact base evaluation, analytic Grand bounds and seeded automated strategy sampling.
Edited: 2026-11-22T18:07:09-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/OutlawQualification.cs | Qualify Outlaw v2 through native deal, confirm and classifier APIs with shared exact base evaluation, analytic Grand bounds and seeded automated strategy sampling.
Edited: 2026-11-22T18:07:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/FortuneForge.Games.CosmicFortune.LocalHarness.csproj | Added loopback-only paid UI harness using production engine, round persistence, emulator database and isolated test wallet.
Edited: 2026-11-22T18:07:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/LocalTestWallet.cs | Added loopback-only paid UI harness using production engine, round persistence, emulator database and isolated test wallet.
Edited: 2026-11-22T18:07:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/Program.cs | Added loopback-only paid UI harness using production engine, round persistence, emulator database and isolated test wallet.
Edited: 2026-11-22T18:10:16-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/README.md | Document the fitted Outlaw Hand v2 game, funded ante, paid-only validation, versioned recovery and current artwork.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/package.json | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/sync-rules.mjs | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/copy-assets.mjs | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/prepare-build.mjs | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/api.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/art.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/types.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/useFrontier.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/HandRail.tsx | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierFortuneGame.tsx | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/rules.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/manifest.ts | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tests/api.test.mjs | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/README.md | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/main.tsx | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/shell.css | Redesign Frontier as a fitted reel-integrated Outlaw hand with server-funded ante, card selection, holds, neutral platform controls and production-only transport.
Edited: 2026-11-22T18:10:27-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/BonusBoard.tsx | Remove disconnected target-board presentation and all retired prop interactions.
Edited: 2026-11-22T18:13:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotSpecialRoundProfilesTests.cs | Verify current package-owned collection profile awards each completed guardian its own enhanced mode.
Edited: 2026-11-22T18:17:28-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/HandRail.tsx | Correct bonus cards to playing-card proportions, add mirrored rank and suit indices, and move hold labels outside the card face.
Edited: 2026-11-22T18:17:28-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Correct bonus cards to playing-card proportions, add mirrored rank and suit indices, and move hold labels outside the card face.
Edited: 2026-11-22T18:18:16-06:00 | Computer: VINNABOK | File: AGENTS.md | Require complete slot client/server package ownership, server-authoritative paid play, neutral platform services, single-service deployment and evidence-based completion claims.
Edited: 2026-11-22T18:18:16-06:00 | Computer: VINNABOK | File: games/JadeDragon/AGENTS.md | Align Jade instructions with game-owned API/admission/recovery, platform wallet ports, paid-only testing and explicit remaining migration debt.
Edited: 2026-11-22T18:24:40-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierFortuneGame.tsx | Keep the shared Spin control mounted during card selection, with a distinct adjacent confirm action.
Edited: 2026-11-22T18:24:40-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Fit the hand confirmation action into the existing control row while preserving the Spin position.
Edited: 2026-11-22T18:25:14-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/Program.cs | Summary: Isolate local review and QA identities with an allowlisted local account header while preserving earned rounds and production gameplay.
Edited: 2026-11-22T18:25:14-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/local/main.tsx | Summary: Isolate local review and QA identities with an allowlisted local account header while preserving earned rounds and production gameplay.
Edited: 2026-11-22T18:26:34-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/README.md | Document isolated review versus QA accounts and the persistent Spin and hand-confirmation controls.
Edited: 2026-11-22T18:29:46-06:00 | Computer: Vinnabok | File: games/CosmicFortune/artwork/prepare-pharaoh-art.mjs | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:29:46-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/visuals.ts | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:01:57-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/cabinetTheme.ts | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:01:56-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/catalog.ts | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/manifest.ts | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/styles.css | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:31:08-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/runtimeOverrides.css | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohAscentPanel.tsx | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohAscentPanel.css | Created Pharaoh’s Ascent artwork, cabinet presentation, guardian interface, and local replacement integration.
Edited: 2026-11-22T18:32:08-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/pharaoh.ts | Summary: Validate connected, nonoverlapping maximal scarab groups and safe settled receipt totals.
Edited: 2026-11-22T18:32:08-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/symbols.ts | Summary: Derive calibrated reel payout guide from the engine contract, correct earned-spin and seal values, and hide unreachable collector symbols.
Edited: 2026-11-22T18:32:08-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohHelp.tsx | Summary: Explain The Sealed Tomb sigils, room choices, relic sets, keys and settlement plus accurate guardian, Sunfire and reel rules.
Edited: 2026-11-22T18:32:08-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/pharaohsAscentPanel.test.tsx | Summary: Verify receipt geometry and totals, visible calibrated rules, null-safe saved progress, inspection-only guardian controls and settled reward presentation.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/arcaneFeatures.ts | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomePanel.tsx | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneTomeBook.tsx | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionWager.tsx | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcanePotionMotion.tsx | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneIngredientFlights.tsx | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/potionArtwork.ts | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/manifest.ts | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/game-release.json | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/browser.ts | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:34:19-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/index.ts | Move Arcane feature contracts and browser entry into the game-owned package and retire host presentation hooks.
Edited: 2026-11-22T18:35:14-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/tomb.ts | Added game-owned Sealed Tomb contract decoding and a persistent pyramid expedition driven exclusively by server-confirmed room actions.
Edited: 2026-11-22T18:35:14-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/SealedTombPanel.tsx | Added game-owned Sealed Tomb contract decoding and a persistent pyramid expedition driven exclusively by server-confirmed room actions.
Edited: 2026-11-22T18:35:14-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/SealedTombPanel.css | Added game-owned Sealed Tomb contract decoding and a persistent pyramid expedition driven exclusively by server-confirmed room actions.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/slots/SlotsPage.tsx | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.tsx | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.ts | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/ArcaneArchivesHost.tsx | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:35:42-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/ArcaneArchivesBundled.ts | Delete the generic slot execution runtime and mount only game-owned clients, including Arcane; expose missing client integrations.
Edited: 2026-11-22T18:36:13-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/ArcaneArchivesView.tsx | Add package-owned Arcane screen composition and settled win presentation using existing game components and neutral rendering primitives.
Edited: 2026-11-22T18:36:13-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/arcaneArchivesView.css | Add package-owned Arcane screen composition and settled win presentation using existing game components and neutral rendering primitives.
Edited: 2026-11-22T18:36:13-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/useArcaneWinPopup.ts | Add package-owned Arcane screen composition and settled win presentation using existing game components and neutral rendering primitives.
Edited: 2026-11-22T18:33:02-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/SealedTombState.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:02-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/SealedTombRules.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:02-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/CosmicFortuneEngine.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundModels.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/PharaohsAscentFeature.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStateCodec.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinResponse.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:33:53-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinEvidence.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:35:00-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/SealedTombRoundPolicy.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:35:00-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/SealedTombEvidence.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:35:35-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundStore.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:35:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundService.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:35:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortunePaidEndpoints.cs | Added The Sealed Tomb funded expedition, native action settlement, persisted progression and replay contracts.
Edited: 2026-11-22T18:37:35-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/package.json | Make Arcane feature contracts package-owned and declare neutral presentation dependency; remove Arcane hooks from the generic runtime.
Edited: 2026-11-22T18:37:35-06:00 | Computer: VINNABOK | File: package-lock.json | Make Arcane feature contracts package-owned and declare neutral presentation dependency; remove Arcane hooks from the generic runtime.
Edited: 2026-11-22T18:37:35-06:00 | Computer: VINNABOK | File: packages/slot-game-runtime/src/experience.ts | Make Arcane feature contracts package-owned and declare neutral presentation dependency; remove Arcane hooks from the generic runtime.
Edited: 2026-11-22T18:37:35-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/types/slots.ts | Make Arcane feature contracts package-owned and declare neutral presentation dependency; remove Arcane hooks from the generic runtime.
Edited: 2026-11-22T18:38:17-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/arcaneWinPresentation.ts | Centralize settled-award celebration thresholds for the Arcane controller and visual popup.
Edited: 2026-11-22T18:38:35-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Place phone cards below Spin and tablet cards left of controls below the cabinet, preserving laptop layouts.
Edited: 2026-11-22T18:39:33-06:00 | Computer: VINNABOK | File: scripts/verify-slot-client-entries.mjs | Expose slot clients still relying on deleted generic runtime and block metadata-only standalone claims.
Edited: 2026-11-22T18:39:33-06:00 | Computer: VINNABOK | File: scripts/tests/verify-slot-client-entries.test.mjs | Expose slot clients still relying on deleted generic runtime and block metadata-only standalone claims.
Edited: 2026-11-22T18:39:33-06:00 | Computer: VINNABOK | File: scripts/verify-game-source-ownership.mjs | Expose slot clients still relying on deleted generic runtime and block metadata-only standalone claims.
Edited: 2026-11-22T18:39:33-06:00 | Computer: VINNABOK | File: package.json | Expose slot clients still relying on deleted generic runtime and block metadata-only standalone claims.
Edited: 2026-11-22T18:40:26-06:00 | Computer: VINNABOK | File: scripts/deploy-game.mjs | Remove manifest-to-generic-renderer browser release generation and require a game-owned component entry.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/GameWorkers/ArcanePaidWorkerGateway.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Controllers/SlotsController.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundStore.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundContracts.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcanePaidRouteContract.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcanePaidEndpoints.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/GameWorkers/ArcanePaidWorkerGatewayTests.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:40:41-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/tests/FortuneForge.Games.ArcaneArchives.Tests/ArcaneRoundStoreTests.cs | Keep current Arcane paid rounds authoritative in its package, remove embedded legacy recovery, and reject obsolete local rounds without settlement.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/types.ts | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/api.ts | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/useArcaneArchives.ts | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/tests/fixtures.ts | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/tests/api.test.ts | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:41:23-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/tests/controller.test.tsx | Own Arcane paid state and spin transport, request identity, authoritative Tome and potion commands, native controller and isolated contract/lifecycle checks.
Edited: 2026-11-22T18:38:54-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinReport.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:38:44-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/FortuneForge.Games.CosmicFortune.Server.csproj | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:41:15-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/SealedTombRulesTests.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:41:54-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneRoundService.cs | Withdraw the incomplete schema-v1 compatibility experiment after the user waived obsolete local-round recovery; restore current native service/report and remove the temporary recovery source.
Edited: 2026-11-22T18:41:54-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneSpinReport.cs | Withdraw the incomplete schema-v1 compatibility experiment after the user waived obsolete local-round recovery; restore current native service/report and remove the temporary recovery source.
Edited: 2026-11-22T18:41:54-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneLegacyRoundRecovery.cs | Withdraw the incomplete schema-v1 compatibility experiment after the user waived obsolete local-round recovery; restore current native service/report and remove the temporary recovery source.
Edited: 2026-11-22T18:41:55-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/README.md | Document native game-owned entry, paid server authority, focused checks and authorized retirement of obsolete local round recovery.
Edited: 2026-11-22T18:40:22-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/SealedTombRoundStoreTests.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:40:22-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/FortuneForge.Games.CosmicFortune.Tests.csproj | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:41:15-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneSpinReportingTests.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:41:15-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentScarabTests.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:40:22-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/LocalTestWallet.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:38:44-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:38:44-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SealedTombDispatchTests.cs | Verified native Sealed Tomb funding, no-charge choices, exact-once credit, action replay and host audit dispatch.
Edited: 2026-11-22T18:43:25-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohHelp.css | Summary: Style a responsive gold-and-teal illustrated expedition scroll with interactive room, relic and payline scenes inside the neutral dialog.
Edited: 2026-11-22T18:43:25-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/scripts/generate-pharaoh-guide.mjs | Summary: Generate guide payline shapes and source integrity metadata from the authoritative classic engine factory.
Edited: 2026-11-22T18:43:25-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohAscentPanel.tsx | Summary: Give the standalone guardian receipt panel its own presentation-only props instead of generic layout contract types.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/types.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/api.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/usePharaohAscent.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/fixtures.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/tomb.test.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/api.test.ts | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/controller.test.tsx | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:43:27-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/sealedTombPanel.test.tsx | Added standalone paid transport/controller and verified account isolation, exact-operation recovery, server-authoritative expedition choices and result dismissal.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.dialogs.test.tsx | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.olympus.test.tsx | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/SlotsPageView.rainbowBonus.test.tsx | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.test.ts | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.preparedSpin.test.ts | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.fifthSun.test.ts | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/pages/slots/SlotsNavbar.test.tsx | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:47:27-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/SlotGameRoute.loading.test.tsx | Remove tests tied to retired generic slot execution and verify native routing plus explicit missing-client failure.
Edited: 2026-11-22T18:25:42-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/definition.ts | Own Crooked Road presentation contracts and metadata without the generic slot showcase builder.
Edited: 2026-11-22T18:25:42-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/catalog.ts | Own Crooked Road presentation contracts and metadata without the generic slot showcase builder.
Edited: 2026-11-22T18:25:42-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/manifest.ts | Own Crooked Road presentation contracts and metadata without the generic slot showcase builder.
Edited: 2026-11-22T18:25:42-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadPanel.tsx | Own Crooked Road presentation contracts and metadata without the generic slot showcase builder.
Edited: 2026-11-22T18:25:42-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadHelp.tsx | Own Crooked Road presentation contracts and metadata without the generic slot showcase builder.
Edited: 2026-11-22T18:27:16-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/useCrookedRoad.ts | Own all paid spin input, exact recovery, account isolation, reel animation, audio, autospin and native feature presentation in the game package.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/index.ts | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/index.tsx | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/browser.ts | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/CrookedRoadBundled.ts | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/CrookedRoadHost.tsx | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: games/CrookedRoad/game-release.json | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/package.json | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.ts | Route both bundled and releasable paid browser entries directly to the package-owned game; remove host-owned Crooked Road request recovery.
Edited: 2026-11-22T18:29:52-06:00 | Computer: VINNABOK | File: games/catalog/slot-packages.json | Align standalone browser ownership, package dependency graph and neutral integration tests with the executable Crooked Road entry.
Edited: 2026-11-22T18:29:52-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/CrookedRoadPanel.test.tsx | Align standalone browser ownership, package dependency graph and neutral integration tests with the executable Crooked Road entry.
Edited: 2026-11-22T18:29:52-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/services/slotsApi.features.test.ts | Retire Crooked Road's obsolete generic-host tests after migrating transport and controller behavior into owned game-package tests.
Edited: 2026-11-22T18:28:51-06:00 | Computer: VINNABOK | File: package-lock.json | Align standalone browser ownership, package dependency graph and neutral integration tests with the executable Crooked Road entry.
Edited: 2026-11-22T18:31:36-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/useCrookedRoad.test.tsx | Verify owned input exclusion, bonus choice/auto guards, lost-reply recovery and isolation from stale account/transport replies.
Edited: 2026-11-22T18:33:57-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/features/slots/useSlotsPageController.features.test.tsx | Retire Crooked Road's obsolete generic-host tests after migrating transport and controller behavior into owned game-package tests.
Edited: 2026-11-22T18:33:57-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/symbols.ts | Remove generic showcase aliases and isolate neutral host tests from the now standalone Crooked Road game.
Edited: 2026-11-22T18:33:57-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/assets/art/irish/README.md | Remove generic showcase aliases and isolate neutral host tests from the now standalone Crooked Road game.
Edited: 2026-11-22T18:35:43-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Verify the actual paid route selects the owned executable game without the legacy manifest renderer or host feature adapter.
Edited: 2026-11-22T18:35:43-06:00 | Computer: VINNABOK | File: fortuneforge.client/src/app/routes/SlotGameRoute.loading.test.tsx | Verify the actual paid route selects the owned executable game without the legacy manifest renderer or host feature adapter.
Edited: 2026-11-22T18:35:43-06:00 | Computer: VINNABOK | File: fortuneforge.client/e2e/crooked-road.paid.spec.ts | Verify the actual paid route selects the owned executable game without the legacy manifest renderer or host feature adapter.
Edited: 2026-11-22T18:40:45-06:00 | Computer: VINNABOK | File: fortuneforge.client/playwright.crooked-road.config.ts | Allow the unchanged paid browser qualification to run through either the site route or isolated production-entry harness.
Edited: 2026-11-22T18:48:23-06:00 | Computer: VINNABOK | File: games/CrookedRoad/README.md | Document executable client ownership, thin host integration, independent release entry and unchanged paid local infrastructure.
Edited: 2026-11-22T18:48:23-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/README.md | Document executable client ownership, thin host integration, independent release entry and unchanged paid local infrastructure.
Edited: 2026-11-22T18:27:59-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/types.ts | Own paid response contracts, canonical identifiers and neutral account/session/shell ports.
Edited: 2026-11-22T18:32:20-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/api.ts | Own prepared paid transport, response validation, keyed road actions and exact operation recovery.
Edited: 2026-11-22T18:32:20-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/api.test.mjs | Verify injected paid transport, exact recovery and game-specific response validation.
Edited: 2026-11-22T18:35:19-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadView.tsx | Own the complete reel screen, native journeys, controls, dialogs and win presentation.
Edited: 2026-11-22T18:31:32-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/crookedRoadGame.css | Own standalone responsive layout and presentation for desktop, laptop and mobile.
Edited: 2026-11-22T18:35:19-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/CrookedRoadView.test.tsx | Verify screen controls, required choices, Well auto play, review guards and dialogs.
Edited: 2026-11-22T18:26:43-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/standalone-boundary.test.mjs | Verify executable runtime ownership through public imports and reject host, foreign-game and showcase dependencies.
Edited: 2026-11-22T18:41:41-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/local/index.html | Add a local-only shell that mounts the unchanged production paid game.
Edited: 2026-11-22T18:41:41-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/local/main.tsx | Inject isolated fixture account, authenticated transport and generic navbar ports into the production game.
Edited: 2026-11-22T18:42:03-06:00 | Computer: VINNABOK | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/local/vite.config.ts | Isolate local package serving from main-site hot reloads while forwarding unchanged paid API requests.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/SpinService.Execution.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/SpinService.Core.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Spins/PaidSlotWorker.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Reels/CryptoReelGenerator.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePlatformReportingTests.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcanePaylineTests.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcaneLightningCollectorTests.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:48:35-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/ArcaneOwnershipTests.cs | Remove Arcane generation and feature calculation from the retired host classic path; retain definition metadata, reject old dispatch, and exercise the package engine and ownership boundary directly.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotAdmission.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotSettlement.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotQueries.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.PreparedSlotOperations.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/Firestore/FirestoreAccountStore.SlotRuntimeBoundary.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Storage/IAccountStore.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Accounts/Services/AccountService.Slots.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/ArcaneTomeTests.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/ArcanePaidWorkerTests.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/ArcanePreparedSpinTests.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:50:19-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/ArcaneRemovedHostRuntimeTests.cs | Remove retired host-owned Arcane admission, feature progression, settlement and prepared-round recovery; retain native game behavior and test the removed host boundary.
Edited: 2026-11-22T18:51:15-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/Slots/SlotMathAuditTests.cs | Stop routing Arcane through the retired host classic simulation lane; its native package owns engine validation and math.
Edited: 2026-11-22T18:53:16-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/README.md | Document exact optimal RTP qualification, sampled audit distinction and mixed-wager funding-rounding limitation.
Edited: 2026-11-22T18:53:23-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/Program.cs | Add exact BigInteger optimal-play Outlaw RTP enumeration with native terminal/hold checks, Hall feasibility, independent probability identities and fixed-wager full-game economics.
Edited: 2026-11-22T18:53:23-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/OutlawOptimalQualification.cs | Add exact BigInteger optimal-play Outlaw RTP enumeration with native terminal/hold checks, Hall feasibility, independent probability identities and fixed-wager full-game economics.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/RoundAudit/SlotRoundAudit.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Bonuses/ArcaneLightningCollector.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Bonuses/ArcaneLightningCollection.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: FortuneForge.Server.Tests/RoundAudit/SlotRoundAuditTests.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneSpinEvidence.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/server/FortuneForge.Games.ArcaneArchives.Server/ArcaneSpinReport.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:54:25-06:00 | Computer: VINNABOK | File: games/ArcaneArchives/tests/FortuneForge.Games.ArcaneArchives.Tests/ArcaneSpinEvidenceTests.cs | Remove retired host Arcane audit/collector policy, retain transport DTOs, dispatch native evidence replay to the game package and verify admission, payout and feature evidence there.
Edited: 2026-11-22T18:56:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Accounts/Models/AccountModels.cs | Remove unused Arcane admission, workbench, potion and response state from generic host models; native package DTOs own those fields.
Edited: 2026-11-22T18:56:39-06:00 | Computer: VINNABOK | File: FortuneForge.Server/Slots/Models/SpinModels.cs | Remove unused Arcane admission, workbench, potion and response state from generic host models; native package DTOs own those fields.
Edited: 2026-11-22T18:57:16-06:00 | Computer: VINNABOK | File: AGENTS.md | Preserve the explicit retirement of the generic slot runtime and require game-owned fixes instead of fallback restoration or weakened inventory checks.
Edited: 2026-11-22T18:39:20-06:00 | Computer: Vinnabok | File: games/CosmicFortune/game-release.json | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:57:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/package.json | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:46:42-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohAscentView.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:36:48-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/index.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:36:48-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/browser.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:38:39-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/pharaohAscentGame.css | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:55:12-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/symbols.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:55:12-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohHelp.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:40:56-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/scripts/verify-standalone.mjs | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:43:20-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/scripts/serve-local-review.mjs | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:43:20-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/review/index.html | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:43:20-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/review/main.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:43:20-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/review/review.css | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:44:23-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/playwright.config.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:57:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/vitest.config.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:45:19-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/paid-browser.spec.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:57:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/guardianAndGuide.test.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:39:20-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PharaohsAscentHost.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:39:20-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PharaohsAscentBundled.ts | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:39:20-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:39:33-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:57:04-06:00 | Computer: Vinnabok | File: package-lock.json | Implemented the independent Pharaoh game entry, local paid review, illustrated presentation, and package-owned verification.
Edited: 2026-11-22T18:57:46-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/winPresentation.ts | Project settled native line and hand awards into presentation without evaluating payouts.
Edited: 2026-11-22T18:57:46-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/useFrontier.ts | Keep win presentation until the next Spin intent and suppress stale recovery receipts.
Edited: 2026-11-22T18:57:46-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tests/winPresentation.test.mjs | Verify settled awards, feature completion, next-spin clearing and stale receipt suppression.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/rules/client-rules.json | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/rules.ts | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/paylineGuide.ts | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/symbols.ts | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/manifest.ts | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:58:55-06:00 | Computer: Vinnabok | File: games/ArcaneArchives/tests/FortuneForge.Games.ArcaneArchives.Tests/ArcaneClientRulesTests.cs | Own the unchanged Arcane client wager/presentation configuration and payline guide snapshot; verify it against native current rules without generating outcomes.
Edited: 2026-11-22T18:59:00-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierWinDisplay.tsx | Add a presentation-only embossed gold win overlay with responsive amount sizing, slow breathing, reduced-motion support and a separately hidden cycling detail.
Edited: 2026-11-22T18:59:00-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/winDisplay.css | Add a presentation-only embossed gold win overlay with responsive amount sizing, slow breathing, reduced-motion support and a separately hidden cycling detail.
Edited: 2026-11-22T18:59:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/paylineGeometry.ts | Summary: Draw decorative payline traces only through settled server cells, with active-line glow, reduced-motion support and geometry checks.
Edited: 2026-11-22T18:59:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/PaylineTracers.tsx | Summary: Draw decorative payline traces only through settled server cells, with active-line glow, reduced-motion support and geometry checks.
Edited: 2026-11-22T18:59:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/paylines.css | Summary: Draw decorative payline traces only through settled server cells, with active-line glow, reduced-motion support and geometry checks.
Edited: 2026-11-22T18:59:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/paylineGeometry.test.mjs | Summary: Draw decorative payline traces only through settled server cells, with active-line glow, reduced-motion support and geometry checks.
Edited: 2026-11-22T19:00:23-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/tests/paylineGeometry.test.mjs | Summary: Place geometry checks in the established package test lane and exercise the actual compiled production module.
Edited: 2026-11-22T19:00:23-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierFortuneGame.tsx | Integrate illustrated win help, exact-cell payline tracers and persistent gold line and hand awards.
Edited: 2026-11-22T18:57:56-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/scripts/run-tests.mjs | Run package tests using the checkout's resolved physical path on Windows.
Edited: 2026-11-22T19:06:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/FrontierHowToWin.tsx | Add a native illustrated four-chapter How to Win guide using Frontier-owned art and authoritative rule metadata, exact paylines and Outlaw funding/action instructions.
Edited: 2026-11-22T19:06:03-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/howToWin.css | Style the Frontier field guide, physical card illustrations and accessible tabs with bounded native-modal geometry, internal reading scroll and reduced-motion support.
Edited: 2026-11-22T19:06:36-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/src/styles.css | Remove retired text-dialog styles after moving the illustrated How to Win guide into its own component and stylesheet.
Edited: 2026-11-22T19:33:50-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/TombOptimalPolicy.cs | Summary: Add exact integer finite-horizon optimal room analysis with an immutable policy cache and checks against production reward draws and terminal ordering; test-only, no gameplay changes.
Edited: 2026-11-22T19:33:32-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/FortuneForge.Games.CosmicFortune.Math.csproj | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:33:32-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Program.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:33:32-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/MathOptions.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:33:32-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/SeededSource.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:35:55-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/TombStrategies.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:35:55-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Simulation.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:35:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/BlockConfidence.cs | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:33:32-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/FortuneForge.Games.CosmicFortune.Server.csproj | Added isolated production-policy RTP simulator with reproducible streams, exact earned progression and funded ticket comparisons.
Edited: 2026-11-22T19:38:49-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/README.md | Documented reproducible RTP commands, exact production policy use, paired tomb strategies, block intervals and finite-sample residuals.
Edited: 2026-11-22T19:48:59-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/Program.cs | Added paired optional local-only test project and wallet checkpoint restoration settings.
Edited: 2026-11-22T19:48:59-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.LocalHarness/LocalTestWallet.cs | Restored existing test settlement receipts and balance from a validated local wallet checkpoint without changing gameplay.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/wagering.ts | Summary: Read the single game-owned wagering document and derive current paid picker options.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/manifest.ts | Summary: Use authoritative point value, paid bounds and R3 default from the shared wagering document.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/usePharaohAscent.ts | Summary: Derive the starting wager index from the authoritative default points.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/api.ts | Summary: Validate fresh cash wagers against current picker while preserving exact historical pending and funded terms.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/wagering.test.ts | Summary: Verify shared config gives R3 default and R0.50 to R75 paid options.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/api.test.ts | Summary: Test current admission boundaries and historical above-limit prepared recovery and funded receipts.
Edited: 2026-11-22T19:50:10-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/controller.test.tsx | Summary: Verify default, picker edges and locked historical earned wagers.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/wagering.json | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:50:43-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentWagering.cs | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/FortuneForge.Games.CosmicFortune.csproj | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/MathOptions.cs | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Program.cs | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:47:38-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/README.md | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T19:50:01-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentWageringTests.cs | Centralized current paid wagering at R0.50 minimum, R3 default and R75 maximum while preserving historical funded and replay terms.
Edited: 2026-11-22T20:07:13-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentSunfire.cs | Price new Sunfire meters from their funded wagers, preserve legacy meters, and prevent bet increases from revaluing stored energy.
Edited: 2026-11-22T20:08:37-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentSunfireTests.cs | Verify funded Sunfire pricing, bet-switch resistance, capped energy funding, historical benefit preservation and exact whole-multiplier returns across all legal flat stakes.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/math.ts | Summary: Read the current versioned math document and derive its visible paytable.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/symbols.ts | Summary: Show current integer payout multipliers from the shared math document while preserving old reward terms.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/manifest.ts | Summary: Remove obsolete fractional payout scale wording from current Scarab Trail help.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohHelp.tsx | Summary: Teach current scarab qualification and direct multipliers in the illustrated guide, retaining saved-funding explanation.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohAscentPanel.tsx | Summary: Remove obsolete current-rule payout scale copy while keeping settled receipt amounts.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/tomb.ts | Summary: Accept server-pinned qualifying scarab metadata and maintain compatibility with earlier receipts.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/SealedTombPanel.tsx | Summary: Show the saved funding threshold in the visible sigil strip and detailed instructions.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/SealedTombPanel.css | Summary: Keep the qualifying scarab caption legible inside the existing funding strip.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/fixtures.ts | Summary: Include authoritative current qualification metadata in isolated test fixtures.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/guardianAndGuide.test.tsx | Summary: Verify current integer paytable parity, trigger copy and removal of obsolete scaling text.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/sealedTombPanel.test.tsx | Summary: Verify legacy two-scarab funding remains visible without progress reset.
Edited: 2026-11-22T20:09:51-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/tomb.test.ts | Summary: Check new qualifying-count bounds and legacy receipt compatibility.
Edited: 2026-11-22T20:11:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/PayoutContributions.cs | Added diagnostic per-symbol and match-length return/exposure reporting with exact reconciliation to native settled totals.
Edited: 2026-11-22T20:11:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Simulation.cs | Added payout contribution diagnostics and legal variable-bet progression stress policies while retaining native earned-spin and tomb settlement flow.
Edited: 2026-11-22T20:11:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/MathOptions.cs | Added explicit fixed, Sunfire, pity, combined progression and alternating wager policy options for isolated math analysis.
Edited: 2026-11-22T20:11:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Program.cs | Described actual-wager analysis and selected-match contribution diagnostics in generated report metadata.
Edited: 2026-11-22T20:11:04-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/README.md | Documented payout contribution interpretation and legal variable-bet analysis without forced outcomes.
Edited: 2026-11-22T20:11:07-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentPity.cs | Keep match safety counters attached to the wager that earned each streak, preserving historical streak recovery.
Edited: 2026-11-22T20:11:07-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentPityTests.cs | Verify wager switching cannot upgrade a safety counter, independent streak preservation and historical funded wagers.
Edited: 2026-11-22T20:18:15-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentFundedBankTests.cs | Verified native FIFO earned-spin wager/mode preservation, storage roundtrip and frozen v2 merged-bank compatibility with isolated unit outcomes.
Edited: 2026-11-22T20:07:40-06:00 | Computer: Vinnabok | File: games/CosmicFortune/math-v3.json | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:07:40-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentMathV3.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:07:40-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/FortuneForge.Games.CosmicFortune.csproj | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:07:40-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/CosmicFortuneEngine.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/SealedTombState.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:10:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/SealedTombRules.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:15:09-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentFreeSpins.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:14:16-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Runtime/ClassicSlotModels.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:14:16-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Slots.Persistence/ClassicSlotRoundProgression.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:15:09-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:10:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundModels.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:14:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinDraws.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:10:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinResponse.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:10:03-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinEvidence.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:15:09-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneSpinReport.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:17:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentScarabTests.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:17:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneSpinReportingTests.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:17:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/CosmicFortuneRoundStoreTests.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:17:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/SealedTombRulesTests.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:17:16-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentWageringTests.cs | Added versioned integer paytable, grandfathered funding, per-wager pity, funded Sunfire and separate funded free-spin batches with historical replay protection.
Edited: 2026-11-22T20:21:00-06:00 | Computer: Vinnabok | File: games/CosmicFortune/math-v3.json | Calibrate the versioned integer paytable toward94 percent total optimal-play return, preserve tomb prizes and scarab tiers, and declare the intended target.
Edited: 2026-11-22T20:28:49-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/Verify-PairedStakes.ps1 | Added read-only exact wager-grid proportionality verification of financial totals, native progression counters, tomb strategies and residual funding.
Edited: 2026-11-22T20:31:31-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/PharaohHelp.tsx | Clarified that Sunfire seal-appearance improvements retain their funded wager value and verified the guide wording.
Edited: 2026-11-22T20:31:31-06:00 | Computer: Vinnabok | File: games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/tests/guardianAndGuide.test.tsx | Clarified that Sunfire seal-appearance improvements retain their funded wager value and verified the guide wording.
Edited: 2026-11-22T20:29:41-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/PharaohsAscentBoosts.cs | Added wager-funded boost credits, legacy-credit recovery, funded Sunfire generation inputs and replay/financial regression coverage.
Edited: 2026-11-22T20:29:41-06:00 | Computer: Vinnabok | File: games/CosmicFortune/src/FortuneForge.Games.CosmicFortune/SealedTombState.cs | Added wager-funded boost credits, legacy-credit recovery, funded Sunfire generation inputs and replay/financial regression coverage.
Edited: 2026-11-22T20:31:24-06:00 | Computer: Vinnabok | File: games/CosmicFortune/server/FortuneForge.Games.CosmicFortune.Server/CosmicFortuneRoundPolicy.cs | Added wager-funded boost credits, legacy-credit recovery, funded Sunfire generation inputs and replay/financial regression coverage.
Edited: 2026-11-22T20:31:24-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Tests/PharaohsAscentBoostTests.cs | Added wager-funded boost credits, legacy-credit recovery, funded Sunfire generation inputs and replay/financial regression coverage.
Edited: 2026-11-22T20:40:01-06:00 | Computer: Vinnabok | File: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/README.md | Documented final v3 integer pricing, 94 percent target, funded progression, paired full-grid proof, same-seed financial equivalence and boosted switching accounting.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/CatalogContract.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/MidnightVaultDefinitionTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/PreparedSlotWorkerOwnershipTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SealedTombDispatchTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotMathAuditTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotSpecialRoundProfilesTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Activity/AcceptedGameActivity.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/SecurityAudit/SlotEvidenceReplayAudit.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Bonuses/SlotSpecialRoundProfiles.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Slots/Games/StandaloneSlotPackageRegistry.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: FortuneForge.slnx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: GAME_FEEL_PROGRESS.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: config/backend-host.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: config/backend-packages.lock.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/GAME_AUDIT_2026-09-22.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/GAME_DEPLOYMENT.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/GAME_LOADING_SCREENS.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/GAME_SECURITY_OVERHAUL_MATRIX.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/GAME_SHIP_REVIEW_LOOP.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/SLOT_CATALOGUE_ART.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: docs/game-qa-audit-2026-09-23.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/e2e/slots.visual.spec.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/package.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/public/assets/slots/audio-suites/v1/manifest.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.catalogue.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/AppRoutes.loading.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/AuthenticatedRouteState.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PharaohsAscentBundled.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PharaohsAscentHost.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/pharaohs-ascent-title-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/pharaohs-ascent.artwork.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/__fixtures__/verifyLoadingB.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/soundSets.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/catalog.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/manifestLoaders/pharaohs-ascent.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/pharaohsAscentRetheme.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeRegistry.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/evidence/audio-provenance.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: fortuneforge.client/vite.config.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/FortuneForge.Games.slnx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/README.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/prepare-pharaoh-art.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/prompts.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/README.txt | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/atlas-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/cutaway-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/frame-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/rooms-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/sun-clean-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/temple-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/title-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/wild-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/artwork/source/wordmark-v1.png | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/README.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/package.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/playwright.config.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/review/index.html | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/review/main.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/review/review.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/copy-static.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/generate-pharaoh-audio.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/generate-pharaoh-guide.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/run-tests.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/serve-local-review.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/verify-standalone.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohAscentPanel.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohAscentPanel.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohAscentView.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohHelp.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohHelp.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/SealedTombPanel.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/SealedTombPanel.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/api.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-alien-captain-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-amber-solar-world-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-astronaut-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-atomic-plasma-charge-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-cabinet-emblem-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-comet-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-crimson-binary-star-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-dark-matter-token-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-emerald-garden-planet-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-meteor-trio-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-moon-station-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-observation-deck-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-ringed-gas-giant-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-rocket-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-sapphire-ice-planet-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-satellite-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-supernova-core-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-tractor-beam-saucer-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-wordmark-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/optimized/pharaohs-ascent-wormhole-v2.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/ankh-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/anubis-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/cabinet-frame-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/chest-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/coin-room-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/coin-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/eye-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/falcon-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/gate-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/horus-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/key-room-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/lotus-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/pharaoh-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/relic-room-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/sarcophagus-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/sun-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/sunfire-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/temple-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/thoth-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/title-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/tomb-cutaway-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/trail-scarab-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/wadjet-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/wild-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/winged-scarab-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/art/pharaohs-ascent/wordmark-v1.webp | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/manifest.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-chamber-complete.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-chamber-opens.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-gate-release.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-guardian-lock.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-guardian-reels.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-guardians-awaken.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-reel-lock.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-scarab-trail.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-seal-anubis.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-seal-horus.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-seal-thoth.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-seal-wadjet.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-stone-reels.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-stone-touch.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-temple-procession.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-temple-rest.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-treasure-great.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-treasure-royal.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-treasure-small.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-wager-step.ogg | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/rules/payline-layouts.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/browser.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/cabinetTheme.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/catalog.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/index.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/manifest.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/math.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/pharaoh.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/pharaohAscentGame.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/sounds.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/styles.css | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/symbols.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/tomb.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/types.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/usePharaohAscent.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/visuals.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/wagering.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/api.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/controller.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/fixtures.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/guardianAndGuide.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/paid-browser.spec.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/sealedTombPanel.test.tsx | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/tomb.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/wagering.test.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tsconfig.build.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tsconfig.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/vitest.config.ts | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/game-release.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/math-v3.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/FortuneForge.Games.PharaohsAscent.Server.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentFeature.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentPaidEndpoints.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentPaidSlotOutcomes.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentRoundModels.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentRoundPolicy.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentRoundService.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentRoundStateCodec.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentRoundStore.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentSpinDraws.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentSpinEvidence.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentSpinReport.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/PharaohsAscentSpinResponse.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/Program.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/Properties/launchSettings.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/SealedTombEvidence.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/FortuneForge.Games.PharaohsAscent.Server/SealedTombRoundPolicy.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/server/GameWorkerModule.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/FortuneForge.Games.PharaohsAscent.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentBoosts.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentEngine.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentFreeSpins.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentMathV3.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentPity.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentScarabs.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentSunfire.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/PharaohsAscentWagering.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/SealedTombRules.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/src/FortuneForge.Games.PharaohsAscent/SealedTombState.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.LocalHarness/FortuneForge.Games.PharaohsAscent.LocalHarness.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.LocalHarness/LocalTestWallet.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.LocalHarness/Program.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/BlockConfidence.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/FortuneForge.Games.PharaohsAscent.Math.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/MathOptions.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/PayoutContributions.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/Program.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/README.md | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/SeededSource.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/Simulation.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/TombOptimalPolicy.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/TombStrategies.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/Verify-PairedStakes.ps1 | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/FortuneForge.Games.PharaohsAscent.Tests.csproj | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentBoostTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentFundedBankTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentPityTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentRoundStateCodecTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentRoundStoreTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentScarabTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentSpinReportingTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentSunfireTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/PharaohsAscentWageringTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/SealedTombRoundStoreTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Tests/SealedTombRulesTests.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/wagering.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Abstractions/AcceptedGameParticipationDocuments.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: games/shared/FortuneForge.Games.Abstractions/GameAnalyticsSettlement.cs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: package-lock.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: package.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: packages/slot-game-runtime/src/contracts/engine-guide-contracts.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: scripts/game-smoke-inventory.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: scripts/game-worker-release.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: scripts/prepare-slot-title-card.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/audio-suites/render-themed-suites.mjs | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/index.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/base-loss.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/base-win.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/collection-paw.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/collection-rand.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/collection-rows.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/collection-sync.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/contract.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/energy-completion.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:35:11-06:00 | Computer: Vinnabok | File: tools/fixtures/slot-contracts/pharaohs-ascent/scatter.json | Rename the game and integration to fresh Pharaohs Ascent identifiers without Cosmic compatibility; remove obsolete package selection.
Edited: 2026-11-24T14:36:25-06:00 | Computer: Vinnabok | File: package-lock.json | Resolve the renamed Pharaohs Ascent workspace and remove the obsolete package link.
Edited: 2026-11-24T14:37:50-06:00 | Computer: Vinnabok | File: C:/Users/Lctiv/source/repos/main/games/PharaohsAscent/guide-contract.json | Keep the fresh Pharaoh game presentation contract within its own package.
Edited: 2026-11-24T14:37:50-06:00 | Computer: Vinnabok | File: C:/Users/Lctiv/source/repos/main/games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/engineGuide.ts | Keep the fresh Pharaoh game presentation contract within its own package.
Edited: 2026-11-24T14:41:23-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/slots/config/cabinetThemes.ts | Remove the unused retired space-theme identifier.
Edited: 2026-11-24T14:57:21-06:00 | Computer: Vinnabok | File: config/backend-packages.lock.json | Pin fresh Pharaohs Ascent release 397c7799-2c6a-4bf3-8133-fa97364e5622 under its new runtime identity; retain every unrelated selected package.
Edited: 2026-11-24T15:02:16-06:00 | Computer: Vinnabok | File: package.json | Add explicit local-only deployment preflight scripts so PowerShell npm argument forwarding cannot drop the safety flag.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: packages/slot-client-core/src/hooks/useSlotAudio.ts | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/usePharaohMusic.ts | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/usePharaohAscent.ts | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/PharaohAscentView.tsx | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/sounds.ts | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/controller.test.tsx | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/tests/audio.test.tsx | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/recordings.json | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-reworked.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-reworked-percussion.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-riq-hit.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-snake-rattle.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-bell-win.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:31:24-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/assets/audio/pharaoh-oud-big-win-g.ogg | Import requested Pharaoh recordings, four-spin music arrangement handoff, button/reel/win cues, and 8x oud pitch/threshold verification.
Edited: 2026-11-24T16:54:20-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/sync-contracts.mjs | Include generated game contract assets inside the releasable Pharaoh client and verify freshness against authoritative game-root contracts.
Edited: 2026-11-24T16:54:20-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/package.json | Include generated game contract assets inside the releasable Pharaoh client and verify freshness against authoritative game-root contracts.
Edited: 2026-11-24T16:54:20-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/math.ts | Include generated game contract assets inside the releasable Pharaoh client and verify freshness against authoritative game-root contracts.
Edited: 2026-11-24T16:54:20-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/wagering.ts | Include generated game contract assets inside the releasable Pharaoh client and verify freshness against authoritative game-root contracts.
Edited: 2026-11-24T16:54:20-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/src/engineGuide.ts | Include generated game contract assets inside the releasable Pharaoh client and verify freshness against authoritative game-root contracts.
Edited: 2026-11-24T16:55:54-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Point Arcane Archives build-isolation metadata at its existing lazy package wrapper rather than the retired manifest loader.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/src/api.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/src/bridge.js | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/package.json | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/vitest.config.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/tests/api.test.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-releases/authenticatedFrameTransport.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/JadeDragonRoute.tsx | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/jadeDragonApi.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/jadeDragonApi.test.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:04:40-06:00 | Computer: Vinnabok | File: packages/slot-client-core/src/contracts/presentationValidation.ts | Move Jade paid API validation and recovery into the game; provide neutral authenticated frame transport and reusable display validators.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: config/game-maintenance.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/GodsOfOlympusBundled.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/GodsOfOlympusHost.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PiratesFortuneBundled.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/PiratesFortuneHost.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/gameMaintenance.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/GameMaintenanceState.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/routeMetadata.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/slots/slotMaintenance.css | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/slots/SlotsLibraryPage.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Configuration/GameMaintenance.cs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server/Controllers/SlotsController.cs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server/FortuneForge.Server.csproj | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server/GameWorkers/Packages/GamePackageRuntime.cs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/catalog/slot-packages.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/package.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/scripts/verify-standalone.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/api.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/browser.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/index.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/manifest.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/olympusGame.css | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/OlympusGame.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/vitest.config.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/game-release.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/package-lock.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/JadeDragon/game-release.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/package.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/scripts/verify-standalone.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/api.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/assets/contracts/guide.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/browser.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/guide.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/index.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/manifest.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/piratesGame.css | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/PiratesGame.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/symbols.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/TreasureCoveIntroduction.tsx | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/vitest.config.ts | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: games/PiratesFortune/game-release.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: package-lock.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: package.json | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: scripts/verify-game-maintenance.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: scripts/verify-paid-frame-boundary.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: scripts/verify-slot-client-entries.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:19:38-06:00 | Computer: Vinnabok | File: scripts/verify-standalone-game-client.mjs | Add game-owned Pirates/Olympus paid clients, Jade frame API ownership, and explicit fail-closed catalogue/server maintenance availability.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/app/routes/SlotGameRoute.loading.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/gameMaintenance.css | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/GameMaintenanceOverlay.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-releases/authenticatedFrameTransport.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotAudioCoverage.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/slotGameManifest.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/catalogueResponsiveArtwork.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/games/OtherGamesPage.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/GameWorkers/GameMaintenanceTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/README.md | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/symbols.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/tests/api.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/tests/controller.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/tests/fixtures.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/JadeDragon/client/jade-dragon/README.md | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/README.md | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/tests/api.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/tests/controller.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/tests/fixtures.ts | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: scripts/build-jade-dragon.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: scripts/tests/game-worker-release.test.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: scripts/tests/verify-slot-client-entries.test.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/FortuneForge.SlotClient.LocalHarness.csproj | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/LocalTestWallet.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/Program.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/review.html | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/review.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/serve-review.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:47:45-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotClient.LocalHarness/verify-paid-browser.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:57:51-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/crookedRoadGame.css | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:57:51-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/src/CrookedRoadView.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:57:51-06:00 | Computer: Vinnabok | File: packages/slot-client-core/src/components/SidecarSpinControls.tsx | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:57:51-06:00 | Computer: Vinnabok | File: scripts/tests/game-navbar-boundary.test.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T17:57:51-06:00 | Computer: Vinnabok | File: scripts/verify-game-navbar.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment.
Edited: 2026-11-24T18:06:10-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/tests/standalone-boundary.test.mjs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Replace retired host-controller fixture with the retained host route for the same forbidden-import assertion.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/AcceptedGameActivityRealContractTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Catalog/CatalogContract.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/RoundAudit/SlotRoundAuditTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/CatalogueSlotBackendMigrationTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotMathAuditTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:12:38-06:00 | Computer: Vinnabok | File: FortuneForge.Server.Tests/Slots/SlotSpecialRoundProfilesTests.cs | Add game-owned Pirates/Olympus/Jade execution evidence, neutral frame transport, guarded maintenance availability, and deployment verification alignment. Update retired catalogue/classic fixtures to current native contracts without changing production gameplay.
Edited: 2026-11-24T18:15:45-06:00 | Computer: Vinnabok | File: games/SicBo/client/FortuneForge.Games.SicBo.Client/src/SicBoGame.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Wait for the asynchronous settled win cue before asserting audio order; retain all cue assertions.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/scripts/export-slot-guide-contracts.mjs | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/assets/catalogue/slots/crooked-road.artwork.json | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/catalog/cataloguePresentationArt.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-experience/GameExperienceRouteProvider.test.tsx | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/gameLoadingRegistry.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsA.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsA.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/features/game-loading/loadingPresentationsB.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/__fixtures__/engineGuide-contracts.json | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/games/slots/shared/engineGuide.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: fortuneforge.client/src/pages/GameMenus.test.ts | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: tools/FortuneForge.SlotContracts/Program.cs | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:25:19-06:00 | Computer: Vinnabok | File: tools/verify-slot-contracts.mjs | Add game-owned Pirates/Olympus/Jade execution evidence and deployment verification alignment. Align paid-only loading aliases, restore accurate artwork metadata, and refresh classic guide evidence from current selected package sources.
Edited: 2026-11-24T18:36:45-06:00 | Computer: Vinnabok | File: scripts/ReleaseGuardrails.psm1 | Use one release-owned SHA256 file helper so locked artifact verification does not depend on Get-FileHash module discovery; verify binary and empty-file digests and preserve upload integrity guards.
Edited: 2026-11-24T18:36:45-06:00 | Computer: Vinnabok | File: scripts/deploy.ps1 | Use one release-owned SHA256 file helper so locked artifact verification does not depend on Get-FileHash module discovery; verify binary and empty-file digests and preserve upload integrity guards.
Edited: 2026-11-24T18:36:45-06:00 | Computer: Vinnabok | File: scripts/tests/deploy-guardrails.tests.ps1 | Use one release-owned SHA256 file helper so locked artifact verification does not depend on Get-FileHash module discovery; verify binary and empty-file digests and preserve upload integrity guards.
Edited: 2026-11-24T18:36:45-06:00 | Computer: Vinnabok | File: scripts/tests/release-upload-archive.tests.ps1 | Use one release-owned SHA256 file helper so locked artifact verification does not depend on Get-FileHash module discovery; verify binary and empty-file digests and preserve upload integrity guards.
Edited: 2026-11-24T18:43:36-06:00 | Computer: Vinnabok | File: games/CrookedRoad/client/FortuneForge.Games.CrookedRoad.Client/scripts/copy-static.mjs | Preserve matching committed contract copies during immutable release builds. Synchronize only changed content; verify existing copies while held under read-only release locks.
Edited: 2026-11-24T18:43:36-06:00 | Computer: Vinnabok | File: games/HighNoonFortune/client/FortuneForge.Games.FrontierFortune.Client/scripts/sync-rules.mjs | Preserve matching committed contract copies during immutable release builds. Synchronize only changed content; verify existing copies while held under read-only release locks.
Edited: 2026-11-24T18:43:36-06:00 | Computer: Vinnabok | File: games/PharaohsAscent/client/FortuneForge.Games.PharaohsAscent.Client/scripts/sync-contracts.mjs | Preserve matching committed contract copies during immutable release builds. Synchronize only changed content; verify existing copies while held under read-only release locks.
Edited: 2026-11-24T19:14:30-06:00 | Computer: Vinnabok | File: .dockerignore | Include the committed game maintenance policy in the Docker publish context so the API can embed and enforce availability. Verify every host configuration dependency is admitted and copied by Docker.
Edited: 2026-11-24T19:14:30-06:00 | Computer: Vinnabok | File: Dockerfile | Include the committed game maintenance policy in the Docker publish context so the API can embed and enforce availability. Verify every host configuration dependency is admitted and copied by Docker.
