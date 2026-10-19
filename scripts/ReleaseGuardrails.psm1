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

Export-ModuleMember -Function Assert-ReleaseSha, Assert-CommitOnMain, Assert-ReleaseToolingMatchesCommit, Assert-LiveReleaseSource, Assert-HostingAdoptionRelease, Get-ReleaseTextHash, Get-HostingSourceSha, Get-ServingApiRevision, Get-ApiRevisionSourceSha, Assert-ServingApiUnchanged, Test-FirestoreIndexDefinitionMatches, Test-FirestoreIndexBelongsToGroup, Get-UnreadyFirestoreIndexes
