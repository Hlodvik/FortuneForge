# Fortune Forge security tools

Standalone Windows setup for [ZAP](https://github.com/zaproxy/zaproxy),
[Trivy](https://github.com/aquasecurity/trivy), and
[Schemathesis](https://github.com/schemathesis/schemathesis). No license purchase,
Docker engine, application changes, deployment, or global PATH changes are required.
Pinned versions, official archive URLs, and archive SHA-256 hashes have one owner:
`versions.json`.

## Installation

From the repository root, in PowerShell:

```powershell
./tools/security/Install-SecurityTools.ps1
./tools/security/Invoke-SecurityScan.ps1 -Tool trivy -Check
./tools/security/Invoke-SecurityScan.ps1 -Tool zap -Check
./tools/security/Invoke-SecurityScan.ps1 -Tool schemathesis -Check
```

Prerequisites: Java 17+ for ZAP and Python 3.11+ for Schemathesis. The installer
defaults to this computer's `C:/Python313/python.exe`; use `-Python` to override.
Runtimes go under `%LOCALAPPDATA%/FortuneForgeTools/security`; set
`FF_SECURITY_TOOLS_HOME` to override that location. Schemathesis uses its own
virtual environment. Installation can be limited with `-Tool trivy`, `-Tool zap`,
or `-Tool schemathesis`. Existing installations are reused.

## Repository and container scans

```powershell
./tools/security/Invoke-SecurityScan.ps1 -Tool trivy
./tools/security/Invoke-SecurityScan.ps1 -Tool trivy -Image 'your-registry/fortuneforge@sha256:YOUR_DIGEST'
./tools/security/Invoke-SecurityScan.ps1 -Tool trivy -Path 'C:/path/to/published-api'
```

Trivy reports HIGH/CRITICAL dependency vulnerabilities, secrets, and supported
configuration checks. It includes development dependencies. Reports are private,
ignored JSON files under `artifacts/security`; console output contains counts,
not matched secret values. Findings produce exit code 1. A failed database
download or scanner error also fails the command; inspect its diagnostic output.

`trivy.yaml` excludes generated output, caches, runtime logs, dependencies installed
on disk, and local analysis artifacts. It still reads npm lockfiles. This repository does
not currently maintain NuGet lockfiles: a repository-only scan does **not** cover
all .NET packages. Scan the published API or deployed container for its
`*.deps.json` runtime dependencies; retain the existing CI .NET dependency audit
for broader package coverage. Trivy can pull a remote image directly with registry
credentials; a running Docker engine is only needed to read a Docker-local image.

## Running website and API scans

These commands do not start Fortune Forge, emulators, or deployment processes.
Start an isolated development instance with the existing Firestore emulator/mock
payment configuration when deployment work is finished, or provide an approved
staging URL. Keep test accounts and balances separate from production.

```powershell
# Bounded ZAP baseline; no active attack job or form submission.
./tools/security/Invoke-SecurityScan.ps1 -Tool zap -TargetUrl 'http://127.0.0.1:5150'

# Active web scan and generated API requests require explicit opt-in.
./tools/security/Invoke-SecurityScan.ps1 -Tool zap -TargetUrl 'http://127.0.0.1:5150' -Active
./tools/security/Invoke-SecurityScan.ps1 -Tool schemathesis -TargetUrl 'http://127.0.0.1:5150' -Active

# Remote staging requires HTTPS and its exact hostname, with no wildcard matching.
./tools/security/Invoke-SecurityScan.ps1 -Tool zap -TargetUrl 'https://ff-test.example' -AllowedHost 'ff-test.example'
```

Loopback is the default scope. No production URL is configured. Even crawling
performs real requests; use test data for all runtime scans. Reports and ZAP
sessions use a unique directory per run under `artifacts/security`.
Schemathesis runs inside that directory too, so its relative caches and test state
stay out of the source tree.

Schemathesis defaults to `/openapi/v1.json`, exposed by the API only in Development.
Use `-Schema` for another same-origin schema URL or an exported local JSON schema.
It overrides schema server URLs with `-TargetUrl`, rejects external references,
disables automatic account signup, and limits workers, rate, examples, and duration.
This initial suite covers examples, coverage, and fuzzing; it does not certify wallet
accounting, game integrity, authorization between users, or payment correctness.

For a dedicated test account, set `FF_SECURITY_TOKEN` in the current process before
scanning. The runners reference that environment variable rather than writing its
value into configuration or arguments. Reports may still include sensitive
request/response data; keep them out of Git and review before sharing.

## Verification

```powershell
./tools/security/tests/security-tools.tests.ps1
./tools/security/tests/runtime-scanners.tests.ps1
```

These checks validate target boundaries and generated scanner configuration
without contacting Fortune Forge. Version checks confirm that runtimes start;
they do not establish that the application has passed a security scan.
