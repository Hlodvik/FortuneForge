[CmdletBinding()]
param(
    [string]$TargetUrl = 'http://127.0.0.1:15150',
    [string]$SchemaPath = '',
    [switch]$Active,
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SecurityTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RuntimeScanners.psm1') -Force
$executable = Get-SecurityToolPath wapiti
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Install the manifest-pinned wapiti3 package in its isolated security tools environment first.'
}
if ($Check) {
    exit (Invoke-RuntimeScannerProcess -Executable $executable -Arguments @('--version'))
}
if (-not $Active) { throw 'Wapiti sends generated requests. Specify -Active for the disposable local API.' }
$target = Assert-SecurityTarget -TargetUrl $TargetUrl
if (-not $target.IsLoopback -or $target.AbsolutePath -ne '/') {
    throw 'Wapiti audits require a loopback API origin, without a path. Production active scans are not supported.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $SchemaPath) {
    $runPointer = Join-Path $repoRoot 'artifacts/security/current-app-run.txt'
    if (-not (Test-Path -LiteralPath $runPointer -PathType Leaf)) { throw 'Supply a local filtered OpenAPI schema through -SchemaPath.' }
    $runRoot = ([IO.File]::ReadAllText($runPointer)).Trim()
    $SchemaPath = Join-Path $runRoot 'openapi-local-scan.json'
}
if (-not (Test-Path -LiteralPath $SchemaPath -PathType Leaf)) { throw "Local OpenAPI schema is missing: $SchemaPath" }
$schema = Get-RuntimeOpenApiSchema -TargetUrl $target -Schema $SchemaPath | ConvertFrom-Json
if (-not $schema.PSObject.Properties['openapi'] -or -not $schema.PSObject.Properties['paths']) {
    throw 'Wapiti auditing requires a bundled OpenAPI JSON document with paths.'
}

# One exclusion policy feeds schema filtering, crawler exclusions and the HTTP guard.
$excludedPrefixes = @('/api/payments', '/api/accounts', '/api/admin')
$methods = @('get', 'post', 'put', 'patch', 'delete', 'options', 'head', 'trace')
$excludedOperations = @()
$operationCount = 0
foreach ($path in @($schema.paths.PSObject.Properties)) {
    $blocked = -not $path.Name.StartsWith('/api/', [StringComparison]::OrdinalIgnoreCase)
    foreach ($prefix in $excludedPrefixes) {
        if ($path.Name -ieq $prefix -or $path.Name.StartsWith($prefix + '/', [StringComparison]::OrdinalIgnoreCase)) { $blocked = $true }
    }
    foreach ($operation in @($path.Value.PSObject.Properties | Where-Object { $_.Name -in $methods })) {
        if ($blocked) { $excludedOperations += ('{0} {1}' -f $operation.Name.ToUpperInvariant(), $path.Name) }
        else { $operationCount++ }
    }
    if ($blocked) { $schema.paths.PSObject.Properties.Remove($path.Name) }
}
if ($operationCount -eq 0) { throw 'No API operations remain after applying the local audit exclusions.' }
$origin = $target.GetLeftPart([UriPartial]::Authority)
$schema | Add-Member -NotePropertyName servers -NotePropertyValue @(@{ url = $origin }) -Force

$reports = New-SecurityReportDirectory wapiti
$schemaCopy = Join-Path $reports 'openapi.json'
$scopePath = Join-Path $reports 'scope.json'
$launcherPath = Join-Path $reports 'local-wapiti.py'
$reportPath = Join-Path $reports 'wapiti.json'
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($schemaCopy, ($schema | ConvertTo-Json -Depth 100), $utf8)
[IO.File]::WriteAllText((Join-Path $reports 'excluded-operations.json'), (ConvertTo-Json -InputObject @($excludedOperations) -Depth 4), $utf8)
$scope = [ordered]@{
    target = $origin
    excludedPrefixes = $excludedPrefixes
    modules = @('xss', 'sql')
    importedOperations = $operationCount
    hardDeadlineSeconds = 120
    identity = 'Local API development-default player if enabled; no admin or multi-user authorization coverage.'
}
[IO.File]::WriteAllText($scopePath, ($scope | ConvertTo-Json -Depth 6), $utf8)

# Keep the installed package untouched. This per-run launcher confines all HTTP
# requests, disables redirect following and replaces off-site example/XSS URLs.
$launcher = @'
import json
import os
from pathlib import Path
import sys
import threading
from urllib.parse import urlsplit

root = Path(__file__).resolve().parent
scope = json.loads((root / "scope.json").read_text(encoding="utf-8"))
target = urlsplit(scope["target"])
target_port = target.port or (443 if target.scheme == "https" else 80)
local_example = scope["target"] + "/__wapiti_local_probe__/"

def deadline():
    (root / "deadline-reached.txt").write_text("The 120-second local audit deadline was reached; coverage may be partial.\n", encoding="utf-8")
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(124)

watchdog = threading.Timer(scope["hardDeadlineSeconds"], deadline)
watchdog.daemon = True
watchdog.start()

import httpx
original_send = httpx.AsyncClient.send

async def local_send(self, request, *args, **kwargs):
    url = request.url
    port = url.port or (443 if url.scheme == "https" else 80)
    path = url.path.lower()
    blocked = any(path == prefix.lower() or path.startswith(prefix.lower() + "/") for prefix in scope["excludedPrefixes"])
    if (url.scheme, url.host, port) != (target.scheme, target.hostname, target_port) or blocked:
        with (root / "blocked-requests.jsonl").open("a", encoding="utf-8") as log:
            log.write(json.dumps({"method": request.method, "url": str(url)}) + "\n")
        raise httpx.RequestError("Request blocked by the local audit scope.", request=request)
    kwargs["follow_redirects"] = False
    return await original_send(self, request, *args, **kwargs)

httpx.AsyncClient.send = local_send

from wapitiCore.attack.mod_xss import ModuleXss
from wapitiCore.parsers import swagger
import wapiti_swagger.parser as swagger_parser
ModuleXss.RANDOM_WEBSITE = local_example
for defaults in (swagger.default_string_values, swagger_parser.default_string_values):
    defaults.update({"hostname": target.hostname, "ipv4": "127.0.0.1", "ipv6": "::1", "uri": local_example, "url": local_example})

from wapitiCore.main.wapiti import wapiti_asyncio_wrapper
try:
    wapiti_asyncio_wrapper()
finally:
    watchdog.cancel()
'@
[IO.File]::WriteAllText($launcherPath, $launcher, $utf8)

$python = Join-Path (Split-Path -Parent $executable) 'python.exe'
$arguments = @($launcherPath, '-u', ($origin + '/api/'), '--scope', 'folder', '--swagger', $schemaCopy,
    '-m', 'xss,sql', '--headless', 'no', '--tasks', '1', '--depth', '1',
    '--max-links-per-page', '10', '--max-files-per-dir', '20', '--max-scan-time', '45', '--max-attack-time', '25',
    '--timeout', '3', '--verify-ssl', '1', '--no-bugreport', '--endpoint', $origin,
    '--store-session', (Join-Path $reports 'session'), '--store-config', (Join-Path $reports 'config'),
    '--log', (Join-Path $reports 'wapiti.log'), '-f', 'json', '-o', $reportPath)
foreach ($prefix in $excludedPrefixes) { $arguments += @('-x', ($origin + $prefix + '*')) }

$controlledEnvironment = @{ PYTHONUTF8 = '1'; PYTHONUNBUFFERED = '1'; HTTP_PROXY = ''; HTTPS_PROXY = ''; ALL_PROXY = ''; NO_PROXY = '*' }
$previousEnvironment = @{}
foreach ($name in $controlledEnvironment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $controlledEnvironment[$name], 'Process')
}
Write-Host "Wapiti reports: $reports"
Write-Host "Local SQL/XSS audit: $operationCount imported operations; 120-second hard deadline."
Push-Location -LiteralPath $reports
try { $exitCode = Invoke-RuntimeScannerProcess -Executable $python -Arguments $arguments }
finally {
    Pop-Location
    foreach ($name in $controlledEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
}
$summary = [ordered]@{
    tool = 'Wapiti'
    version = (Get-SecurityToolsManifest).wapiti.version
    target = $origin
    sourceSchema = [IO.Path]::GetFullPath($SchemaPath)
    importedOperations = $operationCount
    modules = $scope.modules
    identity = $scope.identity
    processExitCode = $exitCode
    reportExists = (Test-Path -LiteralPath $reportPath)
    timedOut = (Test-Path -LiteralPath (Join-Path $reports 'deadline-reached.txt'))
}
[IO.File]::WriteAllText((Join-Path $reports 'summary.json'), ($summary | ConvertTo-Json -Depth 6), $utf8)
Write-Host "Wapiti finished with process exit code $exitCode. Findings require reviewing the JSON report; exit zero is not a clean-security verdict."
if ($exitCode -eq 0 -and -not $summary.reportExists) { exit 1 }
exit $exitCode
