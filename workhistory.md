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
