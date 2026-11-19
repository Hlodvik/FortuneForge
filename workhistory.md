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
