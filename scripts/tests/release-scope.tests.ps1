$ErrorActionPreference = 'Stop'
$scopeRepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $scopeRepoRoot 'scripts/ReleaseGuardrails.psm1') -Force

function Assert-ScopeEqual($Actual, $Expected, [string]$Name) {
    if ($Actual -cne $Expected) { throw "$Name expected '$Expected', got '$Actual'." }
}
function Assert-ScopeThrows([scriptblock]$Action, [string]$Name) {
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    if (-not $threw) { throw "$Name did not reject an unsafe checkout." }
}
function New-ScopeChange([string]$Path, [string]$Status = 'M', [string]$OldMode = '100644', [string]$NewMode = '100644') {
    [pscustomobject]@{ Path = $Path; Status = $Status; OldMode = $OldMode; NewMode = $NewMode }
}
$scopeWorkspace = 'games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client'
$otherWorkspace = 'games/HiLo/client/FortuneForge.Games.HiLo.Client'
$scopeWorkspaces = @(
    [pscustomobject]@{ Path = $scopeWorkspace; Name = '@fortuneforge/games-dragon-tiger'; HasCheck = $true; HasTest = $true },
    [pscustomobject]@{ Path = $otherWorkspace; Name = '@fortuneforge/games-hi-lo'; HasCheck = $true; HasTest = $true }
)
$visual = New-ScopeChange "$scopeWorkspace/src/dragonTiger.css"
$companion = New-ScopeChange "$scopeWorkspace/src/DragonTigerGame.test.tsx"
$scoped = New-ReleaseVerificationPlan -Target hosting -Changes @($visual, $companion, (New-ScopeChange 'docs/notes.md')) -Workspaces $scopeWorkspaces
Assert-ScopeEqual $scoped.mode 'game-visual' 'one-game CSS plus tests and docs'
Assert-ScopeEqual $scoped.workspace '@fortuneforge/games-dragon-tiger' 'exact affected workspace'
foreach ($asset in @('chip.png', 'chip.svg', 'font.woff2')) {
    $plan = New-ReleaseVerificationPlan -Target hosting -Changes @((New-ScopeChange "$scopeWorkspace/src/assets/$asset" 'A' '000000')) -Workspaces $scopeWorkspaces
    Assert-ScopeEqual $plan.mode 'game-visual' "new visual asset $asset"
}
foreach ($path in @('package-lock.json', 'package.json', 'scripts/deploy.ps1',
    'fortuneforge.client/src/wallet.ts', 'FortuneForge.Server/Program.cs',
    "$scopeWorkspace/package.json", "$scopeWorkspace/vite.config.ts", "$scopeWorkspace/src/gateway.ts",
    "$scopeWorkspace/src/DragonTigerGame.tsx", "$scopeWorkspace/src/data.json",
    "$otherWorkspace/src/hiLo.css", 'games/Unknown/client/Unknown.Client/src/foo.css')) {
    $plan = New-ReleaseVerificationPlan -Target hosting -Changes @($visual, (New-ScopeChange $path)) -Workspaces $scopeWorkspaces
    Assert-ScopeEqual $plan.mode 'full' "shared, cross-game, or unknown path $path"
}
foreach ($change in @((New-ScopeChange $visual.Path 'D' '100644' '000000'),
    (New-ScopeChange $visual.Path 'R100'), (New-ScopeChange $visual.Path 'T' '120000'),
    (New-ScopeChange $visual.Path 'M' '100755'), (New-ScopeChange $visual.Path 'M' '100644' '120000'),
    (New-ScopeChange $visual.Path 'M' '100644' '160000'),
    (New-ScopeChange "$scopeWorkspace/src/../escape.css"), (New-ScopeChange "$scopeWorkspace/src/bad`nname.css"))) {
    $plan = New-ReleaseVerificationPlan -Target hosting -Changes @($change) -Workspaces $scopeWorkspaces
    Assert-ScopeEqual $plan.mode 'full' 'unsafe status, mode or path'
}
foreach ($changes in @(@(), @($companion), @((New-ScopeChange 'docs/notes.md')))) {
    $plan = New-ReleaseVerificationPlan -Target hosting -Changes $changes -Workspaces $scopeWorkspaces
    Assert-ScopeEqual $plan.mode 'full' 'empty or nonvisual-only change'
}
foreach ($target in @('api', 'all')) {
    $plan = New-ReleaseVerificationPlan -Target $target -Changes @($visual) -Workspaces $scopeWorkspaces
    Assert-ScopeEqual $plan.mode 'full' "$target requires full checks"
}
$scopedCommands = @(Get-ReleaseVerificationCommands -Plan $scoped -Repository $scopeRepoRoot)
$scopedText = ($scopedCommands | ForEach-Object { $_.Executable + ' ' + ($_.Arguments -join ' ') }) -join "`n"
foreach ($required in @('verify:game-source', 'verify:game-routes', 'games:build', 'web:test', 'lint', 'web:build', 'check --workspace @fortuneforge/games-dragon-tiger', 'test --workspace @fortuneforge/games-dragon-tiger')) {
    Assert-ScopeEqual $scopedText.Contains($required) $true "scoped plan retains $required"
}
Assert-ScopeEqual @($scopedCommands | Where-Object { $_.Executable -ceq 'dotnet' }).Count 0 'scoped checks omit .NET'
Assert-ScopeEqual $scopedText.Contains('run games:check') $false 'scoped checks omit unrelated checks'
$fullCommands = @(Get-ReleaseVerificationCommands -Plan (New-ReleaseVerificationPlan -FullReason 'test') -Repository $scopeRepoRoot)
Assert-ScopeEqual @($fullCommands | Where-Object { $_.Executable -ceq 'dotnet' }).Count 5 'full suite retains all five .NET projects'
$fullText = ($fullCommands | ForEach-Object { $_.Arguments -join ' ' }) -join "`n"
foreach ($project in @('FortuneForge.Server.Tests.csproj', 'FortuneForge.Games.DragonTiger.Tests.csproj',
    'FortuneForge.Games.HiLo.Engine.Tests.csproj', 'FortuneForge.Games.HiLo.Server.Tests.csproj',
    'FortuneForge.Games.AndarBahar.Tests.csproj')) {
    Assert-ScopeEqual $fullText.Contains($project) $true "full suite retains $project"
}
Assert-ScopeEqual $fullText.Contains('run games:check') $true 'full suite retains all game checks'
Assert-ScopeEqual $fullText.Contains("run test`n") $true 'full suite retains all client tests'
Assert-ReleaseTestWaiver -Target hosting
Assert-ReleaseTestWaiver -Target hosting -SkipTestSuites -TestWaiverReason 'Owner requested reviewed visual release without test suites.'
foreach ($target in @('api', 'all')) {
    Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target $target -SkipTestSuites -TestWaiverReason 'Owner request' } "$target cannot waive test suites"
}
foreach ($reason in @('', '   ')) {
    Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target hosting -SkipTestSuites -TestWaiverReason $reason } 'missing waiver reason'
}
Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target hosting -TestWaiverReason 'Orphan reason' } 'reason without explicit waiver'
foreach ($plan in @($scoped, (New-ReleaseVerificationPlan -FullReason 'Tooling changes keep this full'))) {
    $normalCommands = @(Get-ReleaseVerificationCommands -Plan $plan -Repository $scopeRepoRoot)
    $waivedCommands = @(Get-ReleaseVerificationCommands -Plan $plan -Repository $scopeRepoRoot -Target hosting -SkipTestSuites -TestWaiverReason 'Owner request')
    $expectedRetained = @($normalCommands | Where-Object {
        -not (($_.Executable -ceq 'dotnet' -and $_.Arguments[0] -ceq 'test') -or
            ($_.Executable -ceq 'npm.cmd' -and $_.Arguments[0] -ceq 'run' -and $_.Arguments[1] -cin @('test', 'web:test')))
    })
    $actualText = ($waivedCommands | ForEach-Object { $_.Executable + ' ' + ($_.Arguments -join ' ') }) -join "`n"
    $expectedText = ($expectedRetained | ForEach-Object { $_.Executable + ' ' + ($_.Arguments -join ' ') }) -join "`n"
    Assert-ScopeEqual $actualText $expectedText "$($plan.mode) waiver omits only exact application test commands"
    foreach ($required in @('verify:game-source', 'verify:game-routes', 'games:build', 'lint', 'web:build')) {
        Assert-ScopeEqual $actualText.Contains($required) $true "waiver retains $required"
    }
    Assert-ScopeEqual $actualText.Contains($(if ($plan.mode -ceq 'full') { 'games:check' } else { 'check --workspace' })) $true 'waiver retains type checks'
}
$scopeDeploySource = Get-Content -LiteralPath (Join-Path $scopeRepoRoot 'scripts/deploy.ps1') -Raw
foreach ($required in @("(Join-Path `$repoRoot 'scripts/verify-release.ps1')", "'-Repository', `$releaseCheckout",
    "'-ExpectedSha', `$candidateSha", "'-ExpectedToolingSha', `$mainSha", 'verificationPlan = $verificationPlan',
    "'run', 'smoke:production'", "'scripts/tests/deploy-guardrails.tests.ps1'")) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "deployment retains $required"
}
Assert-ScopeEqual ((Get-ReleaseToolPaths) -contains 'scripts/tests/release-scope.tests.ps1') $true 'scope tests protected from dirty-tooling bypass'
$waiverGuard = 'Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason'
Assert-ScopeEqual ($scopeDeploySource.IndexOf($waiverGuard) -lt $scopeDeploySource.IndexOf('function Get-LiveHosting')) $true 'invalid waiver rejected before live reads'
Assert-ScopeEqual $scopeDeploySource.Contains("@('-SkipTestSuites', '-TestWaiverReason', `$TestWaiverReason)") $true 'waiver switch and exact reason forwarded to trusted verifier'
Assert-ScopeEqual $scopeDeploySource.Contains('testSuiteWaiver = [ordered]@{ skipped = [bool]$SkipTestSuites; reason = $TestWaiverReason }') $true 'release record reports waiver rather than passed tests'
$scopeVerifySource = Get-Content -LiteralPath (Join-Path $scopeRepoRoot 'scripts/verify-release.ps1') -Raw
Assert-ScopeEqual $scopeVerifySource.Contains($waiverGuard) $true 'verifier independently rejects invalid waiver flags'
Assert-ScopeEqual $scopeVerifySource.Contains('standalone verification does not accept waivers') $true 'standalone verification cannot waive suites'
Assert-ScopeEqual $scopeVerifySource.Contains('application test suites NOT RUN under the recorded waiver') $true 'verification does not claim waived tests passed'

# Real Git fixture: never infer the deployment delta from just HEAD's parent.
$scopeFixtureParent = Join-Path $scopeRepoRoot 'artifacts'
$scopeFixture = Join-Path $scopeFixtureParent ('release-scope-tests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($scopeFixture) | Out-Null
function Invoke-ScopeGit([string[]]$GitArguments) {
    $output = @(& git -C $scopeFixture -c user.name=ReleaseScopeTests -c user.email=scope@example.invalid @GitArguments)
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $($GitArguments -join ' ')" }
    return ($output -join "`n")
}
function Write-ScopeFixture([string]$Path, [string]$Text) {
    $absolute = Join-Path $scopeFixture $Path
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($absolute)) | Out-Null
    [System.IO.File]::WriteAllText($absolute, $Text)
}
function Commit-ScopeFixture([string]$Message) {
    Invoke-ScopeGit @('add', '--all') | Out-Null
    Invoke-ScopeGit @('commit', '-m', $Message) | Out-Null
    return Invoke-ScopeGit @('rev-parse', 'HEAD')
}
try {
    Invoke-ScopeGit @('init', '-b', 'main') | Out-Null
    Write-ScopeFixture 'package.json' ('{"workspaces":["' + $scopeWorkspace + '"]}')
    Write-ScopeFixture "$scopeWorkspace/package.json" '{"name":"@fortuneforge/games-dragon-tiger","scripts":{"check":"check","test":"test"}}'
    Write-ScopeFixture "$scopeWorkspace/src/dragonTiger.css" '.game { color: red; }'
    $base = Commit-ScopeFixture 'base'
    Write-ScopeFixture "$scopeWorkspace/src/dragonTiger.css" '.game { color: blue; }'
    $visualCommit = Commit-ScopeFixture 'visual'
    $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $visualCommit -HostingBaseSha $base -Target hosting
    Assert-ScopeEqual $plan.mode 'game-visual' 'real live-to-candidate CSS diff'
    Assert-ReleaseCheckout -Repository $scopeFixture -Sha $visualCommit
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeFixture -Sha $base } 'clean different HEAD'
    Write-ScopeFixture 'FortuneForge.Server/Program.cs' '// backend changed'
    $backendCommit = Commit-ScopeFixture 'backend'
    Write-ScopeFixture "$scopeWorkspace/src/dragonTiger.css" '.game { color: green; }'
    $latest = Commit-ScopeFixture 'visual after backend'
    $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $latest -HostingBaseSha $base -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'older live baseline includes hidden backend delta'
    $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $latest -HostingBaseSha $backendCommit -Target hosting
    Assert-ScopeEqual $plan.mode 'game-visual' 'already-live backend does not force unrelated tests'
    foreach ($baseline in @('', 'bad', ('f' * 40), $latest)) {
        $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $latest -HostingBaseSha $baseline -Target hosting
        Assert-ScopeEqual $plan.mode 'full' 'unknown or empty diff baseline'
    }
    $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $base -HostingBaseSha $latest -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'nonancestor baseline'
    foreach ($option in @('Rollback', 'Adoption')) {
        $options = @{ $option = $true }
        $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $latest -HostingBaseSha $backendCommit -Target hosting @options
        Assert-ScopeEqual $plan.mode 'full' "$option cannot scope"
    }
    Invoke-ScopeGit @('mv', "$scopeWorkspace/src/dragonTiger.css", "$scopeWorkspace/src/renamed.css") | Out-Null
    $renamed = Commit-ScopeFixture 'rename'
    $plan = Get-ReleaseVerificationPlan -Repository $scopeFixture -CandidateSha $renamed -HostingBaseSha $latest -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'Git rename exposes deletion and fails closed'
    Write-ScopeFixture 'untracked.txt' 'not immutable'
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeFixture -Sha $renamed } 'untracked source mutation'
}
finally {
    $resolvedFixture = [System.IO.Path]::GetFullPath($scopeFixture)
    if ([System.IO.Path]::GetDirectoryName($resolvedFixture) -cne [System.IO.Path]::GetFullPath($scopeFixtureParent) -or
        [System.IO.Path]::GetFileName($resolvedFixture) -cnotmatch '^release-scope-tests-[0-9a-f]{32}$') { throw 'Unsafe test-fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
Write-Host 'Release scope classifier, Git fixture, immutable checkout and command-plan tests passed.' -ForegroundColor Green
