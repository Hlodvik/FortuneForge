[CmdletBinding()]
param(
    [ValidateSet('all', 'api', 'hosting')]
    [string]$Target = 'all',
    [string]$Commit = 'HEAD',
    [ValidatePattern('^[a-z0-9][a-z0-9_-]{0,62}$')]
    [string]$ArtifactRepository = 'cloud-run-source-deploy',
    [switch]$PreflightOnly,
    [switch]$ContinuousAudit,
    [switch]$InspectLive,
    [switch]$AdoptUnstampedLive,
    [string]$ExpectedHostingRelease = '',
    [string]$ExpectedHostingIndexSha = '',
    [string]$ExpectedApiRevision = '',
    [switch]$AllowRollback,
    [string]$RollbackReason = '',
    [switch]$SkipTestSuites,
    [switch]$LivePlaytest,
    [switch]$TestFree,
    [string]$TestWaiverReason = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = 'fortuneforgegame'
$region = 'us-east4'
$serviceName = 'fortuneforge-api'
$baseUrl = 'https://fortuneforgegame.web.app'
Import-Module (Join-Path $PSScriptRoot 'ReleaseGuardrails.psm1') -Force
Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason -LivePlaytest:$LivePlaytest -TestFree:$TestFree
if ($ContinuousAudit -and $Target -eq 'hosting') { throw '-ContinuousAudit applies only to an API release.' }

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try {
        if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
        Write-Host "`n> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE." }
        if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
    }
    finally { Pop-Location }
}

function Invoke-JsonCommand {
    param([string]$Executable, [string[]]$Arguments)
    if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable $($Arguments -join ' ') failed with code $LASTEXITCODE." }
    try { $result = (($output -join "`n") | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw "$Executable returned invalid JSON for $($Arguments -join ' ')." }
    if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
    return $result
}

function New-ApiUploadContext {
    param([string]$Checkout, $LockedSource, [string]$RecordPath)
    # The SDK selector preserves the committed .gcloudignore exclusions. Its
    # local-only metadata command does not contact a service or create files.
    $inventory = @(Invoke-JsonCommand 'gcloud.cmd' @('meta', 'list-files-for-upload', $Checkout, '--format=json'))
    $upload = Assert-ReleaseUploadPaths -TrackedPaths $LockedSource.TrackedPaths -UploadPaths $inventory
    $text = New-ReleaseUploadIgnoreText -Paths $upload.Paths
    $path = $RecordPath + '.source.gcloudignore'
    $writer = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text)
        $writer.Write($bytes, 0, $bytes.Length)
    }
    finally { $writer.Dispose() }
    $reader = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $LockedSource.Streams.Add($reader)
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try { $actualHash = ([System.BitConverter]::ToString($hash.ComputeHash($reader))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
    $reader.Position = 0
    if ($actualHash -cne (Get-ReleaseTextHash $text)) { throw 'Generated source allowlist changed before its immutable handle was acquired.' }
    return [pscustomobject]@{ IgnoreFile = $path; IgnoreSha256 = $actualHash;
        ManifestSha256 = $upload.ManifestSha256; FileCount = $upload.FileCount }
}

function Assert-ApiBuildRepository {
    try {
        $repository = Invoke-JsonCommand 'gcloud.cmd' @('artifacts', 'repositories', 'describe', $ArtifactRepository,
            '--location', $region, '--project', $project, '--format=json')
    }
    catch { throw "A preconfigured DOCKER Artifact Registry repository $ArtifactRepository in $region is required. Configure access separately; this release never creates repositories or IAM grants. $($_.Exception.Message)" }
    if ($repository.name -cne "projects/$project/locations/$region/repositories/$ArtifactRepository" -or $repository.format -cne 'DOCKER') {
        throw 'Configured Artifact Registry repository identity/format does not match the release target.'
    }
}

function New-HostingReleaseContext {
    param([string]$Checkout, [string]$CandidateSha, [string]$RecordPath)
    Assert-ReleaseCheckout -Repository $Checkout -Sha $CandidateSha
    $dist = Join-Path $Checkout 'fortuneforge.client/dist'
    if (-not (Test-Path -LiteralPath (Join-Path $dist 'index.html'))) { throw 'The verified Hosting build has no index.html.' }
    $marker = [ordered]@{ schemaVersion = 1; target = 'hosting'; sourceSha = $CandidateSha; builtAtUtc = [DateTime]::UtcNow.ToString('o') }
    Assert-ReleaseCheckout -Repository $Checkout -Sha $CandidateSha
    Write-ReleaseRecord -Record $marker -Path (Join-Path $dist 'release.json')
    $original = Enter-ReleaseHostingLock -Directory $dist -CandidateSha $CandidateSha
    $staged = $null
    try {
        $directory = $RecordPath + '.hosting'
        if (Test-Path -LiteralPath $directory) { throw 'The unique Hosting artifact directory already exists; nothing will be overwritten.' }
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
        foreach ($file in $original.Files) {
            $destination = Join-Path $directory $file.Path
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
            $writer = [System.IO.File]::Open($destination, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $file.Stream.Position = 0; $file.Stream.CopyTo($writer) }
            finally { $writer.Dispose() }
        }
        $staged = Enter-ReleaseHostingLock -Directory $directory -CandidateSha $CandidateSha
        if ($staged.FileCount -ne $original.FileCount -or $staged.ManifestSha256 -cne $original.ManifestSha256) { throw 'Staged Hosting artifact bytes differ from the verified build manifest.' }
        $config = Get-Content -LiteralPath (Join-Path $Checkout 'firebase.json') -Raw | ConvertFrom-Json -ErrorAction Stop
        if (-not $config.hosting -or $config.hosting -is [array]) { throw 'A single committed Hosting configuration is required; no site configuration is guessed.' }
        $originalIgnore = @($config.hosting.ignore)
        $config.hosting.public = $directory
        # Preserve all candidate headers, rewrites and original exclusions.
        # Negative extglob complements exclude every unmanifested new file;
        # ordinary Git-style !file exceptions do not work in Firebase14.4.
        $config.hosting.ignore = @($originalIgnore) + @(New-ReleaseHostingIgnorePatterns -Paths @($staged.Files | ForEach-Object Path))
        $configPath = $RecordPath + '.firebase.json'
        $text = $config | ConvertTo-Json -Depth 30
        $writer = [System.IO.File]::Open($configPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try { $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text); $writer.Write($bytes, 0, $bytes.Length) }
        finally { $writer.Dispose() }
        $reader = [System.IO.File]::Open($configPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        $staged.Streams.Add($reader)
        $hash = [System.Security.Cryptography.SHA256]::Create()
        try { $configHash = ([System.BitConverter]::ToString($hash.ComputeHash($reader))).Replace('-', '').ToLowerInvariant() }
        finally { $hash.Dispose() }
        $reader.Position = 0
        if ($configHash -cne (Get-ReleaseTextHash $text)) { throw 'Generated Firebase configuration changed before its immutable handle was acquired.' }
        return [pscustomobject]@{ Lock = $staged; Directory = $directory; ConfigPath = $configPath;
            ConfigSha256 = $configHash; ManifestSha256 = $staged.ManifestSha256; FileCount = $staged.FileCount }
    }
    catch { Exit-ReleaseSourceLock -Lock $staged; throw }
    finally { Exit-ReleaseSourceLock -Lock $original }
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

function Get-LiveFirestoreRules {
    $token = [string](& gcloud.cmd auth print-access-token)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw 'Cannot authenticate the Firestore rules release check.'
    }
    $headers = @{ Authorization = "Bearer $($token.Trim())"; 'x-goog-user-project' = $project }
    $uri = "https://firebaserules.googleapis.com/v1/projects/$project/releases/cloud.firestore"
    try { $release = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
            return [pscustomobject]@{ ReleaseName = ''; RulesetName = ''; ContentHash = '' }
        }
        throw
    }
    if ([string]::IsNullOrWhiteSpace($release.name) -or [string]::IsNullOrWhiteSpace($release.rulesetName)) {
        throw 'The Firestore rules release has no ruleset identity.'
    }
    $ruleset = Invoke-RestMethod -Method Get -Uri "https://firebaserules.googleapis.com/v1/$($release.rulesetName)" -Headers $headers
    $files = @($ruleset.source.files | Where-Object { $_ })
    if ($files.Count -ne 1 -or [string]::IsNullOrWhiteSpace($files[0].content)) {
        throw 'The Firestore ruleset is not a single readable rules file.'
    }
    return [pscustomobject]@{
        ReleaseName = [string]$release.name
        RulesetName = [string]$release.rulesetName
        ContentHash = Get-FirestoreRulesHash ([string]$files[0].content)
    }
}

function Get-ExpectedFirestoreRulesHash {
    param([string]$Checkout)
    $path = Join-Path $Checkout 'firestore.rules'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'The selected commit has no firestore.rules; API traffic cannot be released.'
    }
    $content = Get-Content -LiteralPath $path -Raw
    if ([string]::IsNullOrWhiteSpace($content)) { throw 'The selected Firestore rules file is empty.' }
    return Get-FirestoreRulesHash $content
}

function Get-ExpectedFirestoreIndexes {
    param([string]$Checkout)
    $path = Join-Path $Checkout 'firestore.indexes.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'The selected commit has no firestore.indexes.json; API traffic cannot be released.'
    }
    $definitions = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -ErrorAction Stop
    if (@($definitions.indexes | ForEach-Object { $_.fields } | Where-Object { $_ -and $_.vectorConfig }).Count -gt 0) {
        throw 'The release readiness gate does not support vector indexes yet; add a verified readiness check before releasing them.'
    }
    return @($definitions.indexes | Where-Object { $_ })
}

function Get-ExpectedFirestoreFieldOverrides {
    param([string]$Checkout)
    $path = Join-Path $Checkout 'firestore.indexes.json'
    $definitions = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -ErrorAction Stop
    $overrides = @($definitions.fieldOverrides | Where-Object { $_ })
    Assert-SupportedFirestoreFieldOverrides -Definitions $overrides
    return $overrides
}

function Get-CurrentFirestoreFieldOverrides {
    param([object[]]$Expected)
    if ($Expected.Count -eq 0) { return @() }
    $token = [string](& gcloud.cmd auth print-access-token)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw 'Cannot authenticate the Firestore field override readiness check.'
    }
    $headers = @{ Authorization = "Bearer $($token.Trim())"; 'x-goog-user-project' = $project }
    foreach ($definition in $Expected) {
        $group = [System.Uri]::EscapeDataString([string]$definition.collectionGroup)
        $field = [System.Uri]::EscapeDataString([string]$definition.fieldPath)
        $name = "projects/$project/databases/(default)/collectionGroups/$group/fields/$field"
        try { $current = Invoke-RestMethod -Method Get -Uri "https://firestore.googleapis.com/v1/$name" -Headers $headers }
        catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { continue }
            throw
        }
        if ($current.name -cne $name) { throw "Firestore returned an unexpected field configuration for $name." }
        [pscustomobject]@{
            collectionGroup = [string]$definition.collectionGroup
            fieldPath = [string]$definition.fieldPath
            usesAncestorConfig = [bool]$current.indexConfig.usesAncestorConfig
            reverting = [bool]$current.indexConfig.reverting
            indexes = @($current.indexConfig.indexes | Where-Object { $_ })
        }
    }
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

function Wait-FirestoreFieldOverridesReady {
    param([object[]]$Expected, [int]$TimeoutSeconds = 900)
    if ($Expected.Count -eq 0) { return }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $actual = @(Get-CurrentFirestoreFieldOverrides -Expected $Expected)
        $unready = @(Get-UnreadyFirestoreFieldOverrides -Expected $Expected -Actual $actual)
        if ($unready.Count -eq 0) { return }
        $summary = ($unready | ForEach-Object { "$($_.collectionGroup).$($_.fieldPath): $($_.state)" }) -join '; '
        if ([DateTime]::UtcNow -ge $deadline) { throw "Firestore field exemptions are not ready after $TimeoutSeconds seconds: $summary" }
        Write-Host "Waiting for Firestore field exemptions: $summary" -ForegroundColor Yellow
        Start-Sleep -Seconds 15
    } while ($true)
}

function Wait-FirestoreRulesPublished {
    param([string]$ExpectedHash, [int]$TimeoutSeconds = 90)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $current = Get-LiveFirestoreRules
        if ($current.ContentHash -ceq $ExpectedHash) { return $current }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'The committed Firestore rules are not the published rules after deployment.' }
        Start-Sleep -Seconds 3
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
    param($BeforeHosting, $BeforeApi, $BeforeRules)
    if ($Target -in @('all', 'hosting')) {
        $now = Get-LiveHosting
        if ($now.ReleaseName -cne $BeforeHosting.ReleaseName -or
            $now.VersionName -cne $BeforeHosting.VersionName -or
            $now.IndexSha256 -cne $BeforeHosting.IndexSha256 -or
            $now.SourceSha -cne $BeforeHosting.SourceSha) {
            throw 'Hosting changed during release verification. Restart the deployment preflight.'
        }
    }
    $now = Get-LiveApi
    Assert-ServingApiUnchanged -Before $BeforeApi -Now $now
    if ($Target -in @('all', 'api')) {
        Assert-FirestoreRulesUnchanged -Before $BeforeRules -Now (Get-LiveFirestoreRules)
    }
}

function Write-ReleaseRecord {
    param($Record, [string]$Path)
    $json = $Record | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function New-FirestorePublicationContext {
    param([Parameter(Mandatory)]$PublicationPlan, [Parameter(Mandatory)]$LockedSource,
        [Parameter(Mandatory)][string]$RecordPath)

    $indexesPath = $RecordPath + '.firestore.indexes.json'
    $configPath = $RecordPath + '.firestore.firebase.json'
    $files = @(
        [pscustomobject]@{ Path = $indexesPath; Record = [ordered]@{
            indexes = @($PublicationPlan.Indexes); fieldOverrides = @($PublicationPlan.FieldOverrides) } },
        [pscustomobject]@{ Path = $configPath; Record = [ordered]@{
            firestore = [ordered]@{ indexes = [System.IO.Path]::GetFileName($indexesPath) } } }
    )
    $streams = [System.Collections.Generic.List[System.IO.FileStream]]::new()
    $hashes = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($file in $files) {
            $text = $file.Record | ConvertTo-Json -Depth 8
            $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text)
            $writer = [System.IO.File]::Open($file.Path, [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $writer.Write($bytes, 0, $bytes.Length) }
            finally { $writer.Dispose() }
            $reader = [System.IO.File]::Open($file.Path, [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $streams.Add($reader)
            $hash = [System.Security.Cryptography.SHA256]::Create()
            try { $actualHash = ([System.BitConverter]::ToString($hash.ComputeHash($reader))).Replace('-', '').ToLowerInvariant() }
            finally { $hash.Dispose() }
            $reader.Position = 0
            if ($actualHash -cne (Get-ReleaseTextHash $text)) {
                throw 'Generated Firestore publication files changed before their immutable handles were acquired.'
            }
            $hashes.Add($actualHash)
        }
        foreach ($stream in $streams) { $LockedSource.Streams.Add($stream) }
        return [pscustomobject]@{ ConfigPath = $configPath; IndexesPath = $indexesPath;
            IndexesSha256 = $hashes[0]; ConfigSha256 = $hashes[1] }
    }
    catch {
        foreach ($stream in $streams) { $stream.Dispose() }
        # Keep this run's partial artifacts as failure evidence. Never remove a
        # pre-existing collision or an earlier release's files.
        throw
    }
}

if ($AdoptUnstampedLive -and $AllowRollback) {
    throw 'Use either first-release adoption or an explicit rollback, not both.'
}
if ($AllowRollback -and [string]::IsNullOrWhiteSpace($RollbackReason)) {
    throw '-AllowRollback requires -RollbackReason.'
}
if ($InspectLive -and $PreflightOnly) { throw 'Choose either live inspection or local-only preflight.' }

# Refuse dirty or mismatched source before credentials, network, installs or
# build output changes. This path never creates or changes a Git checkout.
$localPreflight = $null
if (-not $InspectLive) {
    $localPreflight = Get-ReleaseLocalPreflight -Repository $repoRoot -Commit $Commit
    if ($PreflightOnly) { $localPreflight; return }
}

$hostingBefore = $null
$apiBefore = $null
$rulesBefore = $null
if ($Target -in @('all', 'hosting')) { $hostingBefore = Get-LiveHosting }
$apiBefore = Get-LiveApi
if ($Target -in @('all', 'api')) {
    $rulesBefore = Get-LiveFirestoreRules
}

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
        Write-Host "Firestore rules release: $($rulesBefore.ReleaseName)"
        Write-Host "Firestore rules SHA-256: $($rulesBefore.ContentHash)"
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

$candidateSha = $localPreflight.CandidateSha
$mainSha = $localPreflight.ToolingSha
Assert-ReleaseCheckout -Repository $repoRoot -Sha $candidateSha
$releaseToolPaths = @(Get-ReleaseToolPaths)
Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $mainSha -Paths $releaseToolPaths
Assert-LiveTargets -CandidateSha $candidateSha -Hosting $hostingBefore -Api $apiBefore
if ($Target -ceq 'hosting') {
    Assert-HostingApiCompatibility -Repository $repoRoot -CandidateSha $candidateSha -LiveApiSha $apiBefore.SourceSha
}
$verificationPlan = Get-ReleaseVerificationPlan -Repository $repoRoot -CandidateSha $candidateSha `
    -HostingBaseSha $(if ($hostingBefore) { $hostingBefore.SourceSha } else { '' }) `
    -Target $Target -Rollback:$AllowRollback -Adoption:$AdoptUnstampedLive -LivePlaytest:$LivePlaytest

# The existing checkout is the only source. Publication requires a clean,
# exact committed candidate held under a release lock and immutable read handles.
$releaseCheckout = $repoRoot
$workspaceLock = $null
$sourceLock = $null
$hostingContext = $null
$recordPath = ''
$record = [ordered]@{
    schemaVersion = 2
    sourceMode = $localPreflight.SourceMode
    sourceTreeSha = $localPreflight.TreeSha
    sourceManifestSha256 = ''
    sourceFileCount = 0
    apiUploadManifestSha256 = ''
    apiUploadIgnoreSha256 = ''
    apiUploadFileCount = 0
    apiBuildId = ''
    apiBuildTag = ''
    apiImageDigest = ''
    apiImageReference = ''
    firestoreIndexPublicationSha256 = ''
    firestoreIndexConfigSha256 = ''
    firestorePublishedIndexCount = 0
    firestorePublishedFieldOverrideCount = 0
    hostingArtifactManifestSha256 = ''
    hostingArtifactFileCount = 0
    hostingConfigSha256 = ''
    target = $Target
    candidateSha = $candidateSha
    toolingSha = $mainSha
    verificationPlan = $verificationPlan
    testSuiteWaiver = [ordered]@{ skipped = [bool]($SkipTestSuites -or $TestFree); reason = $TestWaiverReason }
    broadTestSuitesDeferred = [bool]($SkipTestSuites -or $TestFree)
    preDeploymentTestsSkipped = [bool]$TestFree
    livePlaytest = [bool]$LivePlaytest
    continuousAudit = [bool]$ContinuousAudit
    focusedTestScopes = if ($LivePlaytest) { @(Get-ReleaseFocusedTestScopes) } else { @() }
    focusedTestsStatus = if ($LivePlaytest) { 'required' } else { 'not-applicable' }
    ciStatus = 'not-checked; no CI success claimed'
    startedAtUtc = [DateTime]::UtcNow.ToString('o')
    adoption = [bool]$AdoptUnstampedLive
    rollbackReason = $RollbackReason
    previousHostingRelease = if ($hostingBefore) { $hostingBefore.ReleaseName } else { '' }
    previousHostingVersion = if ($hostingBefore) { $hostingBefore.VersionName } else { '' }
    previousApiRevision = if ($apiBefore) { $apiBefore.RevisionName } else { '' }
    previousFirestoreRulesRelease = if ($rulesBefore) { $rulesBefore.ReleaseName } else { '' }
    newHostingRelease = ''
    newApiRevision = ''
    newFirestoreRulesRelease = ''
    status = 'preflight'
}

try {
    $workspaceLock = Enter-ReleaseWorkspaceLock -Repository $releaseCheckout
    $sourceLock = Enter-ReleaseSourceLock -Repository $releaseCheckout -Sha $candidateSha
    Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $candidateSha -Paths $releaseToolPaths
    $record.sourceManifestSha256 = $sourceLock.ManifestSha256
    $record.sourceFileCount = $sourceLock.FileCount

    $recordDirectory = Join-Path $repoRoot 'artifacts\releases'
    [System.IO.Directory]::CreateDirectory($recordDirectory) | Out-Null
    $recordPath = Join-Path $recordDirectory (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')) + '-' + $candidateSha.Substring(0, 12) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    Write-ReleaseRecord -Record $record -Path $recordPath

    Write-Host "`nVerifying immutable main commit $candidateSha..." -ForegroundColor Yellow
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('ci') -WorkingDirectory $releaseCheckout
    Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
    # The exact candidate's committed verifier independently recalculates scope.
    $verificationArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $repoRoot 'scripts/verify-release.ps1'), '-Repository', $releaseCheckout,
        '-ExpectedSha', $candidateSha, '-ExpectedToolingSha', $mainSha,
        '-Target', $Target, '-ExpectedMode', $verificationPlan.mode)
    if ($verificationPlan.baseSha) { $verificationArguments += @('-HostingBaseSha', $verificationPlan.baseSha) }
    if ($verificationPlan.workspace) { $verificationArguments += @('-ExpectedWorkspace', $verificationPlan.workspace) }
    if ($AllowRollback) { $verificationArguments += '-Rollback' }
    if ($AdoptUnstampedLive) { $verificationArguments += '-Adoption' }
    if ($LivePlaytest) { $verificationArguments += '-LivePlaytest' }
    if ($TestFree) {
        Write-Warning "All pre-deployment automated tests are NOT RUN. Waiver: $TestWaiverReason"
        $verificationArguments += @('-TestFree', '-TestWaiverReason', $TestWaiverReason)
    }
    elseif ($SkipTestSuites) {
        Write-Warning "Broad application test suites deferred. Fixed focused tests remain mandatory for live playtests. Waiver: $TestWaiverReason"
        $verificationArguments += @('-SkipTestSuites', '-TestWaiverReason', $TestWaiverReason)
    }
    Invoke-Checked -Executable 'powershell.exe' -Arguments $verificationArguments -WorkingDirectory $releaseCheckout
    if ($LivePlaytest) { $record.focusedTestsStatus = 'passed' }
    if ($TestFree) {
        Write-Warning 'Deployment guardrail tests are NOT RUN on the explicit test-free route.'
    }
    else {
        # Guard tests use synthetic command responses, never disposable repositories.
        Invoke-Checked -Executable 'powershell.exe' -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $repoRoot 'scripts/tests/deploy-guardrails.tests.ps1')
        ) -WorkingDirectory $repoRoot
    }
    $sourceChanges = & git -C $releaseCheckout status --porcelain=v1 --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $sourceChanges) {
        throw 'Verification changed tracked or untracked source in the release checkout.'
    }
    Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
    $record.status = 'verified'
    Write-ReleaseRecord -Record $record -Path $recordPath

    $mainAfterVerification = (& git -C $repoRoot rev-parse --verify refs/heads/main).Trim()
    if ($LASTEXITCODE -ne 0 -or $mainAfterVerification -cne $mainSha) {
        throw 'Local main moved during release verification. Restart against the new committed history.'
    }
    Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $mainSha -Paths $releaseToolPaths
    Assert-UnchangedLiveTargets -BeforeHosting $hostingBefore -BeforeApi $apiBefore -BeforeRules $rulesBefore

    if ($Target -in @('all', 'hosting')) {
        $hostingContext = New-HostingReleaseContext -Checkout $releaseCheckout -CandidateSha $candidateSha -RecordPath $recordPath
        $record.hostingArtifactManifestSha256 = $hostingContext.ManifestSha256
        $record.hostingArtifactFileCount = $hostingContext.FileCount
        $record.hostingConfigSha256 = $hostingContext.ConfigSha256
        Write-ReleaseRecord -Record $record -Path $recordPath
    }

    if ($Target -in @('all', 'api')) {
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        $expectedIndexes = @(Get-ExpectedFirestoreIndexes -Checkout $releaseCheckout)
        Assert-NonRedundantFirestoreCompositeIndexes -Definitions $expectedIndexes
        $expectedFieldOverrides = @(Get-ExpectedFirestoreFieldOverrides -Checkout $releaseCheckout)
        $expectedRulesHash = if ($AllowRollback) { '' }
            else { Get-ExpectedFirestoreRulesHash -Checkout $releaseCheckout }
        $currentIndexes = @(Get-CurrentFirestoreIndexes -Expected $expectedIndexes)
        $currentFieldOverrides = @(Get-CurrentFirestoreFieldOverrides -Expected $expectedFieldOverrides)
        $publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes $expectedIndexes `
            -ActualIndexes $currentIndexes -ExpectedFieldOverrides $expectedFieldOverrides `
            -ActualFieldOverrides $currentFieldOverrides
        if (-not $AllowRollback -and ($publicationPlan.Indexes.Count -gt 0 -or $publicationPlan.FieldOverrides.Count -gt 0)) {
            # The pinned CLI skips absent live definitions in noninteractive
            # mode without --force. Existing identities, including CREATING
            # indexes, must not be re-posted by its __name__-dropping matcher.
            $indexContext = New-FirestorePublicationContext -PublicationPlan $publicationPlan -LockedSource $sourceLock -RecordPath $recordPath
            $record.firestoreIndexPublicationSha256 = $indexContext.IndexesSha256
            $record.firestoreIndexConfigSha256 = $indexContext.ConfigSha256
            $record.firestorePublishedIndexCount = $publicationPlan.Indexes.Count
            $record.firestorePublishedFieldOverrideCount = $publicationPlan.FieldOverrides.Count
            Write-ReleaseRecord -Record $record -Path $recordPath
            Write-Host "`nDeploying $($publicationPlan.Indexes.Count) missing Firestore indexes and $($publicationPlan.FieldOverrides.Count) unready field exemptions before the API..." -ForegroundColor Yellow
            Invoke-Checked -Executable 'npx.cmd' -Arguments @(
                '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'firestore:indexes',
                '--config', $indexContext.ConfigPath,
                '--project', $project, '--account', $firebaseAccount, '--non-interactive'
            ) -WorkingDirectory $releaseCheckout
        }
        elseif ($AllowRollback) {
            Write-Warning 'Keeping existing Firestore indexes during source rollback; database indexes are not rolled back automatically.'
        }
        else {
            Write-Host 'Required Firestore index definitions already exist; preserving them and checking readiness.' -ForegroundColor Yellow
        }

        # Firebase accepting an index definition does not mean its backfill has
        # completed. Field exemptions must also be visible before API traffic.
        Wait-FirestoreIndexesReady -Expected $expectedIndexes
        Wait-FirestoreFieldOverridesReady -Expected $expectedFieldOverrides

        Assert-FirestoreRulesUnchanged -Before $rulesBefore -Now (Get-LiveFirestoreRules)
        if (-not $AllowRollback -and $rulesBefore.ContentHash -cne $expectedRulesHash) {
            Write-Host "`nDeploying committed Firestore rules before the API..." -ForegroundColor Yellow
            Invoke-Checked -Executable 'npx.cmd' -Arguments @(
                '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'firestore:rules',
                '--project', $project, '--account', $firebaseAccount, '--non-interactive'
            ) -WorkingDirectory $releaseCheckout
        }
        elseif ($AllowRollback) {
            Write-Warning 'Keeping existing Firestore rules during source rollback; database rules are not rolled back automatically.'
        }
        $rulesAfterPublication = if ($AllowRollback) { Get-LiveFirestoreRules }
            else { Wait-FirestoreRulesPublished -ExpectedHash $expectedRulesHash }
        $record.newFirestoreRulesRelease = $rulesAfterPublication.ReleaseName
        Write-ReleaseRecord -Record $record -Path $recordPath

        $apiImmediatelyBefore = Get-LiveApi
        Assert-ServingApiUnchanged -Before $apiBefore -Now $apiImmediatelyBefore
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha

        Assert-ApiBuildRepository
        $uploadContext = New-ApiUploadContext -Checkout $releaseCheckout -LockedSource $sourceLock -RecordPath $recordPath
        $imageTag = "$region-docker.pkg.dev/$project/$ArtifactRepository/${serviceName}:ff-$($candidateSha.Substring(0, 12))-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        $record.apiUploadManifestSha256 = $uploadContext.ManifestSha256
        $record.apiUploadIgnoreSha256 = $uploadContext.IgnoreSha256
        $record.apiUploadFileCount = $uploadContext.FileCount
        $record.apiBuildTag = $imageTag
        Write-ReleaseRecord -Record $record -Path $recordPath
        Write-Host "`nBuilding the locked committed API source with an exact upload allowlist..." -ForegroundColor Yellow
        # run deploy --source in the installed SDK cannot override its ignore
        # file. builds submit supports this explicit, locked allowlist instead.
        $build = Invoke-JsonCommand 'gcloud.cmd' @('builds', 'submit', $releaseCheckout,
            '--ignore-file', $uploadContext.IgnoreFile, '--tag', $imageTag,
            '--project', $project, '--format=json', '--suppress-logs', '--quiet')
        $builtImage = Get-ReleaseBuiltImage -Build $build -ExpectedTag $imageTag
        $record.apiBuildId = $builtImage.BuildId
        $record.apiImageDigest = $builtImage.Digest
        $record.apiImageReference = $builtImage.Image
        Write-ReleaseRecord -Record $record -Path $recordPath
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        Assert-ServingApiUnchanged -Before $apiBefore -Now (Get-LiveApi)
        Write-Host "`nCreating a non-serving Cloud Run revision from the verified image digest..." -ForegroundColor Yellow
        $apiDeployArguments = @(
            'run', 'deploy', $serviceName,
            '--image', $builtImage.Image,
            '--region', $region, '--project', $project,
            '--max-instances', '1', '--no-traffic',
            "--update-env-vars=FF_RELEASE_SHA=$candidateSha",
            "--update-labels=ff-source-sha=$candidateSha",
            '--quiet'
        )
        if ($ContinuousAudit) { $apiDeployArguments += @('--min-instances', '1', '--no-cpu-throttling') }
        Invoke-Checked -Executable 'gcloud.cmd' -Arguments $apiDeployArguments -WorkingDirectory $releaseCheckout

        $service = Get-CloudRunService
        $newApiRevision = [string]$service.status.latestCreatedRevisionName
        if ([string]::IsNullOrWhiteSpace($newApiRevision) -or $newApiRevision -ceq $apiBefore.RevisionName) {
            throw 'Cloud Run did not expose a distinct new revision; traffic was not changed.'
        }
        $newRevision = Get-ApiRevision $newApiRevision
        if ((Get-ApiRevisionSourceSha $newRevision) -cne $candidateSha) {
            throw 'New Cloud Run revision lacks the expected source stamp; traffic was not changed.'
        }
        Assert-ApiRevisionImage -Revision $newRevision -ExpectedImage $builtImage.Image
        $record.newApiRevision = $newApiRevision
        Write-ReleaseRecord -Record $record -Path $recordPath

        # A Cloud Run source build may take minutes. Verify database prerequisites
        # and the serving revision again immediately before moving traffic.
        Wait-FirestoreIndexesReady -Expected $expectedIndexes
        Wait-FirestoreFieldOverridesReady -Expected $expectedFieldOverrides
        $rulesAtTrafficSwitch = Get-LiveFirestoreRules
        Assert-FirestoreRulesUnchanged -Before $rulesAfterPublication -Now $rulesAtTrafficSwitch
        if (-not $AllowRollback -and $rulesAtTrafficSwitch.ContentHash -cne $expectedRulesHash) {
            throw 'Firestore rules no longer match the committed candidate before API traffic switch.'
        }
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
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        $apiForHosting = Get-LiveApi
        $expectedApiForHosting = if ($Target -ceq 'all') { $apiAfter } else { $apiBefore }
        Assert-ServingApiUnchanged -Before $expectedApiForHosting -Now $apiForHosting
        Assert-HostingApiCompatibility -Repository $repoRoot -CandidateSha $candidateSha `
            -LiveApiSha $apiForHosting.SourceSha -RequireExact:($Target -ceq 'all')
        $hostingImmediatelyBefore = Get-LiveHosting
        if ($hostingImmediatelyBefore.ReleaseName -cne $hostingBefore.ReleaseName -or
            $hostingImmediatelyBefore.VersionName -cne $hostingBefore.VersionName -or
            $hostingImmediatelyBefore.IndexSha256 -cne $hostingBefore.IndexSha256) {
            throw 'Hosting changed before publication. Restart the deployment.'
        }
        if (-not $hostingContext) { throw 'No locked verified Hosting artifact context exists.' }
        Write-Host "`nPublishing stamped Firebase Hosting build..." -ForegroundColor Yellow
        Invoke-Checked -Executable 'npx.cmd' -Arguments @(
            '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'hosting',
            '--config', $hostingContext.ConfigPath,
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
    if ($hostingContext) { Exit-ReleaseSourceLock -Lock $hostingContext.Lock }
    Exit-ReleaseSourceLock -Lock $sourceLock
    if ($workspaceLock) { $workspaceLock.Dispose() }
}
