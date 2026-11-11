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
    [pscustomobject]@{ Path = $scopeWorkspace; Name = '@fortuneforge/games-dragon-tiger'; HasCheck = $true },
    [pscustomobject]@{ Path = $otherWorkspace; Name = '@fortuneforge/games-hi-lo'; HasCheck = $true }
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
foreach ($required in @('verify:game-source', 'verify:game-routes', 'verify:game-navbar', 'verify:ci-emulator', 'games:build', 'web:test', 'lint', 'web:build', 'check --workspace @fortuneforge/games-dragon-tiger', 'test --workspace @fortuneforge/games-dragon-tiger --if-present')) {
    Assert-ScopeEqual $scopedText.Contains($required) $true "scoped plan retains $required"
}
Assert-ScopeEqual @($scopedCommands | Where-Object { $_.Executable -ceq 'dotnet' }).Count 0 'scoped checks omit .NET'
Assert-ScopeEqual $scopedText.Contains('run games:check') $false 'scoped checks omit unrelated checks'
$fullCommands = @(Get-ReleaseVerificationCommands -Plan (New-ReleaseVerificationPlan -FullReason 'test') -Repository $scopeRepoRoot)
Assert-ScopeEqual @($fullCommands | Where-Object { $_.Executable -ceq 'dotnet' }).Count 1 'full suite retains the server test project'
$fullText = ($fullCommands | ForEach-Object { $_.Arguments -join ' ' }) -join "`n"
Assert-ScopeEqual $fullText.Contains('FortuneForge.Server.Tests.csproj') $true 'full suite retains the server test project'
foreach ($project in @('FortuneForge.Games.DragonTiger.Tests.csproj', 'FortuneForge.Games.HiLo.Engine.Tests.csproj',
    'FortuneForge.Games.HiLo.Server.Tests.csproj', 'FortuneForge.Games.AndarBahar.Tests.csproj')) {
    Assert-ScopeEqual $fullText.Contains($project) $false "full suite omits stale hardcoded game project $project"
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
foreach ($target in @('hosting', 'api', 'all')) {
    Assert-ReleaseTestWaiver -Target $target -TestFree -TestWaiverReason 'Owner requested the explicit test-free route.'
}
foreach ($reason in @('', '   ', "`n")) {
    Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target all -TestFree -TestWaiverReason $reason } 'test-free route requires an honest nonempty reason'
}
Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target all -TestFree -SkipTestSuites -TestWaiverReason 'Conflicting modes' } 'test-free route cannot combine with the broad-suite waiver'
Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target all -TestFree -LivePlaytest -TestWaiverReason 'Conflicting modes' } 'test-free route cannot combine with live playtest'
Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target all -LivePlaytest } 'playtest without explicit suite waiver'
Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target hosting -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner request' } 'playtest cannot masquerade as hosting-only'
foreach ($target in @('api', 'all')) {
    Assert-ReleaseTestWaiver -Target $target -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner requested incremental live playtest; broad suites deferred.'
    foreach ($reason in @('', '   ', "`n")) {
        Assert-ScopeThrows { Assert-ReleaseTestWaiver -Target $target -LivePlaytest -SkipTestSuites -TestWaiverReason $reason } 'playtest requires honest nonempty reason'
    }
}
$playtestPlan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha ('a' * 40) -Target all -LivePlaytest
Assert-ScopeEqual $playtestPlan.mode 'live-playtest' 'explicit independently calculated playtest mode'
Assert-ScopeThrows { Get-ReleaseVerificationCommands -Plan $playtestPlan -Repository $scopeRepoRoot -Target all -SkipTestSuites -TestWaiverReason 'Owner request' } 'playtest plan without explicit flag'
Assert-ScopeThrows { Get-ReleaseVerificationCommands -Plan (New-ReleaseVerificationPlan -FullReason 'full') -Repository $scopeRepoRoot -Target all -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner request' } 'flag cannot silently reduce a full plan'
Assert-ScopeThrows { Get-ReleaseVerificationCommands -Plan $playtestPlan -Repository $scopeRepoRoot -Target all -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner request' } 'playtest cannot omit fresh result reports'
$playtestCommands = @(Get-ReleaseVerificationCommands -Plan $playtestPlan -Repository $scopeRepoRoot -Target all -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner request' -FocusedResultsDirectory (Join-Path $scopeRepoRoot 'artifacts/synthetic-focused-reports'))
$focusedCommands = @($playtestCommands | Where-Object { $_.Executable -ceq 'dotnet' -and $_.Arguments[0] -ceq 'test' })
Assert-ScopeEqual $focusedCommands.Count 6 'all six fixed backend scopes mandatory'
$expectedFilters = @('FortuneForge.Server.Tests.Slots.', 'FortuneForge.Server.Tests.Games.Craps.FirestorePaidCrapsStoreTests',
    'FortuneForge.Server.Tests.SecurityAudit.PaidCraps', 'FortuneForge.Server.Tests.Activity.',
    'FortuneForge.Server.Tests.Cards.History.CardRoomHistoryQueryTests', 'FortuneForge.Server.Tests.SecurityAudit.InvestigationFirestoreTests')
for ($focusedIndex = 0; $focusedIndex -lt $focusedCommands.Count; $focusedIndex++) {
    $command = $focusedCommands[$focusedIndex]
    Assert-ScopeEqual $command.Arguments[([array]::IndexOf($command.Arguments, '--filter') + 1)] ('FullyQualifiedName~' + $expectedFilters[$focusedIndex]) 'exact fixed focused filter'
    Assert-ScopeEqual ($command.Arguments -ccontains 'RunConfiguration.TreatNoTestsAsError=true') $true 'zero-match tests fail'
    Assert-ScopeEqual ($command.Arguments -ccontains '--no-build') $true 'focused tests use current run build'
    Assert-ScopeEqual ([IO.Path]::GetExtension($command.FocusedResultPath)) '.trx' 'every scope requires an independently checked report'
}
$playtestText = ($playtestCommands | ForEach-Object { $_.Executable + ' ' + ($_.Arguments -join ' ') }) -join "`n"
foreach ($required in @('verify:game-source', 'verify:game-routes', 'verify:game-navbar', 'verify:ci-emulator', 'games:check', 'games:build', 'lint', 'web:build', 'dotnet build')) {
    Assert-ScopeEqual $playtestText.Contains($required) $true "playtest retains $required"
}
Assert-ScopeEqual @($playtestCommands | Where-Object { $_.Executable -ceq 'npm.cmd' -and $_.Arguments[1] -cin @('test', 'web:test') }).Count 0 'broad npm tests explicitly deferred'

$testFreeCommands = @(Get-ReleaseVerificationCommands -Plan (New-ReleaseVerificationPlan -FullReason 'test-free') `
    -Repository $scopeRepoRoot -Target all -TestFree -TestWaiverReason 'Owner requested the explicit test-free route.')
$testFreeText = ($testFreeCommands | ForEach-Object { $_.Executable + ' ' + ($_.Arguments -join ' ') }) -join "`n"
Assert-ScopeEqual @($testFreeCommands | Where-Object {
    ($_.Executable -ceq 'dotnet' -and $_.Arguments[0] -ceq 'test') -or
    ($_.Executable -ceq 'npm.cmd' -and $_.Arguments[0] -ceq 'run' -and
        $_.Arguments[1] -cin @('test', 'web:test', 'verify:game-navbar', 'verify:ci-emulator'))
}).Count 0 'test-free command plan contains no test runner or npm wrapper that embeds node:test'
foreach ($required in @('verify-game-navbar.mjs', 'verify-ci-emulator-order.mjs', 'games:check', 'games:build', 'lint', 'web:build')) {
    Assert-ScopeEqual $testFreeText.Contains($required) $true "test-free route retains non-test check $required"
}

# Synthetic reports only: never invoke .NET, SDK/cloud commands or copy a repo.
$focusedReport = [IO.Path]::GetTempFileName()
try {
    $reportTemplate = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{0}</Results><ResultSummary outcome="{1}"><Counters total="{2}" executed="{3}" passed="{4}" /></ResultSummary></TestRun>'
    [IO.File]::WriteAllText($focusedReport, ($reportTemplate -f '<UnitTestResult outcome="Passed" />', 'Completed', 1, 1, 1))
    Assert-ReleaseFocusedTestResult -Path $focusedReport
    foreach ($values in @(
        @('', 'Completed', 0, 0, 0),
        @('<UnitTestResult outcome="NotExecuted" />', 'Completed', 1, 0, 0),
        @('<UnitTestResult outcome="Failed" />', 'Failed', 1, 1, 0),
        @('<UnitTestResult outcome="Passed" />', 'Aborted', 1, 1, 1),
        @('<UnitTestResult outcome="NotExecuted" />', 'Completed', 1, 1, 1),
        @('', 'Completed', 1, 1, 1))) {
        [IO.File]::WriteAllText($focusedReport, ($reportTemplate -f $values))
        Assert-ScopeThrows { Assert-ReleaseFocusedTestResult -Path $focusedReport } 'empty, skipped, failed, aborted or inconsistent focus report'
    }
    [IO.File]::WriteAllText($focusedReport, '<not-trx />')
    Assert-ScopeThrows { Assert-ReleaseFocusedTestResult -Path $focusedReport } 'unrecognized report'
}
finally { Remove-Item -LiteralPath $focusedReport -Force }
Assert-ScopeThrows { Assert-ReleaseFocusedTestResult -Path $focusedReport } 'missing report'
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
    foreach ($required in @('verify:game-source', 'verify:game-routes', 'verify:game-navbar', 'verify:ci-emulator', 'games:build', 'lint', 'web:build')) {
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
Assert-ScopeEqual $scopeDeploySource.Contains('testSuiteWaiver = [ordered]@{ skipped = [bool]($SkipTestSuites -or $TestFree); reason = $TestWaiverReason }') $true 'release record reports every waiver rather than passed tests'
foreach ($required in @('broadTestSuitesDeferred = [bool]($SkipTestSuites -or $TestFree)', 'preDeploymentTestsSkipped = [bool]$TestFree',
    'focusedTestScopes = if ($LivePlaytest)', "focusedTestsStatus = if (`$LivePlaytest)", "ciStatus = 'not-checked; no CI success claimed'",
    "`$verificationArguments += '-LivePlaytest'", "`$verificationArguments += @('-TestFree', '-TestWaiverReason', `$TestWaiverReason)",
    "if (`$TestFree)", 'Deployment guardrail tests are NOT RUN')) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "playtest records and forwards $required"
}
$scopeVerifySource = Get-Content -LiteralPath (Join-Path $scopeRepoRoot 'scripts/verify-release.ps1') -Raw
Assert-ScopeEqual $scopeVerifySource.Contains($waiverGuard) $true 'verifier independently rejects invalid waiver flags'
Assert-ScopeEqual $scopeVerifySource.Contains('standalone verification does not accept waivers') $true 'standalone verification cannot waive suites'
Assert-ScopeEqual $scopeVerifySource.Contains('application test suites NOT RUN under the recorded waiver') $true 'verification does not claim waived tests passed'
Assert-ScopeEqual $scopeVerifySource.Contains('all pre-deployment automated tests were NOT RUN under the recorded waiver') $true 'test-free verification never claims skipped tests passed'
Assert-ScopeEqual $scopeVerifySource.Contains('Assert-ReleaseFocusedTestResult -Path $command.FocusedResultPath') $true 'verifier checks actual focused results after execution'
Assert-ScopeEqual $scopeVerifySource.Contains('Focused report already exists before its test run') $true 'stale focused evidence rejected'

# Exercise Git parsing and immutable source policy through a read-only adapter.
# No repository, branch or worktree is created by these tests.
$module = Get-Module ReleaseGuardrails
$shaA = 'a' * 40
$shaB = 'b' * 40
$shaC = 'c' * 40
$fixture = [pscustomobject]@{
    Head = $shaC; Main = $shaC; Branch = 'main'; Dirty = @(); Flags = @('H synthetic.txt')
    Available = $true; Ancestor = $true; RawChanges = @($visual); Malformed = $false
    NameChanges = @($visual.Path); ToolBlob = ('1' * 40); WorkingBlob = ('1' * 40)
    Workspace = $scopeWorkspace; Stage = ''; Canonical = @(); MockBuildInputs = $true
}
$originalReader = & $module { (Get-Item Function:Invoke-ReleaseGit).ScriptBlock }
$originalBuildInputs = & $module { (Get-Item Function:Assert-ReleaseBuildInputs).ScriptBlock }
& $module {
    param($State)
    $script:ReleaseFixture = $State
    $script:ReleaseOriginalBuildInputs = (Get-Item Function:Assert-ReleaseBuildInputs).ScriptBlock
    function script:Assert-ReleaseBuildInputs {
        param($Repository)
        if (-not $script:ReleaseFixture.MockBuildInputs) { & $script:ReleaseOriginalBuildInputs -Repository $Repository }
    }
    function script:Invoke-ReleaseGit {
        param([string]$Repository, [string[]]$Arguments, [string[]]$InputLines)
        $state = $script:ReleaseFixture
        $code = 0; $output = @()
        switch ($Arguments[0]) {
            'rev-parse' {
                $query = $Arguments[-1]
                if ($query -ceq 'HEAD' -or $query -ceq 'HEAD^{commit}') { $output = @($state.Head) }
                elseif ($query -ceq 'refs/heads/main') { $output = @($state.Main) }
                elseif ($query.EndsWith('^{tree}')) { $output = @('2' * 40) }
                elseif ($query.Contains(':')) { $output = @($state.ToolBlob) }
                else { $output = @($query.Replace('^{commit}', '')) }
            }
            'symbolic-ref' { $output = @($state.Branch); if (-not $state.Branch) { $code = 1 } }
            'status' { $output = @($state.Dirty) }
            'ls-files' { $output = if ($Arguments -contains '--stage') { @($state.Stage) } else { @(($state.Flags -join [char]0) + [char]0) } }
            'hash-object' { $output = if ($Arguments -contains '--stdin-paths') { @($state.Canonical) } else { @($state.WorkingBlob) } }
            'cat-file' { if (-not $state.Available) { $code = 1 } }
            'merge-base' { if (-not $state.Ancestor) { $code = 1 } }
            'diff' {
                if ($Arguments -contains '--raw') {
                    $raw = ''
                    foreach ($change in $state.RawChanges) {
                        $raw += ':' + $change.OldMode + ' ' + $change.NewMode + ' ' + ('1' * 40) + ' ' + ('2' * 40) + ' ' + $change.Status + [char]0 + $change.Path + [char]0
                    }
                    if ($state.Malformed) { $raw = $raw.TrimEnd([char]0) }
                    $output = @($raw)
                } else {
                    $raw = ($state.NameChanges -join [char]0) + [char]0
                    if ($state.Malformed) { $raw = $raw.TrimEnd([char]0) }
                    $output = @($raw)
                }
            }
            'show' {
                if ($Arguments[1].EndsWith(':package.json')) { $output = @('{"workspaces":["' + $state.Workspace + '"]}') }
                else { $output = @('{"name":"@fortuneforge/games-dragon-tiger","scripts":{"check":"check"}}') }
            }
            default { throw "Unexpected Git inspection command: $($Arguments -join ' ')" }
        }
        return [pscustomobject]@{ ExitCode = $code; Output = @($output) }
    }
} $fixture
try {
    $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting
    Assert-ScopeEqual $plan.mode 'game-visual' 'complete NUL Git diff and committed workspace manifest without a test script'
    Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaA } 'exact candidate mismatch'
    $fixture.Branch = ''
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC } 'detached HEAD'
    $fixture.Branch = 'other'
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC } 'other branch at same commit'
    $fixture.Branch = 'main'; $fixture.Main = $shaB
    Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC } 'main moved'
    $fixture.Main = $shaC
    foreach ($dirty in @(' M tracked.cs', 'M  staged.cs', '?? untracked.cs')) {
        $fixture.Dirty = @($dirty)
        Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC } 'dirty tracked, staged or untracked source'
    }
    $fixture.Dirty = @()
    foreach ($flag in @('h hidden.cs', 'S sparse.cs')) {
        $fixture.Flags = @($flag)
        Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $scopeRepoRoot -Sha $shaC } 'masked index source'
    }
    $fixture.Flags = @('H synthetic.txt')
    Assert-ReleaseToolingMatchesCommit -Repository $scopeRepoRoot -Sha $shaC -Paths @('scripts/verify-release.ps1')
    $fixture.WorkingBlob = '3' * 40
    Assert-ScopeThrows { Assert-ReleaseToolingMatchesCommit -Repository $scopeRepoRoot -Sha $shaC -Paths @('scripts/verify-release.ps1') } 'dirty tooling canonical blob mismatch'
    $fixture.WorkingBlob = $fixture.ToolBlob
    $preflight = Get-ReleaseLocalPreflight -Repository $scopeRepoRoot -Commit $shaC
    Assert-ScopeEqual $preflight.CandidateSha $shaC 'immutable candidate identity'
    Assert-ScopeEqual $preflight.ToolingSha $shaC 'tooling and candidate must match'
    Assert-ScopeEqual $preflight.SourceMode 'existing-clean-checkout' 'existing source mode'
    $fixture.RawChanges = @((New-ScopeChange 'FortuneForge.Server/Program.cs'), $visual)
    $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'complete live baseline includes earlier backend delta'
    $fixture.RawChanges = @($visual)
    $fixture.Malformed = $true
    $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'truncated NUL record fails closed'
    $fixture.Malformed = $false
    $fixture.Available = $false
    $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'missing baseline object'
    $fixture.Available = $true; $fixture.Ancestor = $false
    $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting
    Assert-ScopeEqual $plan.mode 'full' 'nonancestor baseline'
    $fixture.Ancestor = $true
    foreach ($option in @('Rollback', 'Adoption')) {
        $options = @{ $option = $true }
        $plan = Get-ReleaseVerificationPlan -Repository $scopeRepoRoot -CandidateSha $shaC -HostingBaseSha $shaA -Target hosting @options
        Assert-ScopeEqual $plan.mode 'full' "$option never forces visual scope"
    }

    Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaC -RequireExact
    Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA
    foreach ($path in @('FortuneForge.Server/Games/Craps/CrapsController.cs', 'games/Craps/server/Contracts.cs', 'games/catalog/games/craps.game.json', 'Dockerfile', 'scripts/unknown-codegen.mjs', 'unknown/config.json')) {
        $fixture.NameChanges = @($visual.Path, $path)
        Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA } "hosting-only incompatible or unknown API delta $path"
    }
    $fixture.NameChanges = @($visual.Path)
    $fixture.Malformed = $true
    Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA } 'incomplete compatibility diff'
    $fixture.Malformed = $false
    Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha '' } 'unstamped API'
    Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA -RequireExact } 'combined Hosting requires newly stamped exact API'
    $fixture.Ancestor = $false
    Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA } 'API divergence'
    $fixture.Ancestor = $true; $fixture.Available = $false
    Assert-ScopeThrows { Assert-HostingApiCompatibility -Repository $scopeRepoRoot -CandidateSha $shaC -LiveApiSha $shaA } 'unavailable API source'
    $fixture.Available = $true

    # A synthetic plain text file exercises real Windows sharing semantics.
    # This directory has no Git metadata and contains no repository/source copy.
    $fixtureDirectory = Join-Path $scopeRepoRoot ('.tmp/release-lock-test-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
    $file = Join-Path $fixtureDirectory 'synthetic.txt'
    [System.IO.File]::WriteAllText($file, 'immutable synthetic input')
    $fixture.Stage = '100644 ' + ('1' * 40) + ' 0' + [char]9 + 'synthetic.txt' + [char]0
    $fixture.Canonical = @('1' * 40)
    $sourceLock = $null; $workspaceLock = $null
    try {
        $sourceLock = Enter-ReleaseSourceLock -Repository $fixtureDirectory -Sha $shaC
        Assert-ScopeEqual $sourceLock.FileCount 1 'one synthetic source file held'
        Assert-ScopeThrows { [System.IO.File]::WriteAllText($file, 'mutated') } 'source edit denied while source handle held'
        $workspaceLock = Enter-ReleaseWorkspaceLock -Repository $fixtureDirectory
        Assert-ScopeThrows { Enter-ReleaseWorkspaceLock -Repository $fixtureDirectory } 'concurrent release reservation'
        Exit-ReleaseSourceLock -Lock $sourceLock; $sourceLock = $null
        [System.IO.File]::WriteAllText($file, 'unlocked')
        $fixture.Canonical = @('9' * 40)
        Assert-ScopeThrows { Enter-ReleaseSourceLock -Repository $fixtureDirectory -Sha $shaC } 'locked canonical bytes mismatch'
        [System.IO.File]::WriteAllText($file, 'failure released the source handle')
        $fixture.MockBuildInputs = $false
        Assert-ReleaseCheckout -Repository $fixtureDirectory -Sha $shaC
        $lateEnvironment = Join-Path $fixtureDirectory '.env.production.local'
        [System.IO.File]::WriteAllText($lateEnvironment, 'VITE_SYNTHETIC=added-after-preflight')
        Assert-ScopeThrows { Assert-ReleaseCheckout -Repository $fixtureDirectory -Sha $shaC } 'environment injected after successful source preflight rejected at next command/stamp boundary'
        Remove-Item -LiteralPath $lateEnvironment
        $fixture.MockBuildInputs = $true
    }
    finally {
        Exit-ReleaseSourceLock -Lock $sourceLock
        if ($workspaceLock) { $workspaceLock.Dispose() }
        # Keep these tiny ignored synthetic files as test evidence; do not
        # recursively delete any release directory, prior fixture or source.
    }
}
finally {
    & $module {
        param($Reader, $BuildInputs)
        Set-Item Function:script:Invoke-ReleaseGit -Value $Reader
        Set-Item Function:script:Assert-ReleaseBuildInputs -Value $BuildInputs
        Remove-Variable -Name ReleaseFixture -Scope Script
        Remove-Variable -Name ReleaseOriginalBuildInputs -Scope Script
    } $originalReader $originalBuildInputs
}
try {
    $localEnvironment = Join-Path $fixtureDirectory '.env.production.local'
    [System.IO.File]::WriteAllText($localEnvironment, 'VITE_SYNTHETIC=unreviewed')
    Assert-ScopeThrows { & $module { param($Path) Assert-ReleaseBuildInputs -Repository $Path } $fixtureDirectory } 'implicit local environment file'
    Remove-Item -LiteralPath $localEnvironment
    $serverSettingsDirectory = Join-Path $fixtureDirectory 'FortuneForge.Server'
    [System.IO.Directory]::CreateDirectory($serverSettingsDirectory) | Out-Null
    $serverSettings = Join-Path $serverSettingsDirectory 'appsettings.Local.json'
    [System.IO.File]::WriteAllText($serverSettings, '{"Synthetic":true}')
    Assert-ScopeThrows { & $module { param($Path) Assert-ReleaseBuildInputs -Repository $Path } $fixtureDirectory } 'local server settings'
    Remove-Item -LiteralPath $serverSettings
    $previousOverride = [System.Environment]::GetEnvironmentVariable('VITE_RELEASE_GUARD_TEST', 'Process')
    [System.Environment]::SetEnvironmentVariable('VITE_RELEASE_GUARD_TEST', 'unreviewed', 'Process')
    Assert-ScopeThrows { & $module { param($Path) Assert-ReleaseBuildInputs -Repository $Path } $fixtureDirectory } 'implicit external Vite override'
}
finally {
    [System.Environment]::SetEnvironmentVariable('VITE_RELEASE_GUARD_TEST', $previousOverride, 'Process')
    if (Test-Path -LiteralPath $localEnvironment) { Remove-Item -LiteralPath $localEnvironment }
}
foreach ($required in @('Get-ReleaseLocalPreflight -Repository $repoRoot -Commit $Commit',
    '$releaseCheckout = $repoRoot', 'Enter-ReleaseWorkspaceLock', 'Enter-ReleaseSourceLock',
    'Exit-ReleaseSourceLock', 'sourceManifestSha256', 'Assert-HostingApiCompatibility')) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "existing checkout guard $required"
}
Assert-ScopeEqual ($scopeDeploySource.IndexOf('Get-ReleaseLocalPreflight -Repository $repoRoot -Commit $Commit') -lt $scopeDeploySource.IndexOf('$hostingBefore = Get-LiveHosting')) $true 'local refusal precedes any live inventory'
Assert-ScopeEqual $scopeDeploySource.Contains("'worktree', 'add'") $false 'deployment cannot create another worktree'
Assert-ScopeEqual $scopeDeploySource.Contains('worktree remove') $false 'deployment cannot delete an old release worktree'
Assert-ScopeEqual $scopeVerifySource.Contains('identical candidate/tooling SHA') $true 'verifier rejects tooling/candidate mismatch'
$stampGuard = 'Assert-ReleaseCheckout -Repository $Checkout -Sha $CandidateSha'
Assert-ScopeEqual ($scopeDeploySource.LastIndexOf($stampGuard, $scopeDeploySource.IndexOf('Write-ReleaseRecord -Record $marker')) -gt $scopeDeploySource.IndexOf('$marker = [ordered]@{')) $true 'source and ignored build-input guard immediately before Hosting stamp'
$cloudIgnore = Get-Content -LiteralPath (Join-Path $scopeRepoRoot '.gcloudignore') -Raw
Assert-ScopeEqual $cloudIgnore.Contains('#!include:.gitignore') $true 'ignored private/local inputs excluded from Cloud Run upload'
foreach ($pattern in @('**/.practice/**', '**/.data/**', '**/App_Data/**')) {
    Assert-ScopeEqual $cloudIgnore.Contains($pattern) $true "nested private sample exclusion $pattern"
}

$uploadPaths = @('Dockerfile', 'games/Test/src/Engine.cs', 'games/Test/src/space name.cs')
$upload = Assert-ReleaseUploadPaths -TrackedPaths $uploadPaths -UploadPaths $uploadPaths
Assert-ScopeEqual $upload.FileCount 3 'only locked tracked upload files accepted'
$ignore = New-ReleaseUploadIgnoreText -Paths $upload.Paths
Assert-ScopeEqual $ignore.Contains("`n**`n") $true 'strict upload excludes every unspecified path'
foreach ($exact in @('!/Dockerfile', '!/games/', '!/games/Test/', '!/games/Test/src/', '!/games/Test/src/Engine.cs', '!/games/Test/src/space\ name.cs')) {
    Assert-ScopeEqual $ignore.Contains($exact) $true "strict upload exact path $exact"
}
foreach ($privatePath in @('games/sample/.practice/private.json', 'games/sample/.data/private.json', 'games/sample/App_Data/private.json', 'unknown.txt')) {
    Assert-ScopeThrows { Assert-ReleaseUploadPaths -TrackedPaths $uploadPaths -UploadPaths @($uploadPaths + $privatePath) } "untracked or ignored upload $privatePath"
}
foreach ($unsupported in @('games/Test/[literal].json', 'games/Test/file!.json', 'games/Test/file#.json', '../escape', '/rooted')) {
    Assert-ScopeThrows { Assert-ReleaseUploadPaths -TrackedPaths @('Dockerfile', $unsupported) -UploadPaths @('Dockerfile', $unsupported) } 'unsupported literal filename cannot become an ignore glob'
    Assert-ScopeThrows { New-ReleaseUploadIgnoreText -Paths @('Dockerfile', $unsupported) } 'strict ignore generator refuses unsupported literals'
}
Assert-ScopeThrows { Assert-ReleaseUploadPaths -TrackedPaths $uploadPaths -UploadPaths @() } 'empty upload inventory'
Assert-ScopeThrows { Assert-ReleaseUploadPaths -TrackedPaths $uploadPaths -UploadPaths @('Dockerfile', 'Dockerfile') } 'duplicate upload inventory'

$expectedTag = 'us-east4-docker.pkg.dev/fortuneforgegame/cloud-run-source-deploy/fortuneforge-api:ff-aaaaaaaaaaaa-1234abcd'
function New-BuildEvidence {
    [pscustomobject]@{ id = '11111111-1111-4111-8111-111111111111'; status = 'SUCCESS';
        results = [pscustomobject]@{ images = @([pscustomobject]@{ name = $expectedTag; digest = ('sha256:' + ('a' * 64)) }) } }
}
$built = Get-ReleaseBuiltImage -Build (New-BuildEvidence) -ExpectedTag $expectedTag
Assert-ScopeEqual $built.Image 'us-east4-docker.pkg.dev/fortuneforgegame/cloud-run-source-deploy/fortuneforge-api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' 'digest comes directly from matching build evidence'
foreach ($status in @('QUEUED', 'WORKING', 'FAILURE', 'CANCELLED', 'TIMEOUT', '')) {
    $build = New-BuildEvidence; $build.status = $status
    Assert-ScopeThrows { Get-ReleaseBuiltImage -Build $build -ExpectedTag $expectedTag } "non-successful build $status"
}
$build = New-BuildEvidence; $build.id = 'missing-build-id'
Assert-ScopeThrows { Get-ReleaseBuiltImage -Build $build -ExpectedTag $expectedTag } 'invalid build identity'
$build = New-BuildEvidence; $build.results.images[0].name = $expectedTag + '-other'
Assert-ScopeThrows { Get-ReleaseBuiltImage -Build $build -ExpectedTag $expectedTag } 'different result tag'
$build = New-BuildEvidence; $build.results.images += $build.results.images[0]
Assert-ScopeThrows { Get-ReleaseBuiltImage -Build $build -ExpectedTag $expectedTag } 'ambiguous image results'
foreach ($digest in @('', 'sha256:abc', ('sha256:' + ('A' * 64)), 'mutable-tag')) {
    $build = New-BuildEvidence; $build.results.images[0].digest = $digest
    Assert-ScopeThrows { Get-ReleaseBuiltImage -Build $build -ExpectedTag $expectedTag } 'missing or invalid result digest'
}
$revisionImage = [pscustomobject]@{ spec = [pscustomobject]@{ containers = @([pscustomobject]@{ image = $built.Image }) } }
Assert-ApiRevisionImage -Revision $revisionImage -ExpectedImage $built.Image
$revisionImage.spec.containers[0].image = $expectedTag
Assert-ScopeThrows { Assert-ApiRevisionImage -Revision $revisionImage -ExpectedImage $built.Image } 'revision cannot use mutable tag'

# Invoke only this helper in memory with a synthetic SDK inventory; the deploy
# script's top-level code and all cloud actions remain unexecuted.
$deployAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $scopeRepoRoot 'scripts/deploy.ps1'), [ref]$null, [ref]$null)
$uploadFunction = $deployAst.Find({ param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $Node.Name -ceq 'New-ApiUploadContext' }, $false)
. ([scriptblock]::Create($uploadFunction.Extent.Text))
function Invoke-JsonCommand {
    param($Executable, $Arguments)
    Assert-ScopeEqual $Executable 'gcloud.cmd' 'local upload selector tool'
    Assert-ScopeEqual ($Arguments[0..1] -join ' ') 'meta list-files-for-upload' 'local-only SDK selection command'
    return @('Dockerfile', 'games/Test/src/Engine.cs')
}
$uploadFixture = Join-Path $scopeRepoRoot ('.tmp/release-upload-test-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($uploadFixture) | Out-Null
$uploadLock = [pscustomobject]@{ TrackedPaths = @('Dockerfile', 'games/Test/src/Engine.cs'); Streams = [System.Collections.Generic.List[System.IO.FileStream]]::new() }
try {
    $context = New-ApiUploadContext -Checkout $uploadFixture -LockedSource $uploadLock -RecordPath (Join-Path $uploadFixture 'synthetic-record.json')
    Assert-ScopeEqual $context.FileCount 2 'selector intersects exact tracked candidate paths'
    Assert-ScopeEqual (Get-ReleaseTextHash (Get-Content -LiteralPath $context.IgnoreFile -Raw)) $context.IgnoreSha256 'generated allowlist bytes recorded'
    Assert-ScopeThrows { [System.IO.File]::WriteAllText($context.IgnoreFile, '**') } 'generated ignored manifest is write-locked during upload'
}
finally { foreach ($stream in $uploadLock.Streams) { $stream.Dispose() }; Remove-Item Function:Invoke-JsonCommand; Remove-Item Function:New-ApiUploadContext }
foreach ($required in @("'builds', 'submit'", "'--ignore-file', `$uploadContext.IgnoreFile", "'--suppress-logs'", "'--image', `$builtImage.Image", "'--no-traffic'", 'apiBuildId', 'apiImageDigest', 'apiUploadManifestSha256', 'apiUploadIgnoreSha256', 'Assert-ApiRevisionImage')) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "safe build/deployment retains $required"
}
Assert-ScopeEqual $scopeDeploySource.Contains("'--source', `$releaseCheckout") $false 'unsupported Cloud Run source ignore path cannot be used'

$hostingPatterns = @(New-ReleaseHostingIgnorePatterns -Paths @('index.html', 'release.json', 'assets/app.js', 'assets/deep/font.woff2'))
foreach ($pattern in @('!(assets|index.html|release.json)', '!(assets|index.html|release.json)/**', 'assets/!(app.js|deep)', 'assets/deep/!(font.woff2)')) {
    Assert-ScopeEqual ($hostingPatterns -ccontains $pattern) $true "Firebase exact complement $pattern"
}
foreach ($unsafe in @('assets/[literal].js', 'assets/(a|b).js', 'assets/a b.js', '../escape')) {
    Assert-ScopeThrows { New-ReleaseHostingIgnorePatterns -Paths @('index.html', $unsafe) } 'unsupported Firebase literal segment'
}
foreach ($name in @('Write-ReleaseRecord', 'New-HostingReleaseContext')) {
    $function = $deployAst.Find({ param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $Node.Name -ceq $name }, $false)
    . ([scriptblock]::Create($function.Extent.Text))
}
$hostingFixture = Join-Path $scopeRepoRoot ('.tmp/hosting-artifact-case-' + [guid]::NewGuid().ToString('N'))
$syntheticDist = Join-Path $hostingFixture 'fortuneforge.client/dist'
[System.IO.Directory]::CreateDirectory((Join-Path $syntheticDist 'assets')) | Out-Null
[System.IO.File]::WriteAllText((Join-Path $syntheticDist 'index.html'), '<html>synthetic built artifact</html>')
[System.IO.File]::WriteAllText((Join-Path $syntheticDist 'assets/app.js'), '// synthetic built artifact')
$originalConfig = [pscustomobject]@{ hosting = [pscustomobject]@{ public = 'fortuneforge.client/dist'; ignore = @('**/private/**');
    headers = @([pscustomobject]@{source='/assets/**';headers=@([pscustomobject]@{key='Cache-Control';value='immutable'})});
    rewrites = @([pscustomobject]@{source='/api/**';run=[pscustomobject]@{serviceId='synthetic-api';region='synthetic-region'}}) } }
[System.IO.File]::WriteAllText((Join-Path $hostingFixture 'firebase.json'), ($originalConfig | ConvertTo-Json -Depth 15))
$checkoutFunction = (Get-Command Assert-ReleaseCheckout).ScriptBlock
$script:HostingCandidate = $shaC
$script:HostingCheckpoints = 0
function Assert-ReleaseCheckout {
    param($Repository, $Sha)
    Assert-ScopeEqual $Sha $script:HostingCandidate 'synthetic candidate checkpoint'
    $script:HostingCheckpoints++
}
$hostingContext = $null
try {
    $hostingContext = New-HostingReleaseContext -Checkout $hostingFixture -CandidateSha $shaC -RecordPath (Join-Path $hostingFixture 'synthetic-record.json')
    Assert-ScopeEqual $hostingContext.FileCount 3 'built index, asset and candidate marker staged exactly'
    Assert-ScopeEqual ($script:HostingCheckpoints -ge 2) $true 'source/build inputs checked before stamp'
    $stagedConfig = Get-Content -LiteralPath $hostingContext.ConfigPath -Raw | ConvertFrom-Json
    Assert-ScopeEqual $stagedConfig.hosting.public $hostingContext.Directory 'absolute unique built-artifact public directory'
    Assert-ScopeEqual ($stagedConfig.hosting.headers | ConvertTo-Json -Depth 10 -Compress) ($originalConfig.hosting.headers | ConvertTo-Json -Depth 10 -Compress) 'candidate Hosting headers preserved'
    Assert-ScopeEqual ($stagedConfig.hosting.rewrites | ConvertTo-Json -Depth 10 -Compress) ($originalConfig.hosting.rewrites | ConvertTo-Json -Depth 10 -Compress) 'candidate API rewrites preserved'
    Assert-ScopeEqual ($stagedConfig.hosting.ignore -ccontains '**/private/**') $true 'candidate original exclusions preserved'
    Assert-ScopeEqual (Get-ReleaseTextHash (Get-Content -LiteralPath $hostingContext.ConfigPath -Raw)) $hostingContext.ConfigSha256 'generated config bytes recorded'
    foreach ($lockedPath in @((Join-Path $hostingContext.Directory 'assets/app.js'), (Join-Path $hostingContext.Directory 'release.json'), $hostingContext.ConfigPath)) {
        Assert-ScopeThrows { [System.IO.File]::WriteAllText($lockedPath, 'mutated') } 'verified asset, marker and Firebase config cannot be overwritten'
    }
    [System.IO.File]::WriteAllText((Join-Path $hostingContext.Directory 'assets/injected.js'), '// unmanifested')
    Assert-ScopeEqual ($stagedConfig.hosting.ignore -ccontains 'assets/!(app.js)') $true 'exact SDK complement excludes later injected sibling'
}
finally {
    if ($hostingContext) { Exit-ReleaseSourceLock -Lock $hostingContext.Lock }
    Set-Item Function:Assert-ReleaseCheckout -Value $checkoutFunction
    Remove-Item Function:Write-ReleaseRecord
    Remove-Item Function:New-HostingReleaseContext
}
foreach ($required in @('New-HostingReleaseContext -Checkout $releaseCheckout', "'--config', `$hostingContext.ConfigPath", 'hostingArtifactManifestSha256', 'hostingConfigSha256')) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "verified Hosting context retains $required"
}

$indexFunction = $deployAst.Find({ param($Node) $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $Node.Name -ceq 'New-FirestorePublicationContext' }, $false)
. ([scriptblock]::Create($indexFunction.Extent.Text))
$indexFixture = Join-Path $scopeRepoRoot ('.tmp/firestore-publication-case-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($indexFixture) | Out-Null
$indexLock = [pscustomobject]@{ Streams = [System.Collections.Generic.List[System.IO.FileStream]]::new() }
$indexPlan = [pscustomobject]@{ Indexes = @([pscustomobject]@{
    collectionGroup = 'syntheticMissing'; queryScope = 'COLLECTION'; fields = @(
        [pscustomobject]@{ fieldPath = 'owner'; order = 'ASCENDING' },
        [pscustomobject]@{ fieldPath = 'created'; order = 'DESCENDING' }
    ) }); FieldOverrides = @([pscustomobject]@{ collectionGroup = 'syntheticEvidence'; fieldPath = 'payload'; indexes = @() }) }
try {
    $context = New-FirestorePublicationContext -PublicationPlan $indexPlan -LockedSource $indexLock -RecordPath (Join-Path $indexFixture 'synthetic-record.json')
    $config = Get-Content -LiteralPath $context.ConfigPath -Raw | ConvertFrom-Json
    $indexes = Get-Content -LiteralPath $context.IndexesPath -Raw | ConvertFrom-Json
    Assert-ScopeEqual $config.firestore.indexes ([System.IO.Path]::GetFileName($context.IndexesPath)) 'subset config resolves only its run-owned manifest'
    Assert-ScopeEqual @($indexes.indexes).Count 1 'subset keeps only planned missing composites'
    Assert-ScopeEqual $indexes.indexes[0].collectionGroup 'syntheticMissing' 'planned definition bytes preserved'
    Assert-ScopeEqual @($indexes.fieldOverrides).Count 1 'subset keeps planned field exemption'
    Assert-ScopeEqual @($indexes.fieldOverrides[0].indexes).Count 0 'empty exemption remains an array'
    Assert-ScopeEqual (Get-ReleaseTextHash (Get-Content -LiteralPath $context.IndexesPath -Raw)) $context.IndexesSha256 'subset manifest hash recorded'
    Assert-ScopeEqual (Get-ReleaseTextHash (Get-Content -LiteralPath $context.ConfigPath -Raw)) $context.ConfigSha256 'subset config hash recorded'
    Assert-ScopeEqual $indexLock.Streams.Count 2 'both generated files leased through source cleanup'
    foreach ($path in @($context.IndexesPath, $context.ConfigPath)) {
        Assert-ScopeThrows { [System.IO.File]::WriteAllText($path, '{}') } 'generated database publication bytes cannot change'
        Assert-ScopeThrows { [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read).Dispose() } 'generated database publication handle denies new writer'
    }
    $collisionRecord = Join-Path $indexFixture 'collision-record.json'
    $collisionConfig = $collisionRecord + '.firestore.firebase.json'
    [System.IO.File]::WriteAllText($collisionConfig, 'earlier evidence')
    $failedLock = [pscustomobject]@{ Streams = [System.Collections.Generic.List[System.IO.FileStream]]::new() }
    Assert-ScopeThrows { New-FirestorePublicationContext -PublicationPlan $indexPlan -LockedSource $failedLock -RecordPath $collisionRecord } 'pre-existing config collision fails closed'
    Assert-ScopeEqual ([System.IO.File]::ReadAllText($collisionConfig)) 'earlier evidence' 'collision evidence never overwritten or removed'
    Assert-ScopeEqual $failedLock.Streams.Count 0 'failed context retains no partially transferred leases'
    $partialPath = $collisionRecord + '.firestore.indexes.json'
    $probe = [System.IO.File]::Open($partialPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $probe.Dispose()
}
finally { Exit-ReleaseSourceLock -Lock $indexLock; Remove-Item Function:New-FirestorePublicationContext }
foreach ($required in @('New-FirestorePublicationContext -PublicationPlan $publicationPlan -LockedSource $sourceLock', "'--config', `$indexContext.ConfigPath", 'firestoreIndexPublicationSha256', 'firestoreIndexConfigSha256')) {
    Assert-ScopeEqual $scopeDeploySource.Contains($required) $true "missing-only database publication retains $required"
}

Write-Host 'Release scope, synthetic Git contracts, immutable checkout locks and command-plan tests passed.' -ForegroundColor Green
