[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverTests = Join-Path $repoRoot 'FortuneForge.Server.Tests\FortuneForge.Server.Tests.csproj'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Push-Location $repoRoot
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

Write-Host "`nVerifying source ownership and route inventory..." -ForegroundColor Yellow
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'verify:game-source')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'verify:game-routes')

Write-Host "`nRunning client and game checks..." -ForegroundColor Yellow
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'games:check')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'test')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'lint')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'build')

Write-Host "`nRunning server tests..." -ForegroundColor Yellow
Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @('test', $serverTests, '--configuration', 'Release')

Write-Host "`nFortune Forge release verification passed." -ForegroundColor Green
