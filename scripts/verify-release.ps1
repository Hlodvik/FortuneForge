[CmdletBinding()]
param(
    [string]$Repository = '', [string]$ExpectedSha = '', [string]$ExpectedToolingSha = '',
    [ValidateSet('all', 'api', 'hosting')][string]$Target = 'all',
    [string]$HostingBaseSha = '', [string]$ExpectedMode = '', [string]$ExpectedWorkspace = '',
    [switch]$Rollback, [switch]$Adoption,
    [switch]$SkipTestSuites, [string]$TestWaiverReason = '', [switch]$LivePlaytest,
    [switch]$TestFree
)

$ErrorActionPreference = 'Stop'
$toolingRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ReleaseGuardrails.psm1') -Force
Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason -LivePlaytest:$LivePlaytest -TestFree:$TestFree
if (($SkipTestSuites -or $TestFree) -and (-not $Repository -or -not $ExpectedSha -or -not $ExpectedToolingSha -or -not $ExpectedMode)) {
    throw 'A test waiver requires the guarded immutable release context; standalone verification does not accept waivers.'
}
$repoRoot = if ($Repository) { (Resolve-Path -LiteralPath $Repository).Path } else { $toolingRoot }
# Check the committed database manifest before any build or verification command.
# A single ordered field plus its implicit document-name order is not a composite.
$firestoreDefinitions = Get-Content -LiteralPath (Join-Path $repoRoot 'firestore.indexes.json') -Raw | ConvertFrom-Json -ErrorAction Stop
Assert-NonRedundantFirestoreCompositeIndexes -Definitions @($firestoreDefinitions.indexes | Where-Object { $_ })
Assert-SupportedFirestoreFieldOverrides -Definitions @($firestoreDefinitions.fieldOverrides | Where-Object { $_ })

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Push-Location $repoRoot
    try {
        if ($ExpectedSha) { Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha }
        Write-Host "`n> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Executable exited with code $LASTEXITCODE."
        }
        if ($ExpectedSha) { Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha }
    }
    finally {
        Pop-Location
    }
}

# Guarded verification uses this existing checkout and exact candidate tooling.
if ($Repository -or $ExpectedSha -or $ExpectedToolingSha -or $ExpectedMode -or $HostingBaseSha) {
    Assert-ReleaseSha $ExpectedSha
    Assert-ReleaseSha $ExpectedToolingSha
    if ([System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') -ine [System.IO.Path]::GetFullPath($toolingRoot).TrimEnd('\') -or
        $ExpectedSha -cne $ExpectedToolingSha) {
        throw 'Existing-checkout verification requires identical candidate/tooling SHA and the active checkout path.'
    }
    $activeHead = [string](& git -C $toolingRoot rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or $activeHead.Trim() -cne $ExpectedToolingSha) { throw 'Trusted verification tooling HEAD changed.' }
    $main = [string](& git -C $toolingRoot rev-parse --verify refs/heads/main)
    if ($LASTEXITCODE -ne 0 -or $main.Trim() -cne $ExpectedToolingSha) { throw 'Trusted verification tooling is not current main.' }
    Assert-ReleaseToolingMatchesCommit -Repository $toolingRoot -Sha $ExpectedToolingSha -Paths (Get-ReleaseToolPaths)
    Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha
    $plan = Get-ReleaseVerificationPlan -Repository $repoRoot -CandidateSha $ExpectedSha -HostingBaseSha $HostingBaseSha -Target $Target -Rollback:$Rollback -Adoption:$Adoption -LivePlaytest:$LivePlaytest
    if ($plan.mode -cne $ExpectedMode -or $plan.workspace -cne $ExpectedWorkspace) { throw 'Verification scope differs from the independently recalculated live-to-candidate plan.' }
}
else {
    # Standalone npm run verify:release remains full verification.
    $plan = New-ReleaseVerificationPlan -FullReason 'Standalone verification defaults to the full suite.'
}
Write-Host "`nVerification mode: $($plan.mode) $($plan.workspace)" -ForegroundColor Yellow
Write-Host "Scope reason: $($plan.reason)"
if ($TestFree) { Write-Warning "All pre-deployment automated tests are NOT RUN. Waiver: $TestWaiverReason" }
elseif ($SkipTestSuites) { Write-Warning "Broad application test suites NOT RUN. Waiver: $TestWaiverReason" }
$focusedResultsDirectory = ''
if ($LivePlaytest) {
    # Fresh ignored reports are produced by this run, never reused as assurance.
    $focusedResultsDirectory = Join-Path $repoRoot ('artifacts/releases/focused-' + $ExpectedSha.Substring(0, 12) + '-' + [guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $focusedResultsDirectory) { throw 'Focused report directory already exists.' }
    [System.IO.Directory]::CreateDirectory($focusedResultsDirectory) | Out-Null
    Write-Host "Mandatory focused test reports: $focusedResultsDirectory"
}
foreach ($command in @(Get-ReleaseVerificationCommands -Plan $plan -Repository $repoRoot -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason -LivePlaytest:$LivePlaytest -TestFree:$TestFree -FocusedResultsDirectory $focusedResultsDirectory)) {
    if ($command.PSObject.Properties['FocusedResultPath'] -and (Test-Path -LiteralPath $command.FocusedResultPath)) { throw 'Focused report already exists before its test run.' }
    Invoke-Checked -Executable $command.Executable -Arguments $command.Arguments
    if ($command.PSObject.Properties['FocusedResultPath']) { Assert-ReleaseFocusedTestResult -Path $command.FocusedResultPath }
}
if ($ExpectedSha) { Assert-ReleaseCheckout -Repository $repoRoot -Sha $ExpectedSha }
if ($TestFree) {
    Write-Host "`nFortune Forge release build/checks passed ($($plan.mode)); all pre-deployment automated tests were NOT RUN under the recorded waiver." -ForegroundColor Yellow
}
elseif ($LivePlaytest) {
    Write-Host "`nLive-playtest build/checks and all fixed focused backend test scopes passed; broader application suites deferred under the recorded waiver. CI status is not claimed." -ForegroundColor Yellow
}
elseif ($SkipTestSuites) {
    Write-Host "`nFortune Forge release build/checks passed ($($plan.mode)); application test suites NOT RUN under the recorded waiver." -ForegroundColor Yellow
}
else { Write-Host "`nFortune Forge release verification passed ($($plan.mode))." -ForegroundColor Green }
