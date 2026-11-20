[CmdletBinding()]
param(
    [string]$TargetUrl = 'https://fortuneforgegame.web.app',
    [string[]]$AllowedHost = @('fortuneforgegame.web.app'),
    [switch]$Check,
    [switch]$InstallTemplates
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SecurityTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RuntimeScanners.psm1') -Force
$executable = Get-SecurityToolPath nuclei
if (-not (Test-Path -LiteralPath $executable)) { throw 'Install Nuclei with Install-SecurityTools.ps1 -Tool nuclei first.' }
if ($Check) {
    $code = Invoke-RuntimeScannerProcess $executable @('-version')
    exit $code
}
$templatesDirectory = Join-Path (Get-SecurityToolsHome) 'nuclei-templates'
if ($InstallTemplates) {
    # Explicit updates are separate from scans: every execution still checks the
    # reviewed file digests below, even when the upstream collection changes.
    $code = Invoke-RuntimeScannerProcess $executable @('-ut', '-ud', $templatesDirectory, '-no-color')
    exit $code
}
$target = Assert-SecurityTarget -TargetUrl $TargetUrl -AllowedHost $AllowedHost
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'nuclei-profile.json') -Raw | ConvertFrom-Json
$templatePrefix = [IO.Path]::GetFullPath($templatesDirectory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$approvedFiles = @()
foreach ($entry in $profile.templates) {
    $file = [IO.Path]::GetFullPath((Join-Path $templatesDirectory $entry.path))
    if (-not $file.StartsWith($templatePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'A reviewed template is missing. Run -InstallTemplates, then review any upstream profile changes.'
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($file))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($actual -ne $entry.sha256) { throw "Template changed since review: $($entry.path). Review it before updating nuclei-profile.json." }
    $approvedFiles += $file
}
$reports = New-SecurityReportDirectory nuclei
$findings = Join-Path $reports 'nuclei.jsonl'
$console = Join-Path $reports 'console.txt'
$configuration = Join-Path $reports 'nuclei.yaml'
[IO.File]::WriteAllText($configuration, '{}', [Text.UTF8Encoding]::new($false))
$arguments = @('-u', $target.AbsoluteUri, '-t', ($approvedFiles -join ','),
    '-config', $configuration, '-pt', 'http', '-dut', '-ni', '-dr', '-duc',
    '-dashboard=false', '-rl', '2', '-c', '2', '-bs', '1', '-retries', '0',
    '-timeout', '10', '-no-color', '-jsonl', '-omit-raw', '-o', $findings)
Write-Host "Running $($approvedFiles.Count) reviewed request-only templates. Reports: $reports"
Push-Location -LiteralPath $reports
try {
    # Extracted results can include exposed configuration. Keep all native
    # console details in the ignored report; display only identifiers/severity.
    $code = Invoke-RuntimeScannerProcess -Executable $executable -Arguments $arguments 6> $console
}
finally { Pop-Location }
if (Test-Path -LiteralPath $findings) {
    foreach ($line in Get-Content -LiteralPath $findings) {
        if ($line.Trim()) {
            $finding = $line | ConvertFrom-Json
            $matcher = if ($finding.PSObject.Properties['matcher-name']) { $finding.'matcher-name' } else { '' }
            Write-Host "$($finding.'template-id') [$($finding.info.severity)] $matcher"
        }
    }
}
Write-Host "Nuclei finished with exit code $code. Inspect console.txt for scan completion and any skipped templates."
exit $code
