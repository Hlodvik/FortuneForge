function Assert-ReleaseSha {
    param([Parameter(Mandatory)][string]$Sha)

    if ($Sha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
        throw "Invalid release commit SHA: $Sha"
    }
}

function Assert-CommitOnMain {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Sha
    )

    Assert-ReleaseSha $Sha
    & git -C $Repository merge-base --is-ancestor $Sha refs/heads/main
    if ($LASTEXITCODE -ne 0) {
        throw "Release commit $Sha is not on the local main history."
    }
}

function Assert-ReleaseToolingMatchesCommit {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string[]]$Paths
    )

    Assert-ReleaseSha $Sha
    foreach ($relativePath in $Paths) {
        $committedBlob = & git -C $Repository rev-parse --verify "$Sha`:$relativePath" 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($committedBlob)) {
            throw "Release tooling $relativePath is not present in commit $Sha."
        }
        $absolutePath = Join-Path $Repository ($relativePath.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            throw "Release tooling $relativePath is missing from the active checkout."
        }
        $workingBlob = & git -C $Repository hash-object "--path=$relativePath" $absolutePath
        if ($LASTEXITCODE -ne 0 -or $workingBlob -cne $committedBlob) {
            throw "Release tooling $relativePath differs from committed $Sha. Commit and test the tooling before deploying."
        }
    }
}

function Assert-LiveReleaseSource {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$CandidateSha,
        [Parameter(Mandatory)][string]$Target,
        [AllowEmptyString()][string]$LiveSha = '',
        [switch]$AdoptUnstampedLive,
        [AllowEmptyString()][string]$ObservedFingerprint = '',
        [AllowEmptyString()][string]$ExpectedFingerprint = '',
        [switch]$AllowRollback,
        [AllowEmptyString()][string]$RollbackReason = ''
    )

    Assert-ReleaseSha $CandidateSha
    if ([string]::IsNullOrWhiteSpace($LiveSha)) {
        if (-not $AdoptUnstampedLive -or [string]::IsNullOrWhiteSpace($ExpectedFingerprint) -or
            $ExpectedFingerprint -cne $ObservedFingerprint) {
            throw "$Target is live without a source stamp. Deployment is blocked. Inspect the live release, then use -AdoptUnstampedLive with its exact observed fingerprint."
        }
        return
    }

    Assert-ReleaseSha $LiveSha
    & git -C $Repository merge-base --is-ancestor $LiveSha $CandidateSha
    if ($LASTEXITCODE -eq 0) { return }

    if (-not $AllowRollback -or [string]::IsNullOrWhiteSpace($RollbackReason)) {
        throw "$Target serves $LiveSha, which is not an ancestor of candidate $CandidateSha. An older or divergent release requires -AllowRollback and -RollbackReason."
    }
}

function Assert-HostingAdoptionRelease {
    param(
        [AllowEmptyString()][string]$LiveSha = '',
        [switch]$AdoptUnstampedLive,
        [Parameter(Mandatory)][string]$ObservedRelease,
        [AllowEmptyString()][string]$ExpectedRelease = ''
    )

    if ([string]::IsNullOrWhiteSpace($LiveSha) -and $AdoptUnstampedLive -and
        ([string]::IsNullOrWhiteSpace($ExpectedRelease) -or $ExpectedRelease -cne $ObservedRelease)) {
        throw 'Unstamped Hosting adoption requires the exact observed release identifier as well as its index fingerprint.'
    }
}

function Get-ReleaseTextHash {
    param([Parameter(Mandatory)][string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-HostingSourceSha {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$ResponseBody,
        [AllowEmptyString()][string]$IndexBody = '',
        [switch]$MarkerAbsent
    )

    if ($MarkerAbsent) { return '' }
    # Before the release marker exists, Firebase's catch-all rewrite returns
    # index.html. Any other HTML (including an error page) is not an adoption
    # fingerprint and must fail closed.
    if ($ResponseBody.TrimStart().StartsWith('<')) {
        if (-not [string]::IsNullOrEmpty($IndexBody) -and $ResponseBody -ceq $IndexBody) { return '' }
        throw 'Hosting release marker returned unexpected HTML, not the live index fallback.'
    }
    try {
        $marker = $ResponseBody | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw 'The live Hosting release marker is malformed; refusing to treat it as an unstamped release.'
    }
    if ($marker.schemaVersion -ne 1 -or $marker.target -cne 'hosting' -or
        [string]::IsNullOrWhiteSpace($marker.sourceSha)) {
        throw 'The live Hosting release marker has an unsupported format.'
    }
    Assert-ReleaseSha $marker.sourceSha
    return [string]$marker.sourceSha
}

function Get-ServingApiRevision {
    param([Parameter(Mandatory)]$Service)

    $serving = @($Service.status.traffic | Where-Object { $_ -and $_.percent -eq 100 })
    if ($serving.Count -ne 1 -or [string]::IsNullOrWhiteSpace($serving[0].revisionName)) {
        throw 'Cloud Run must have exactly one named revision serving 100% of traffic before release.'
    }
    return [string]$serving[0].revisionName
}

function Get-ApiRevisionSourceSha {
    param([Parameter(Mandatory)]$Revision)

    $containers = @($Revision.spec.containers | Where-Object { $_ })
    if ($containers.Count -ne 1) {
        throw 'Cloud Run revision does not have exactly one application container.'
    }
    $stamps = @($containers[0].env | Where-Object { $_ -and $_.name -ceq 'FF_RELEASE_SHA' })
    if ($stamps.Count -eq 0) { return '' }
    if ($stamps.Count -ne 1 -or [string]::IsNullOrWhiteSpace($stamps[0].value)) {
        throw 'Cloud Run revision has an invalid FF_RELEASE_SHA stamp.'
    }
    Assert-ReleaseSha $stamps[0].value
    return [string]$stamps[0].value
}

function Assert-ServingApiUnchanged {
    param([Parameter(Mandatory)]$Before, [Parameter(Mandatory)]$Now)

    if ($Now.RevisionName -cne $Before.RevisionName -or
        $Now.SourceSha -cne $Before.SourceSha) {
        throw 'API traffic changed during release preparation. Refusing to overwrite that release.'
    }
}

function Test-FirestoreIndexDefinitionMatches {
    param([Parameter(Mandatory)]$Expected, [Parameter(Mandatory)]$Actual)

    if ($Expected.collectionGroup -cne $Actual.collectionGroup -or
        $Expected.queryScope -cne $Actual.queryScope) { return $false }
    $expectedFields = @($Expected.fields | Where-Object { $_ })
    $actualFields = @($Actual.fields | Where-Object { $_ })
    if ($actualFields.Count -eq $expectedFields.Count + 1 -and
        $actualFields[-1].fieldPath -ceq '__name__' -and
        $expectedFields[-1].fieldPath -cne '__name__') {
        $actualFields = @($actualFields | Select-Object -First $expectedFields.Count)
    }
    if ($actualFields.Count -ne $expectedFields.Count) { return $false }
    for ($i = 0; $i -lt $expectedFields.Count; $i++) {
        if ($expectedFields[$i].fieldPath -cne $actualFields[$i].fieldPath -or
            $expectedFields[$i].order -cne $actualFields[$i].order -or
            $expectedFields[$i].arrayConfig -cne $actualFields[$i].arrayConfig) {
            return $false
        }
    }
    return $true
}

function Test-FirestoreIndexBelongsToGroup {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Group
    )

    $prefix = "projects/$Project/databases/(default)/collectionGroups/$Group/indexes/"
    return $Name.StartsWith($prefix, [System.StringComparison]::Ordinal)
}

function Get-UnreadyFirestoreIndexes {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Expected,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Actual
    )

    foreach ($definition in $Expected) {
        $matching = @($Actual | Where-Object {
            Test-FirestoreIndexDefinitionMatches -Expected $definition -Actual $_
        })
        if (@($matching | Where-Object { $_.state -ceq 'READY' }).Count -gt 0) { continue }
        $state = if ($matching.Count -eq 0) { 'MISSING' } else { [string]$matching[0].state }
        [pscustomobject]@{
            collectionGroup = [string]$definition.collectionGroup
            fields = (@($definition.fields | ForEach-Object { $_.fieldPath }) -join ',')
            state = $state
        }
    }
}

function Get-ReleaseToolPaths {
    @('scripts/deploy.ps1', 'scripts/ReleaseGuardrails.psm1',
      'scripts/tests/deploy-guardrails.tests.ps1', 'scripts/tests/release-scope.tests.ps1',
      'scripts/verify-release.ps1')
}

function Assert-ReleaseCheckout {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Sha)
    Assert-ReleaseSha $Sha
    $head = [string](& git -C $Repository rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Trim() -cne $Sha) { throw 'Release checkout HEAD is not the exact selected commit.' }
    $dirty = @(& git -C $Repository status --porcelain=v1 --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'Release verification requires a clean source checkout.' }
}

function New-ReleaseVerificationPlan {
    param(
        [string]$Target = 'all', [string]$BaseSha = '', [string]$CandidateSha = '',
        [AllowEmptyCollection()][object[]]$Changes = @(),
        [AllowEmptyCollection()][object[]]$Workspaces = @(),
        [string]$FullReason = ''
    )
    $plan = [ordered]@{ mode = 'full'; workspace = ''; baseSha = $BaseSha;
        candidateSha = $CandidateSha; reason = ''; changedPaths = @($Changes | ForEach-Object { $_.Path }) }
    if ($FullReason) { $plan.reason = $FullReason; return [pscustomobject]$plan }
    if ($Target -cne 'hosting') { $plan.reason = 'Only Hosting-only releases may use visual scope.'; return [pscustomobject]$plan }
    if ($Changes.Count -eq 0) { $plan.reason = 'No visual changes to scope.'; return [pscustomobject]$plan }
    $selected = $null
    $hasVisual = $false
    foreach ($change in $Changes) {
        $path = [string]$change.Path
        # Refuse unusual names, renames, deletions, executable bits, symlinks,
        # submodules, and type changes rather than guessing their impact.
        if ($path -cmatch '[\\\x00-\x20\x7f]' -or $path -cmatch '(^|/)\.\.?(/|$)' -or
            $change.Status -cnotmatch '^[AM]$' -or $change.NewMode -cne '100644' -or
            ($change.OldMode -cne '100644' -and $change.OldMode -cne '000000')) {
            $plan.reason = "Unsafe or unsupported change: $path"; return [pscustomobject]$plan
        }
        if ($path -cmatch '^(docs/.+\.md|README\.md)$') { continue }
        $workspace = @($Workspaces | Where-Object { $path.StartsWith($_.Path + '/', [System.StringComparison]::Ordinal) })
        if ($workspace.Count -ne 1) { $plan.reason = "Shared or unknown path: $path"; return [pscustomobject]$plan }
        $workspace = $workspace[0]
        if ($selected -and $selected.Path -cne $workspace.Path) {
            $plan.reason = 'Changes span multiple game workspaces.'; return [pscustomobject]$plan
        }
        $selected = $workspace
        $relative = $path.Substring($workspace.Path.Length + 1)
        $isVisual = $relative -cmatch '^src/.+\.css$' -or
            $relative -cmatch '^(src/assets|public)/.+\.(png|jpe?g|webp|avif|gif|svg|woff2?|ttf|otf)$'
        $isTestOrDoc = $relative -cmatch '^src/.+\.(test|spec)\.(tsx?|jsx?|mjs|cjs)$' -or
            $relative -cmatch '^src/.+\.e2e\.mjs$' -or
            $relative -cmatch '^tests/.+\.(tsx?|jsx?|mjs|cjs)$' -or
            $relative -cmatch '^(docs/.+\.md|README\.md)$'
        if (-not $isVisual -and -not $isTestOrDoc) {
            $plan.reason = "Nonvisual source, configuration, or manifest: $path"; return [pscustomobject]$plan
        }
        $hasVisual = $hasVisual -or $isVisual
    }
    if (-not $selected -or -not $hasVisual -or
        $selected.Name -cnotmatch '^@fortuneforge/games-[a-z0-9-]+$' -or
        -not $selected.HasCheck -or -not $selected.HasTest) {
        $plan.reason = 'A registered, testable game with a visual change is required.'; return [pscustomobject]$plan
    }
    $plan.mode = 'game-visual'
    $plan.workspace = $selected.Name
    $plan.reason = 'One game visual-only diff; shared checks and every game build remain mandatory.'
    return [pscustomobject]$plan
}

function Get-ReleaseVerificationPlan {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$CandidateSha,
        [string]$HostingBaseSha = '', [string]$Target = 'all',
        [switch]$Rollback, [switch]$Adoption
    )
    Assert-ReleaseSha $CandidateSha
    $reason = ''
    if ($Target -cne 'hosting') { $reason = 'API or combined release requires full verification.' }
    elseif ($Rollback -or $Adoption) { $reason = 'Rollback or adoption requires full verification.' }
    elseif ($HostingBaseSha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') { $reason = 'No valid live Hosting baseline.' }
    if ($reason) { return New-ReleaseVerificationPlan -Target $Target -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason $reason }
    try {
        & git -C $Repository cat-file -e "$HostingBaseSha`^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Missing baseline object.' }
    }
    catch { return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason 'Live Hosting commit is unavailable locally.' }
    try {
        & git -C $Repository merge-base --is-ancestor $HostingBaseSha $CandidateSha 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Nonancestor baseline.' }
    }
    catch { return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason 'Live Hosting baseline is not an ancestor.' }
    try {
        # -z keeps file names intact; disabling rename detection exposes both
        # sides, so any deletion falls back to full checks.
        $rawLines = @(& git -C $Repository diff --raw -z --no-renames --no-ext-diff --no-textconv $HostingBaseSha $CandidateSha --)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read live-to-candidate diff.' }
        $raw = $rawLines -join "`n"
        $parts = $raw.Split([char]0)
        if ($raw -and (-not $raw.EndsWith([string][char]0) -or $parts.Length % 2 -ne 1)) { throw 'Incomplete Git diff record.' }
        $changes = @()
        for ($index = 0; $index -lt $parts.Length - 1; $index += 2) {
            if ($parts[$index] -cnotmatch '^:([0-7]{6}) ([0-7]{6}) [0-9a-f]+ [0-9a-f]+ ([A-Z][0-9]*)$') { throw 'Unsupported Git diff record.' }
            $changes += [pscustomobject]@{ OldMode = $Matches[1]; NewMode = $Matches[2]; Status = $Matches[3]; Path = $parts[$index + 1] }
        }
        $packageText = @(& git -C $Repository show "${CandidateSha}:package.json")
        if ($LASTEXITCODE -ne 0) { throw 'Missing root workspace manifest.' }
        $package = ($packageText -join "`n") | ConvertFrom-Json -ErrorAction Stop
        $workspaces = @()
        foreach ($path in @($package.workspaces)) {
            if ($path -cnotmatch '^games/[A-Za-z0-9-]+/client/[A-Za-z0-9.-]+$') { continue }
            $manifestText = @(& git -C $Repository show "${CandidateSha}:$path/package.json")
            if ($LASTEXITCODE -ne 0) { throw "Missing workspace manifest: $path" }
            $manifest = ($manifestText -join "`n") | ConvertFrom-Json -ErrorAction Stop
            $workspaces += [pscustomobject]@{ Path = $path; Name = [string]$manifest.name;
                HasCheck = -not [string]::IsNullOrWhiteSpace($manifest.scripts.check);
                HasTest = -not [string]::IsNullOrWhiteSpace($manifest.scripts.test) }
        }
        return New-ReleaseVerificationPlan -Target $Target -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -Changes $changes -Workspaces $workspaces
    }
    catch {
        return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason "Cannot safely classify scope: $($_.Exception.Message)"
    }
}

function Assert-ReleaseTestWaiver {
    param([string]$Target = 'all', [switch]$SkipTestSuites, [string]$TestWaiverReason = '')
    if ($SkipTestSuites) {
        if ($Target -cne 'hosting') { throw '-SkipTestSuites is permitted only for a Hosting-only release.' }
        if ([string]::IsNullOrWhiteSpace($TestWaiverReason)) { throw '-SkipTestSuites requires a nonempty -TestWaiverReason.' }
    }
    elseif (-not [string]::IsNullOrEmpty($TestWaiverReason)) { throw '-TestWaiverReason requires -SkipTestSuites.' }
}

function Get-ReleaseVerificationCommands {
    param(
        [Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][string]$Repository,
        [string]$Target = 'all', [switch]$SkipTestSuites, [string]$TestWaiverReason = ''
    )
    Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason
    if ($Plan.mode -cnotin @('full', 'game-visual')) { throw 'Unknown verification mode.' }
    if ($Plan.mode -ceq 'game-visual' -and $Plan.workspace -cnotmatch '^@fortuneforge/games-[a-z0-9-]+$') { throw 'Invalid scoped workspace.' }
    $commands = [System.Collections.Generic.List[object]]::new()
    foreach ($script in @('verify:game-source', 'verify:game-routes')) {
        $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', $script) })
    }
    $checkArgs = if ($Plan.mode -ceq 'game-visual') { @('run', 'check', '--workspace', $Plan.workspace) } else { @('run', 'games:check') }
    $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = $checkArgs })
    $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'games:build') })
    if (-not $SkipTestSuites) {
        if ($Plan.mode -ceq 'game-visual') {
            $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'test', '--workspace', $Plan.workspace) })
            $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'web:test') })
        }
        else { $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'test') }) }
    }
    foreach ($script in @('lint', 'web:build')) { $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', $script) }) }
    if ($Plan.mode -ceq 'full' -and -not $SkipTestSuites) {
        foreach ($path in @('FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj',
            'games/DragonTiger/tests/FortuneForge.Games.DragonTiger.Tests/FortuneForge.Games.DragonTiger.Tests.csproj',
            'games/HiLo/tests/FortuneForge.Games.HiLo.Engine.Tests/FortuneForge.Games.HiLo.Engine.Tests.csproj',
            'games/HiLo/tests/FortuneForge.Games.HiLo.Server.Tests/FortuneForge.Games.HiLo.Server.Tests.csproj',
            'games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/FortuneForge.Games.AndarBahar.Tests.csproj')) {
            $commands.Add([pscustomobject]@{ Executable = 'dotnet'; Arguments = @('test', (Join-Path $Repository $path), '--configuration', 'Release') })
        }
    }
    return $commands.ToArray()
}

Export-ModuleMember -Function Assert-ReleaseSha, Assert-CommitOnMain, Assert-ReleaseToolingMatchesCommit, Assert-LiveReleaseSource, Assert-HostingAdoptionRelease, Get-ReleaseTextHash, Get-HostingSourceSha, Get-ServingApiRevision, Get-ApiRevisionSourceSha, Assert-ServingApiUnchanged, Test-FirestoreIndexDefinitionMatches, Test-FirestoreIndexBelongsToGroup, Get-UnreadyFirestoreIndexes, Get-ReleaseToolPaths, Assert-ReleaseCheckout, New-ReleaseVerificationPlan, Get-ReleaseVerificationPlan, Assert-ReleaseTestWaiver, Get-ReleaseVerificationCommands
