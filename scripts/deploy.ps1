[CmdletBinding()]
param(
    [ValidateSet('all', 'api', 'hosting')]
    [string]$Target = 'all'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientRoot = Join-Path $repoRoot 'fortuneforge.client'
$serverProject = Join-Path $repoRoot 'FortuneForge.Server\FortuneForge.Server.csproj'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        Write-Host "`n> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Executable exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

if ($Target -in @('all', 'hosting')) {
    Write-Host "`nValidating the web client..." -ForegroundColor Yellow
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'lint') -WorkingDirectory $clientRoot
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'build') -WorkingDirectory $clientRoot
}

if ($Target -in @('all', 'api')) {
    Write-Host "`nValidating and deploying the Cloud Run API..." -ForegroundColor Yellow
    Invoke-Checked `
        -Executable 'dotnet' `
        -Arguments @(
            'run', '--project', (Join-Path $repoRoot 'tools\FortuneForge.SlotMath\FortuneForge.SlotMath.csproj'),
            '--configuration', 'Release', '--', '250000'
        ) `
        -WorkingDirectory $repoRoot
    Invoke-Checked `
        -Executable 'dotnet' `
        -Arguments @('build', $serverProject, '--configuration', 'Release') `
        -WorkingDirectory $repoRoot
    Invoke-Checked `
        -Executable 'gcloud.cmd' `
        -Arguments @(
            'run', 'deploy', 'fortuneforge-api',
            '--source', $repoRoot,
            '--region', 'us-east4',
            '--project', 'fortuneforgegame',
            '--quiet'
        ) `
        -WorkingDirectory $repoRoot
}

if ($Target -in @('all', 'hosting')) {
    Write-Host "`nDeploying Firebase Hosting..." -ForegroundColor Yellow
    Invoke-Checked `
        -Executable 'npx.cmd' `
        -Arguments @(
            '--yes', 'firebase-tools@14.4.0',
            'deploy', '--only', 'hosting',
            '--project', 'fortuneforgegame'
        ) `
        -WorkingDirectory $repoRoot
}

Write-Host "`nFortune Forge deployment complete ($Target)." -ForegroundColor Green
