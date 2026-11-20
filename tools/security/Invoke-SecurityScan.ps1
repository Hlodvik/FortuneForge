[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('trivy', 'zap', 'schemathesis')][string]$Tool,
    [string]$TargetUrl = 'http://127.0.0.1:5150',
    [string[]]$AllowedHost = @(),
    [string]$Schema = '/openapi/v1.json',
    [string]$Image = '',
    [string]$Path = '',
    [switch]$Active,
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SecurityTools.psm1') -Force
$executable = Get-SecurityToolPath $Tool
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Install $Tool first: ./tools/security/Install-SecurityTools.ps1 -Tool $Tool"
}
if ($Check) {
    $versionFlag = if ($Tool -eq 'zap') { '-version' } else { '--version' }
    $checkArguments = @($versionFlag)
    if ($Tool -eq 'zap') {
        $checkArguments = @('-cmd', '-silent', '-dir', (New-SecurityReportDirectory 'zap-version'), $versionFlag)
    }
    $previousErrorAction = $ErrorActionPreference
    Push-Location (Split-Path -Parent $executable)
    try {
        $ErrorActionPreference = 'Continue'
        & $executable @checkArguments
        $versionExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction; Pop-Location }
    exit $versionExitCode
}

# Resolve and validate network scope before creating reports or starting a scanner.
if ($Tool -ne 'trivy') {
    $target = Assert-SecurityTarget -TargetUrl $TargetUrl -AllowedHost $AllowedHost
    if ($Tool -eq 'schemathesis' -and -not $Active) { throw 'Schemathesis sends generated API requests; specify -Active against test data.' }
}
$reports = New-SecurityReportDirectory $Tool
Write-Host "Reports: $reports"
if ($Tool -eq 'trivy') {
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $scanTarget = if ($Image) { 'image' } else { 'fs' }
    if ($Image -and $Path) { throw 'Supply either -Image or -Path, not both.' }
    $subject = if ($Image) { $Image } elseif ($Path) { (Resolve-Path -LiteralPath $Path).Path } else { $repoRoot }
    & $executable $scanTarget --config (Join-Path $PSScriptRoot 'trivy.yaml') --exit-code 1 --format json --output (Join-Path $reports 'trivy.json') $subject
    $scanExitCode = $LASTEXITCODE
    if (Test-Path -LiteralPath (Join-Path $reports 'trivy.json')) {
        $report = Get-Content -LiteralPath (Join-Path $reports 'trivy.json') -Raw | ConvertFrom-Json
        $results = if ($report.PSObject.Properties['Results']) { @($report.Results) } else { @() }
        foreach ($result in $results) {
            $vulnerabilities = if ($result.PSObject.Properties['Vulnerabilities']) { @($result.Vulnerabilities).Count } else { 0 }
            $secrets = if ($result.PSObject.Properties['Secrets']) { @($result.Secrets).Count } else { 0 }
            $misconfigurations = if ($result.PSObject.Properties['Misconfigurations']) { @($result.Misconfigurations | Where-Object { $_.Status -eq 'FAIL' }).Count } else { 0 }
            if ($vulnerabilities + $secrets + $misconfigurations -gt 0) {
                Write-Host "$($result.Target): vulnerabilities=$vulnerabilities secrets=$secrets misconfigurations=$misconfigurations"
            }
        }
    }
    exit $scanExitCode
}

Import-Module (Join-Path $PSScriptRoot 'RuntimeScanners.psm1') -Force
if ($Tool -eq 'zap') {
    $result = Invoke-ZapSecurityScan -TargetUrl $target -ZapDirectory (Split-Path -Parent $executable) -ReportDirectory $reports -Active:$Active
}
else {
    $result = Invoke-SchemathesisSecurityScan -TargetUrl $target -SchemathesisExecutable $executable -ReportDirectory $reports -Schema $Schema -Active:$Active
}
Write-Host "$($result.Tool) $($result.Mode) finished with exit code $($result.ExitCode)."
exit $result.ExitCode
