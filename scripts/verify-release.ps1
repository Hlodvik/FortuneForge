[CmdletBinding()]
param(
    [string]$Repository = '', [string]$ExpectedSha = '', [string]$ExpectedToolingSha = '',
    [ValidateSet('all', 'api', 'hosting')][string]$Target = 'all',
    [string]$HostingBaseSha = '', [string]$ExpectedMode = '', [string]$ExpectedWorkspace = '',
    [switch]$Rollback, [switch]$Adoption,
    [switch]$SkipTestSuites, [string]$TestWaiverReason = ''
)

$ErrorActionPreference = 'Stop'
$toolingRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ReleaseGuardrails.psm1') -Force
Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason
if ($SkipTestSuites -and (-not $Repository -or -not $ExpectedSha -or -not $ExpectedToolingSha -or -not $ExpectedMode)) {
    throw 'A test-suite waiver requires the guarded immutable release context; standalone verification does not accept waivers.'
}
$repoRoot = if ($Repository) { (Resolve-Path -LiteralPath $Repository).Path } else { $toolingRoot }

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

# Current main's committed verifier may verify an explicitly selected older
# application checkout; it never copies tooling into or changes that checkout.
if ($Repository -or $ExpectedSha -or $ExpectedToolingSha -or $ExpectedMode -or $HostingBaseSha) {
    Assert-ReleaseSha $ExpectedSha
    Assert-ReleaseSha $ExpectedToolingSha
    $activeHead = [string](& git -C $toolingRoot rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or $activeHead.Trim() -cne $ExpectedToolingSha) { throw 'Trusted verification tooling HEAD changed.' }
    $main = [string](& git -C $toolingRoot rev-parse --verify refs/heads/main)
    if ($LASTEXITCODE -ne 0 -or $main.Trim() -cne $ExpectedToolingSha) { throw 'Trusted verification tooling is not current main.' }
    Assert-ReleaseToolingMatchesCommit -Repository $toolingRoot -Sha $ExpectedToolingSha -Paths (Get-ReleaseToolPaths)
    Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha
    $plan = Get-ReleaseVerificationPlan -Repository $repoRoot -CandidateSha $ExpectedSha -HostingBaseSha $HostingBaseSha -Target $Target -Rollback:$Rollback -Adoption:$Adoption
    if ($plan.mode -cne $ExpectedMode -or $plan.workspace -cne $ExpectedWorkspace) { throw 'Verification scope differs from the independently recalculated live-to-candidate plan.' }
}
else {
    # Standalone npm run verify:release remains full verification.
    $plan = New-ReleaseVerificationPlan -FullReason 'Standalone verification defaults to the full suite.'
}
Write-Host "`nVerification mode: $($plan.mode) $($plan.workspace)" -ForegroundColor Yellow
Write-Host "Scope reason: $($plan.reason)"
if ($SkipTestSuites) { Write-Warning "Application test suites NOT RUN. Waiver: $TestWaiverReason" }
foreach ($command in @(Get-ReleaseVerificationCommands -Plan $plan -Repository $repoRoot -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason)) {
    Invoke-Checked -Executable $command.Executable -Arguments $command.Arguments
}
if ($ExpectedSha) { Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha }
if ($SkipTestSuites) {
    Write-Host "`nFortune Forge release build/checks passed ($($plan.mode)); application test suites NOT RUN under the recorded waiver." -ForegroundColor Yellow
}
else { Write-Host "`nFortune Forge release verification passed ($($plan.mode))." -ForegroundColor Green }
