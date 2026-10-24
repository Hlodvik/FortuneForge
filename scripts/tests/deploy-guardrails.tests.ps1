$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repoRoot 'scripts/ReleaseGuardrails.psm1') -Force

function Assert-Equal {
    param($Actual, $Expected, [string]$Name)
    if ($Actual -cne $Expected) { throw "$Name expected '$Expected', got '$Actual'." }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Name)
    $threw = $false
    try { & $Action | Out-Null }
    catch { $threw = $true }
    if (-not $threw) { throw "$Name did not reject the unsafe release." }
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
$parent = (& git -C $repoRoot rev-parse 'HEAD^').Trim()
Assert-CommitOnMain -Repository $repoRoot -Sha $head
Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $head -Paths @('scripts/verify-release.ps1')
Assert-Throws {
    Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $head -Paths @('scripts/does-not-exist.ps1')
} 'uncommitted release tooling'
Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $head -Target 'Hosting' -LiveSha $head
Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $head -Target 'Hosting' -LiveSha $parent
Assert-Throws { Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $parent -Target 'Hosting' -LiveSha $head } 'older candidate'
Assert-Throws { Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $head -Target 'Hosting' } 'unstamped live release'
Assert-Throws {
    Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $head -Target 'Hosting' `
        -AdoptUnstampedLive -ObservedFingerprint 'observed' -ExpectedFingerprint 'wrong'
} 'wrong adoption fingerprint'
Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $head -Target 'Hosting' `
    -AdoptUnstampedLive -ObservedFingerprint 'observed' -ExpectedFingerprint 'observed'
Assert-Throws {
    Assert-HostingAdoptionRelease -LiveSha '' -AdoptUnstampedLive `
        -ObservedRelease 'sites/example/releases/current' -ExpectedRelease 'sites/example/releases/previous'
} 'changed unstamped Hosting release'
Assert-HostingAdoptionRelease -LiveSha '' -AdoptUnstampedLive `
    -ObservedRelease 'sites/example/releases/current' -ExpectedRelease 'sites/example/releases/current'
Assert-Throws {
    Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $parent -Target 'Hosting' `
        -LiveSha $head -AllowRollback
} 'rollback without reason'
Assert-LiveReleaseSource -Repository $repoRoot -CandidateSha $parent -Target 'Hosting' `
    -LiveSha $head -AllowRollback -RollbackReason 'Intentional verified rollback'

Assert-Equal (Get-ReleaseTextHash 'abc') 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad' 'SHA-256'
$oldSpa = '<html>old SPA fallback</html>'
Assert-Equal (Get-HostingSourceSha -ResponseBody $oldSpa -IndexBody $oldSpa) '' 'unstamped Hosting fallback'
Assert-Equal (Get-HostingSourceSha -ResponseBody '' -MarkerAbsent) '' 'missing Hosting marker'
Assert-Throws { Get-HostingSourceSha -ResponseBody '<html>unexpected error</html>' -IndexBody $oldSpa } 'unexpected Hosting HTML'
Assert-Equal (Get-HostingSourceSha "{`"schemaVersion`":1,`"target`":`"hosting`",`"sourceSha`":`"$head`"}") $head 'Hosting stamp'
Assert-Throws { Get-HostingSourceSha '{broken' } 'malformed Hosting stamp'

$service = [pscustomobject]@{ status = [pscustomobject]@{ traffic = @([pscustomobject]@{ percent = 100; revisionName = 'api-001' }) } }
Assert-Equal (Get-ServingApiRevision $service) 'api-001' 'serving API revision'
$splitService = [pscustomobject]@{ status = [pscustomobject]@{ traffic = @(
    [pscustomobject]@{ percent = 50; revisionName = 'api-001' },
    [pscustomobject]@{ percent = 50; revisionName = 'api-002' }
) } }
Assert-Throws { Get-ServingApiRevision $splitService } 'split API traffic'
$revision = [pscustomobject]@{ spec = [pscustomobject]@{ containers = @(
    [pscustomobject]@{ env = @([pscustomobject]@{ name = 'FF_RELEASE_SHA'; value = $head }) }
) } }
Assert-Equal (Get-ApiRevisionSourceSha $revision) $head 'API stamp'
$unstampedRevision = [pscustomobject]@{ spec = [pscustomobject]@{ containers = @([pscustomobject]@{ env = @() }) } }
Assert-Equal (Get-ApiRevisionSourceSha $unstampedRevision) '' 'unstamped API revision'
$beforeApi = [pscustomobject]@{ RevisionName = 'api-001'; SourceSha = $head }
Assert-ServingApiUnchanged -Before $beforeApi -Now ([pscustomobject]@{ RevisionName = 'api-001'; SourceSha = $head })
Assert-Throws {
    Assert-ServingApiUnchanged -Before $beforeApi -Now ([pscustomobject]@{ RevisionName = 'api-002'; SourceSha = $head })
} 'concurrent API deployment'

$expectedIndex = [pscustomobject]@{
    collectionGroup = 'solitaireMatchPlayers'
    queryScope = 'COLLECTION'
    fields = @(
        [pscustomobject]@{ fieldPath = 'userId'; order = 'ASCENDING' },
        [pscustomobject]@{ fieldPath = 'startedAt'; order = 'DESCENDING' }
    )
}
$creatingIndex = [pscustomobject]@{
    collectionGroup = 'solitaireMatchPlayers'
    queryScope = 'COLLECTION'
    fields = @(
        [pscustomobject]@{ fieldPath = 'userId'; order = 'ASCENDING' },
        [pscustomobject]@{ fieldPath = 'startedAt'; order = 'DESCENDING' },
        [pscustomobject]@{ fieldPath = '__name__'; order = 'DESCENDING' }
    )
    state = 'CREATING'
}
Assert-Equal (Test-FirestoreIndexDefinitionMatches -Expected $expectedIndex -Actual $creatingIndex) $true 'implicit name field'
Assert-Equal @(Get-UnreadyFirestoreIndexes -Expected @($expectedIndex) -Actual @($creatingIndex)).Count 1 'CREATING index is not ready'
$creatingIndex.state = 'READY'
Assert-Equal @(Get-UnreadyFirestoreIndexes -Expected @($expectedIndex) -Actual @($creatingIndex)).Count 0 'READY index can serve'
$creatingIndex.fields[1].order = 'ASCENDING'
Assert-Equal @(Get-UnreadyFirestoreIndexes -Expected @($expectedIndex) -Actual @($creatingIndex)).Count 1 'wrong index order is not ready'
$solitaireIndexName = 'projects/fortuneforgegame/databases/(default)/collectionGroups/solitaireMatchPlayers/indexes/CICAgOjXh4EK'
$otherIndexName = 'projects/fortuneforgegame/databases/(default)/collectionGroups/arcadeCompetitions/indexes/other'
Assert-Equal (Test-FirestoreIndexBelongsToGroup -Name $solitaireIndexName -Project 'fortuneforgegame' -Group 'solitaireMatchPlayers') $true 'correct Firestore group'
Assert-Equal (Test-FirestoreIndexBelongsToGroup -Name $otherIndexName -Project 'fortuneforgegame' -Group 'solitaireMatchPlayers') $false 'other Firestore group excluded'

# Keep the traffic preconditions adjacent to the actual traffic command. These
# checks supplement the policy tests above; they do not call a live service.
$deploySource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/deploy.ps1') -Raw
$firebaseAccountFlag = "'--account', " + '$firebaseAccount'
Assert-Equal ($deploySource.Split(@($firebaseAccountFlag), [System.StringSplitOptions]::None).Count - 1) 2 'explicit account for both Firebase publications'
$trafficCommand = $deploySource.LastIndexOf("'run', 'services', 'update-traffic'")
if ($trafficCommand -lt 0) { throw 'API traffic command is missing from the deploy script.' }
$readinessCheck = $deploySource.LastIndexOf('Wait-FirestoreIndexesReady -Expected $expectedIndexes', $trafficCommand)
$revisionRead = $deploySource.LastIndexOf('$apiAtTrafficSwitch = Get-LiveApi', $trafficCommand)
$revisionGuard = $deploySource.LastIndexOf('Assert-ServingApiUnchanged -Before $apiBefore -Now $apiAtTrafficSwitch', $trafficCommand)
if ($readinessCheck -lt 0 -or $revisionRead -le $readinessCheck -or
    $revisionGuard -le $revisionRead -or $revisionGuard -gt $trafficCommand) {
    throw 'API traffic command is missing its immediately preceding index and live-revision checks.'
}

& (Join-Path $PSScriptRoot 'release-scope.tests.ps1')
Write-Host 'Release guardrail tests passed.' -ForegroundColor Green
