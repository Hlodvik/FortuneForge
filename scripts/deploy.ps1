[CmdletBinding()]
param(
    [ValidateSet('all', 'api', 'hosting')]
    [string]$Target = 'all',
    [string]$Commit = 'HEAD',
    [switch]$InspectLive,
    [switch]$AdoptUnstampedLive,
    [string]$ExpectedHostingRelease = '',
    [string]$ExpectedHostingIndexSha = '',
    [string]$ExpectedApiRevision = '',
    [switch]$AllowRollback,
    [string]$RollbackReason = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$checkoutParent = Split-Path -Parent $repoRoot
$project = 'fortuneforgegame'
$region = 'us-east4'
$serviceName = 'fortuneforge-api'
$baseUrl = 'https://fortuneforgegame.web.app'
Import-Module (Join-Path $PSScriptRoot 'ReleaseGuardrails.psm1') -Force

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try {
        Write-Host "`n> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}

function Invoke-JsonCommand {
    param([string]$Executable, [string[]]$Arguments)
    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable $($Arguments -join ' ') failed with code $LASTEXITCODE." }
    try { return (($output -join "`n") | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw "$Executable returned invalid JSON for $($Arguments -join ' ')." }
}

function Get-LiveHosting {
    $nonce = [guid]::NewGuid().ToString('N')
    $headers = @{ 'Cache-Control' = 'no-cache' }
    # Hosting canonicalizes /index.html to /. Read the canonical URL directly
    # so Windows PowerShell does not loop on its relative query-only redirect.
    $index = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/?release-check=$nonce" -Headers $headers
    if ($index.StatusCode -ne 200) { throw "Hosting index returned $($index.StatusCode)." }
    $marker = $null
    $markerAbsent = $false
    try {
        $marker = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/release.json?release-check=$nonce" -Headers $headers
        if ($marker.StatusCode -ne 200) { throw "Hosting release marker returned $($marker.StatusCode)." }
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
            $markerAbsent = $true
        }
        else { throw }
    }

    $token = (& gcloud.cmd auth print-access-token).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw 'Cannot authenticate the Firebase Hosting release inventory.'
    }
    $releases = Invoke-RestMethod -Method Get `
        -Uri "https://firebasehosting.googleapis.com/v1beta1/sites/$project/releases?pageSize=1" `
        -Headers @{ Authorization = "Bearer $token"; 'x-goog-user-project' = $project }
    $latest = @($releases.releases | Where-Object { $_ } | Select-Object -First 1)
    if ($latest.Count -ne 1 -or [string]::IsNullOrWhiteSpace($latest[0].name) -or
        [string]::IsNullOrWhiteSpace($latest[0].version.name)) {
        throw 'Cannot identify the current Firebase Hosting release and version.'
    }
    return [pscustomobject]@{
        SourceSha = Get-HostingSourceSha -ResponseBody $(if ($marker) { [string]$marker.Content } else { '' }) `
            -IndexBody ([string]$index.Content) -MarkerAbsent:$markerAbsent
        IndexSha256 = Get-ReleaseTextHash ([string]$index.Content)
        ReleaseName = [string]$latest[0].name
        VersionName = [string]$latest[0].version.name
    }
}

function Get-CloudRunService {
    return Invoke-JsonCommand 'gcloud.cmd' @(
        'run', 'services', 'describe', $serviceName,
        '--region', $region, '--project', $project, '--format=json'
    )
}

function Get-ApiRevision {
    param([string]$Name)
    return Invoke-JsonCommand 'gcloud.cmd' @(
        'run', 'revisions', 'describe', $Name,
        '--region', $region, '--project', $project, '--format=json'
    )
}

function Get-LiveApi {
    $service = Get-CloudRunService
    $revisionName = Get-ServingApiRevision $service
    $revision = Get-ApiRevision $revisionName
    return [pscustomobject]@{
        SourceSha = Get-ApiRevisionSourceSha $revision
        RevisionName = $revisionName
    }
}

function Get-ExpectedFirestoreIndexes {
    param([string]$Checkout)
    $path = Join-Path $Checkout 'firestore.indexes.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'The selected commit has no firestore.indexes.json; API traffic cannot be released.'
    }
    $definitions = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -ErrorAction Stop
    if (@($definitions.fieldOverrides | Where-Object { $_ }).Count -gt 0) {
        throw 'The release readiness gate does not support fieldOverrides yet; add a verified readiness check before releasing them.'
    }
    if (@($definitions.indexes | ForEach-Object { $_.fields } | Where-Object { $_ -and $_.vectorConfig }).Count -gt 0) {
        throw 'The release readiness gate does not support vector indexes yet; add a verified readiness check before releasing them.'
    }
    return @($definitions.indexes | Where-Object { $_ })
}

function Get-CurrentFirestoreIndexes {
    param([object[]]$Expected)
    $groups = @($Expected | ForEach-Object { [string]$_.collectionGroup } | Sort-Object -Unique)
    if ($groups.Count -eq 0) { return @() }
    $token = [string](& gcloud.cmd auth print-access-token)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw 'Cannot authenticate the Firestore index readiness check.'
    }
    $headers = @{ Authorization = "Bearer $($token.Trim())"; 'x-goog-user-project' = $project }
    $all = [System.Collections.Generic.List[object]]::new()
    foreach ($group in $groups) {
        $encodedGroup = [System.Uri]::EscapeDataString($group)
        $base = "https://firestore.googleapis.com/v1/projects/$project/databases/(default)/collectionGroups/$encodedGroup/indexes"
        $pageToken = ''
        do {
            $uri = if ($pageToken) { "$base`?pageToken=$([System.Uri]::EscapeDataString($pageToken))" } else { $base }
            $page = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
            foreach ($index in @($page.indexes | Where-Object { $_ })) {
                # Firestore's list response can include other collection groups;
                # never assign one of those indexes to the queried group.
                if (-not (Test-FirestoreIndexBelongsToGroup -Name ([string]$index.name) -Project $project -Group $group)) {
                    continue
                }
                $all.Add([pscustomobject]@{
                    collectionGroup = $group
                    queryScope = [string]$index.queryScope
                    fields = $index.fields
                    state = [string]$index.state
                })
            }
            $pageToken = [string]$page.nextPageToken
        } while ($pageToken)
    }
    return $all.ToArray()
}

function Wait-FirestoreIndexesReady {
    param([object[]]$Expected, [int]$TimeoutSeconds = 900)
    if ($Expected.Count -eq 0) { return }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $actual = @(Get-CurrentFirestoreIndexes -Expected $Expected)
        $unready = @(Get-UnreadyFirestoreIndexes -Expected $Expected -Actual $actual)
        if ($unready.Count -eq 0) { return }
        $failed = @($unready | Where-Object { $_.state -in @('NEEDS_REPAIR', 'ERROR') })
        $summary = ($unready | ForEach-Object { "$($_.collectionGroup) [$($_.fields)]: $($_.state)" }) -join '; '
        if ($failed.Count -gt 0) { throw "Firestore index build failed: $summary" }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Firestore indexes are not READY after $TimeoutSeconds seconds: $summary" }
        Write-Host "Waiting for Firestore indexes to become READY: $summary" -ForegroundColor Yellow
        Start-Sleep -Seconds 15
    } while ($true)
}

function Assert-LiveTargets {
    param([string]$CandidateSha, $Hosting, $Api)
    if ($Target -in @('all', 'hosting')) {
        Assert-HostingAdoptionRelease -LiveSha $Hosting.SourceSha -AdoptUnstampedLive:$AdoptUnstampedLive `
            -ObservedRelease $Hosting.ReleaseName -ExpectedRelease $ExpectedHostingRelease
        Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $CandidateSha -Target 'Hosting' `
            -LiveSha $Hosting.SourceSha -AdoptUnstampedLive:$AdoptUnstampedLive `
            -ObservedFingerprint $Hosting.IndexSha256 -ExpectedFingerprint $ExpectedHostingIndexSha `
            -AllowRollback:$AllowRollback -RollbackReason $RollbackReason
    }
    if ($Target -in @('all', 'api')) {
        Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $CandidateSha -Target 'API' `
            -LiveSha $Api.SourceSha -AdoptUnstampedLive:$AdoptUnstampedLive `
            -ObservedFingerprint $Api.RevisionName -ExpectedFingerprint $ExpectedApiRevision `
            -AllowRollback:$AllowRollback -RollbackReason $RollbackReason
    }
}

function Assert-UnchangedLiveTargets {
    param($BeforeHosting, $BeforeApi)
    if ($Target -in @('all', 'hosting')) {
        $now = Get-LiveHosting
        if ($now.ReleaseName -cne $BeforeHosting.ReleaseName -or
            $now.VersionName -cne $BeforeHosting.VersionName -or
            $now.IndexSha256 -cne $BeforeHosting.IndexSha256 -or
            $now.SourceSha -cne $BeforeHosting.SourceSha) {
            throw 'Hosting changed during release verification. Restart the deployment preflight.'
        }
    }
    if ($Target -in @('all', 'api')) {
        $now = Get-LiveApi
        Assert-ServingApiUnchanged -Before $BeforeApi -Now $now
    }
}

function Write-ReleaseRecord {
    param($Record, [string]$Path)
    $json = $Record | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

if ($AdoptUnstampedLive -and $AllowRollback) {
    throw 'Use either first-release adoption or an explicit rollback, not both.'
}
if ($AllowRollback -and [string]::IsNullOrWhiteSpace($RollbackReason)) {
    throw '-AllowRollback requires -RollbackReason.'
}

$hostingBefore = $null
$apiBefore = $null
if ($Target -in @('all', 'hosting')) { $hostingBefore = Get-LiveHosting }
if ($Target -in @('all', 'api')) { $apiBefore = Get-LiveApi }

if ($InspectLive) {
    if ($hostingBefore) {
        Write-Host "Hosting source SHA: $($hostingBefore.SourceSha)"
        Write-Host "Hosting index SHA-256: $($hostingBefore.IndexSha256)"
        Write-Host "Hosting release: $($hostingBefore.ReleaseName)"
        Write-Host "Hosting version: $($hostingBefore.VersionName)"
    }
    if ($apiBefore) {
        Write-Host "API source SHA: $($apiBefore.SourceSha)"
        Write-Host "API serving revision: $($apiBefore.RevisionName)"
    }
    return
}

# Use the same already-configured principal for Firebase and Cloud Run rather
# than silently choosing a different Firebase CLI default account.
$firebaseAccount = [string](& gcloud.cmd config get-value account --quiet 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($firebaseAccount) -or $firebaseAccount.Trim() -ceq '(unset)') {
    throw 'Configure an authenticated gcloud account before deploying.'
}
$firebaseAccount = $firebaseAccount.Trim()

$candidateSha = (& git -C $repoRoot rev-parse --verify "$Commit`^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Cannot resolve commit $Commit." }
Assert-CommitOnMain -Repository $repoRoot -Sha $candidateSha
$mainSha = (& git -C $repoRoot rev-parse --verify refs/heads/main).Trim()
$activeSha = (& git -C $repoRoot rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $activeSha -cne $mainSha) {
    throw 'Run the release tooling from the local main checkout, not a detached or stale checkout.'
}
$releaseToolPaths = @(
    'scripts/deploy.ps1',
    'scripts/ReleaseGuardrails.psm1',
    'scripts/tests/deploy-guardrails.tests.ps1',
    'scripts/verify-release.ps1'
)
Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $mainSha -Paths $releaseToolPaths
Assert-LiveTargets -CandidateSha $candidateSha -Hosting $hostingBefore -Api $apiBefore

# A release must never build from the active, possibly dirty development tree.
# This detached checkout creates no branch and leaves local edits untouched.
$checkoutName = 'FortuneForge-release-' + [guid]::NewGuid().ToString('N')
$releaseCheckout = Join-Path $checkoutParent $checkoutName
$checkoutCreated = $false
$recordPath = ''
$record = [ordered]@{
    schemaVersion = 1
    target = $Target
    candidateSha = $candidateSha
    startedAtUtc = [DateTime]::UtcNow.ToString('o')
    adoption = [bool]$AdoptUnstampedLive
    rollbackReason = $RollbackReason
    previousHostingRelease = if ($hostingBefore) { $hostingBefore.ReleaseName } else { '' }
    previousHostingVersion = if ($hostingBefore) { $hostingBefore.VersionName } else { '' }
    previousApiRevision = if ($apiBefore) { $apiBefore.RevisionName } else { '' }
    newHostingRelease = ''
    newApiRevision = ''
    status = 'preflight'
}

try {
    if (Test-Path -LiteralPath $releaseCheckout) { throw 'The generated release checkout already exists.' }
    Invoke-Checked -Executable 'git' -Arguments @('-C', $repoRoot, 'worktree', 'add', '--detach', $releaseCheckout, $candidateSha) -WorkingDirectory $repoRoot
    $checkoutCreated = $true

    $recordDirectory = Join-Path $repoRoot 'artifacts\releases'
    [System.IO.Directory]::CreateDirectory($recordDirectory) | Out-Null
    $recordPath = Join-Path $recordDirectory (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')) + '-' + $candidateSha.Substring(0, 12) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    Write-ReleaseRecord -Record $record -Path $recordPath

    Write-Host "`nVerifying immutable main commit $candidateSha..." -ForegroundColor Yellow
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('ci') -WorkingDirectory $releaseCheckout
    # verify:release runs game/client tests, lint, builds, and server tests. The
    # 250,000-spin slot simulation is a separate, change-triggered workflow.
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'verify:release') -WorkingDirectory $releaseCheckout
    # The orchestration tests run from the committed active main tooling, so
    # they still protect explicit releases of older commits without this suite.
    Invoke-Checked -Executable 'powershell.exe' -Arguments @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $repoRoot 'scripts/tests/deploy-guardrails.tests.ps1')
    ) -WorkingDirectory $repoRoot
    $sourceChanges = & git -C $releaseCheckout status --porcelain=v1 --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $sourceChanges) {
        throw 'Verification changed tracked or untracked source in the release checkout.'
    }
    $record.status = 'verified'
    Write-ReleaseRecord -Record $record -Path $recordPath

    $mainAfterVerification = (& git -C $repoRoot rev-parse --verify refs/heads/main).Trim()
    if ($LASTEXITCODE -ne 0 -or $mainAfterVerification -cne $mainSha) {
        throw 'Local main moved during release verification. Restart against the new committed history.'
    }
    Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $mainSha -Paths $releaseToolPaths
    Assert-UnchangedLiveTargets -BeforeHosting $hostingBefore -BeforeApi $apiBefore

    if ($Target -in @('all', 'api')) {
        $expectedIndexes = @(Get-ExpectedFirestoreIndexes -Checkout $releaseCheckout)
        $indexesChanged = $true
        if (-not [string]::IsNullOrWhiteSpace($apiBefore.SourceSha)) {
            & git -C $repoRoot diff --quiet $apiBefore.SourceSha $candidateSha -- firestore.indexes.json
            if ($LASTEXITCODE -eq 0) { $indexesChanged = $false }
            elseif ($LASTEXITCODE -ne 1) { throw 'Cannot compare committed Firestore index definitions.' }
        }
        $currentIndexes = @(Get-CurrentFirestoreIndexes -Expected $expectedIndexes)
        $missingIndexes = @(Get-UnreadyFirestoreIndexes -Expected $expectedIndexes -Actual $currentIndexes |
            Where-Object { $_.state -ceq 'MISSING' })
        if ($indexesChanged -and -not $AllowRollback -and $missingIndexes.Count -gt 0) {
            Write-Host "`nDeploying committed Firestore indexes before the API..." -ForegroundColor Yellow
            Invoke-Checked -Executable 'npx.cmd' -Arguments @(
                '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'firestore:indexes',
                '--project', $project, '--account', $firebaseAccount, '--non-interactive'
            ) -WorkingDirectory $releaseCheckout
        }
        elseif ($indexesChanged -and $AllowRollback) {
            Write-Warning 'Keeping existing Firestore indexes during source rollback; database indexes are not rolled back automatically.'
        }
        elseif ($indexesChanged) {
            Write-Host 'Required Firestore index definitions already exist; preserving them and checking readiness.' -ForegroundColor Yellow
        }

        # Firebase accepting an index definition does not mean its backfill has
        # completed. Never route the candidate API until its indexes are READY.
        Wait-FirestoreIndexesReady -Expected $expectedIndexes

        $apiImmediatelyBefore = Get-LiveApi
        Assert-ServingApiUnchanged -Before $apiBefore -Now $apiImmediatelyBefore

        Write-Host "`nBuilding a non-serving Cloud Run revision..." -ForegroundColor Yellow
        Invoke-Checked -Executable 'gcloud.cmd' -Arguments @(
            'run', 'deploy', $serviceName,
            '--source', $releaseCheckout,
            '--region', $region, '--project', $project,
            '--max-instances', '1', '--no-traffic',
            "--update-env-vars=FF_RELEASE_SHA=$candidateSha",
            "--update-labels=ff-source-sha=$candidateSha",
            '--quiet'
        ) -WorkingDirectory $releaseCheckout

        $service = Get-CloudRunService
        $newApiRevision = [string]$service.status.latestCreatedRevisionName
        if ([string]::IsNullOrWhiteSpace($newApiRevision) -or $newApiRevision -ceq $apiBefore.RevisionName) {
            throw 'Cloud Run did not expose a distinct new revision; traffic was not changed.'
        }
        $newRevision = Get-ApiRevision $newApiRevision
        if ((Get-ApiRevisionSourceSha $newRevision) -cne $candidateSha) {
            throw 'New Cloud Run revision lacks the expected source stamp; traffic was not changed.'
        }
        $record.newApiRevision = $newApiRevision
        Write-ReleaseRecord -Record $record -Path $recordPath

        # A Cloud Run source build may take minutes. Verify both index readiness
        # and the serving revision again immediately before moving traffic.
        Wait-FirestoreIndexesReady -Expected $expectedIndexes
        $apiAtTrafficSwitch = Get-LiveApi
        Assert-ServingApiUnchanged -Before $apiBefore -Now $apiAtTrafficSwitch

        Invoke-Checked -Executable 'gcloud.cmd' -Arguments @(
            'run', 'services', 'update-traffic', $serviceName,
            "--to-revisions=$($newApiRevision)=100",
            '--region', $region, '--project', $project, '--quiet'
        ) -WorkingDirectory $releaseCheckout
        $apiAfter = Get-LiveApi
        if ($apiAfter.RevisionName -cne $newApiRevision -or $apiAfter.SourceSha -cne $candidateSha) {
            throw 'Cloud Run traffic did not move to the stamped candidate revision.'
        }
    }

    if ($Target -in @('all', 'hosting')) {
        $hostingImmediatelyBefore = Get-LiveHosting
        if ($hostingImmediatelyBefore.ReleaseName -cne $hostingBefore.ReleaseName -or
            $hostingImmediatelyBefore.VersionName -cne $hostingBefore.VersionName -or
            $hostingImmediatelyBefore.IndexSha256 -cne $hostingBefore.IndexSha256) {
            throw 'Hosting changed before publication. Restart the deployment.'
        }
        $distDirectory = Join-Path $releaseCheckout 'fortuneforge.client\dist'
        if (-not (Test-Path -LiteralPath (Join-Path $distDirectory 'index.html'))) {
            throw 'The verified Hosting build has no index.html.'
        }
        $marker = [ordered]@{
            schemaVersion = 1
            target = 'hosting'
            sourceSha = $candidateSha
            builtAtUtc = [DateTime]::UtcNow.ToString('o')
        }
        Write-ReleaseRecord -Record $marker -Path (Join-Path $distDirectory 'release.json')
        Write-Host "`nPublishing stamped Firebase Hosting build..." -ForegroundColor Yellow
        Invoke-Checked -Executable 'npx.cmd' -Arguments @(
            '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'hosting',
            '--project', $project, '--account', $firebaseAccount, '--non-interactive'
        ) -WorkingDirectory $releaseCheckout

        $hostingAfter = $null
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            $hostingAfter = Get-LiveHosting
            if ($hostingAfter.SourceSha -ceq $candidateSha -and
                $hostingAfter.ReleaseName -cne $hostingBefore.ReleaseName) { break }
            Start-Sleep -Seconds 3
        }
        if ($hostingAfter.SourceSha -cne $candidateSha -or
            $hostingAfter.ReleaseName -ceq $hostingBefore.ReleaseName) {
            throw 'Hosting did not serve a new stamped release after deployment.'
        }
        $record.newHostingRelease = $hostingAfter.ReleaseName
        Write-ReleaseRecord -Record $record -Path $recordPath
    }

    Write-Host "`nRunning production smoke..." -ForegroundColor Yellow
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', 'smoke:production') -WorkingDirectory $releaseCheckout
    $record.status = 'complete'
    $record.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    Write-ReleaseRecord -Record $record -Path $recordPath
    Write-Host "`nFortune Forge deployment complete ($Target, $candidateSha)." -ForegroundColor Green
    Write-Host "Release record: $recordPath"
}
catch {
    if ($recordPath) {
        $record.status = 'failed'
        $record.error = $_.Exception.Message
        Write-ReleaseRecord -Record $record -Path $recordPath
    }
    if ($apiBefore) {
        Write-Warning "Previous API revision: $($apiBefore.RevisionName). Check current traffic before any explicit rollback."
    }
    if ($hostingBefore) {
        Write-Warning "Previous Hosting version: $($hostingBefore.VersionName). Check current release before any explicit rollback."
    }
    throw
}
finally {
    if ($checkoutCreated) {
        $resolvedParent = [System.IO.Path]::GetFullPath($checkoutParent).TrimEnd('\')
        $resolvedCheckout = [System.IO.Path]::GetFullPath($releaseCheckout)
        if ([System.IO.Path]::GetDirectoryName($resolvedCheckout).TrimEnd('\') -cne $resolvedParent -or
            [System.IO.Path]::GetFileName($resolvedCheckout) -cnotmatch '^FortuneForge-release-[0-9a-f]{32}$') {
            throw 'Refusing to remove a release checkout outside its validated workspace parent.'
        }
        & git -C $repoRoot worktree remove --force $resolvedCheckout
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not clean the temporary release checkout at $resolvedCheckout. It was not deleted."
        }
    }
}
