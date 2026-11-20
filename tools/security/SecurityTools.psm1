Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SecurityToolsManifest {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
}

function Get-SecurityToolsHome {
    if ($env:FF_SECURITY_TOOLS_HOME) { return [IO.Path]::GetFullPath($env:FF_SECURITY_TOOLS_HOME) }
    if (-not $env:LOCALAPPDATA) { throw 'Set FF_SECURITY_TOOLS_HOME or run on Windows with LOCALAPPDATA defined.' }
    Join-Path $env:LOCALAPPDATA 'FortuneForgeTools/security'
}

function Get-SecurityToolPath {
    param([Parameter(Mandatory)][ValidateSet('trivy', 'zap', 'schemathesis', 'sslyze', 'nuclei', 'wapiti')][string]$Tool)
    $manifest = Get-SecurityToolsManifest
    Join-Path (Get-SecurityToolsHome) $manifest.$Tool.executable
}

function Assert-SecurityTarget {
    param([Parameter(Mandatory)][string]$TargetUrl, [string[]]$AllowedHost = @())
    $target = $null
    if (-not [uri]::TryCreate($TargetUrl, [UriKind]::Absolute, [ref]$target) -or
        $target.Scheme -notin @('http', 'https') -or $target.UserInfo -or $target.Query -or $target.Fragment) {
        throw 'TargetUrl must be an absolute HTTP(S) URL without credentials, a query, or a fragment.'
    }
    if (-not $target.IsLoopback) {
        if ($target.Scheme -ne 'https' -or $target.DnsSafeHost -notin $AllowedHost) {
            throw 'Remote scans require HTTPS and an exact staging hostname supplied through -AllowedHost.'
        }
    }
    return $target
}

function New-SecurityReportDirectory {
    param([Parameter(Mandatory)][string]$Tool)
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $runName = '{0}-{1}-{2}' -f $Tool, (Get-Date -Format 'yyyyMMdd-HHmmss'), [guid]::NewGuid().ToString('N').Substring(0, 8)
    $directory = Join-Path $repoRoot "artifacts/security/$runName"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    return $directory
}

Export-ModuleMember -Function Get-SecurityToolsManifest, Get-SecurityToolsHome, Get-SecurityToolPath, Assert-SecurityTarget, New-SecurityReportDirectory
