[CmdletBinding()]
param(
    [ValidateScript({
        if ([uri]::CheckHostName($_) -ne [UriHostNameType]::Dns -or
            $_ -notmatch '^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$') {
            throw 'TargetHost must be one exact DNS hostname, without a URL, port, wildcard, address range, or extra arguments.'
        }
        $true
    })]
    [string]$TargetHost = 'fortuneforgegame.web.app',
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SecurityTools.psm1') -Force
$manifest = Get-SecurityToolsManifest
if (-not $manifest.PSObject.Properties['sslyze']) { throw 'The security tools manifest does not yet define SSLyze.' }
$executable = Get-SecurityToolPath 'sslyze'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Install SSLyze first using ./tools/security/Install-SecurityTools.ps1 -Tool sslyze.'
}
if ($Check) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $python = Join-Path (Split-Path -Parent $executable) 'python.exe'
        & $python -c "import importlib.metadata; print('SSLyze ' + importlib.metadata.version('sslyze'))"
        $checkExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    exit $checkExitCode
}

$reports = New-SecurityReportDirectory 'sslyze'
$jsonPath = Join-Path $reports 'sslyze.json'
$consolePath = Join-Path $reports 'console.txt'
# Explicit commands avoid SSLyze's default preset and its additional attack checks.
# This run performs ordinary certificate validation and protocol/cipher negotiation.
$scanArguments = @('--mozilla_config=disable', '--slow_connection', '--certinfo',
    '--sslv2', '--sslv3', '--tlsv1', '--tlsv1_1', '--tlsv1_2', '--tlsv1_3',
    '--json_out', $jsonPath, "$($TargetHost.ToLowerInvariant()):443")
Write-Host "TLS target: $($TargetHost.ToLowerInvariant()):443"
Write-Host "Reports: $reports"
$startedAt = [DateTimeOffset]::Now.ToString('o')
$previousErrorAction = $ErrorActionPreference
Push-Location -LiteralPath $reports
try {
    # Windows PowerShell treats native stderr as an error even for exit code zero.
    $ErrorActionPreference = 'Continue'
    & $executable @scanArguments 2>&1 | Tee-Object -FilePath $consolePath | ForEach-Object { Write-Host $_ }
    $scanExitCode = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorAction; Pop-Location }
$run = [ordered]@{
    tool = 'SSLyze'
    version = $manifest.sslyze.version
    targetHost = $TargetHost.ToLowerInvariant()
    port = 443
    checks = @('certificate_info', 'ssl_2_0_cipher_suites', 'ssl_3_0_cipher_suites',
        'tls_1_0_cipher_suites', 'tls_1_1_cipher_suites', 'tls_1_2_cipher_suites', 'tls_1_3_cipher_suites')
    mozillaComplianceAssessment = $false
    startedAt = $startedAt
    completedAt = [DateTimeOffset]::Now.ToString('o')
    exitCode = $scanExitCode
    resultPath = $jsonPath
}
[IO.File]::WriteAllText((Join-Path $reports 'run.json'), ($run | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "SSLyze finished with exit code $scanExitCode. Review selected check statuses in sslyze.json."
exit $scanExitCode
