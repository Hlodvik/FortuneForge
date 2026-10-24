[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverTests = Join-Path $repoRoot 'FortuneForge.Server.Tests\FortuneForge.Server.Tests.csproj'
$dragonTigerTests = Join-Path $repoRoot 'games\DragonTiger\tests\FortuneForge.Games.DragonTiger.Tests\FortuneForge.Games.DragonTiger.Tests.csproj'
$hiLoEngineTests = Join-Path $repoRoot 'games\HiLo\tests\FortuneForge.Games.HiLo.Engine.Tests\FortuneForge.Games.HiLo.Engine.Tests.csproj'
$hiLoServerTests = Join-Path $repoRoot 'games\HiLo\tests\FortuneForge.Games.HiLo.Server.Tests\FortuneForge.Games.HiLo.Server.Tests.csproj'

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
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'games:build')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'test')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'lint')
Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'web:build')

Write-Host "`nRunning server tests..." -ForegroundColor Yellow
Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @('test', $serverTests, '--configuration', 'Release')
Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @('test', $dragonTigerTests, '--configuration', 'Release')
Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @('test', $hiLoEngineTests, '--configuration', 'Release')
Invoke-Checked `
    -Executable 'dotnet' `
    -Arguments @('test', $hiLoServerTests, '--configuration', 'Release')

Write-Host "`nFortune Forge release verification passed." -ForegroundColor Green
