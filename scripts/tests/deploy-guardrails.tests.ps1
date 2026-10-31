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
Assert-Equal (Get-FirestoreRulesHash "rules_version = '2';`r`n") (Get-FirestoreRulesHash "rules_version = '2';`n") 'Firestore rules line endings'
$beforeRules = [pscustomobject]@{ ReleaseName = ''; RulesetName = ''; ContentHash = '' }
Assert-FirestoreRulesUnchanged -Before $beforeRules -Now ([pscustomobject]@{ ReleaseName = ''; RulesetName = ''; ContentHash = '' })
Assert-Throws {
    Assert-FirestoreRulesUnchanged -Before $beforeRules -Now ([pscustomobject]@{
        ReleaseName = 'projects/example/releases/cloud.firestore'; RulesetName = 'projects/example/rulesets/new'; ContentHash = 'new'
    })
} 'concurrent Firestore rules publication'
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
$expectedField = [pscustomobject]@{ collectionGroup = 'gameRoundEvidence'; fieldPath = 'payloadJson'; indexes = @() }
Assert-SupportedFirestoreFieldOverrides -Definitions @($expectedField)
Assert-Throws {
    Assert-SupportedFirestoreFieldOverrides -Definitions @([pscustomobject]@{
        collectionGroup = 'gameRoundEvidence'; fieldPath = 'payloadJson'; indexes = @([pscustomobject]@{ order = 'ASCENDING' })
    })
} 'unsupported indexed field override'
Assert-Throws {
    Assert-SupportedFirestoreFieldOverrides -Definitions @([pscustomobject]@{
        collectionGroup = 'gameRoundEvidence'; fieldPath = 'payloadJson'
    })
} 'missing field override indexes'
Assert-Throws {
    Assert-SupportedFirestoreFieldOverrides -Definitions @([pscustomobject]@{
        collectionGroup = 'gameRoundEvidence'; fieldPath = 'payloadJson'; indexes = @(); ttl = $true
    })
} 'unverified field override setting'
Assert-Throws {
    Assert-SupportedFirestoreFieldOverrides -Definitions @($expectedField, $expectedField)
} 'duplicate field override'
$actualField = [pscustomobject]@{ collectionGroup = 'gameRoundEvidence'; fieldPath = 'payloadJson';
    usesAncestorConfig = $false; reverting = $false; indexes = @() }
Assert-Equal @(Get-UnreadyFirestoreFieldOverrides -Expected @($expectedField) -Actual @($actualField)).Count 0 'ready field exemption'
$actualField.usesAncestorConfig = $true
Assert-Equal @(Get-UnreadyFirestoreFieldOverrides -Expected @($expectedField) -Actual @($actualField)).Count 1 'inherited field indexes are not exempt'
$actualField.usesAncestorConfig = $false
$actualField.reverting = $true
Assert-Equal @(Get-UnreadyFirestoreFieldOverrides -Expected @($expectedField) -Actual @($actualField)).Count 1 'reverting field exemption is not ready'
$actualField.reverting = $false
$actualField.indexes = @([pscustomobject]@{ state = 'READY' })
Assert-Equal @(Get-UnreadyFirestoreFieldOverrides -Expected @($expectedField) -Actual @($actualField)).Count 1 'explicit field index is not exempt'
Assert-Equal @(Get-UnreadyFirestoreFieldOverrides -Expected @($expectedField) -Actual @()).Count 1 'missing field exemption'
$creatingIndex.fields[1].order = 'DESCENDING'
$explicitNameIndex = [pscustomobject]@{
    collectionGroup = 'solitaireMatchPlayers'; queryScope = 'COLLECTION'
    fields = @($expectedIndex.fields + $creatingIndex.fields[-1])
}
$actualField.indexes = @()
$actualField.usesAncestorConfig = $true
$missingComposite = [pscustomobject]@{
    collectionGroup = 'gameRoundAudits'; queryScope = 'COLLECTION'; fields = @(
        [pscustomobject]@{ fieldPath = 'reviewPending'; order = 'ASCENDING' },
        [pscustomobject]@{ fieldPath = 'completedAtUtc'; order = 'ASCENDING' }
    )
}
$publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes @($explicitNameIndex, $missingComposite) `
    -ActualIndexes @($creatingIndex) -ExpectedFieldOverrides @($expectedField) -ActualFieldOverrides @($actualField)
Assert-Equal $publicationPlan.Indexes.Count 1 'only missing composite index is published'
Assert-Equal $publicationPlan.Indexes[0].collectionGroup 'gameRoundAudits' 'existing composite index is excluded'
Assert-Equal $publicationPlan.FieldOverrides.Count 1 'only unready field exemption is published'
$creatingIndex.state = 'CREATING'
$publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes @($explicitNameIndex) -ActualIndexes @($creatingIndex) `
    -ExpectedFieldOverrides @() -ActualFieldOverrides @()
Assert-Equal $publicationPlan.Indexes.Count 0 'creating composite index is not recreated'
$actualField.usesAncestorConfig = $false
$publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes @() -ActualIndexes @() `
    -ExpectedFieldOverrides @($expectedField) -ActualFieldOverrides @($actualField)
Assert-Equal $publicationPlan.FieldOverrides.Count 0 'ready field exemption is not patched again'
Assert-Throws {
    New-FirestoreIndexPublicationPlan -ExpectedIndexes @($expectedIndex, $explicitNameIndex) -ActualIndexes @() `
        -ExpectedFieldOverrides @() -ActualFieldOverrides @()
} 'duplicate committed composite index'

# Keep the traffic preconditions adjacent to the actual traffic command. These
# checks supplement the policy tests above; they do not call a live service.
$deploySource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/deploy.ps1') -Raw
$firebaseAccountFlag = "'--account', " + '$firebaseAccount'
Assert-Equal ($deploySource.Split(@($firebaseAccountFlag), [System.StringSplitOptions]::None).Count - 1) 3 'explicit account for all Firebase publications'
$indexPublication = $deploySource.IndexOf("'--only', 'firestore:indexes'")
$indexConfig = $deploySource.IndexOf("'--config', " + '$temporaryIndexConfig.ConfigPath', $indexPublication)
$indexNonInteractive = $deploySource.IndexOf("'--non-interactive'", $indexConfig)
$rulesPublication = $deploySource.IndexOf("'--only', 'firestore:rules'")
if ($indexPublication -lt 0 -or $indexConfig -le $indexPublication -or
    $indexNonInteractive -le $indexConfig -or $indexNonInteractive -ge $rulesPublication -or
    $deploySource.Substring($indexPublication, $rulesPublication - $indexPublication).Contains("'--force'")) {
    throw 'Filtered Firestore index publication must use a temporary config, noninteractive mode and no force deletion.'
}
$trafficCommand = $deploySource.LastIndexOf("'run', 'services', 'update-traffic'")
if ($trafficCommand -lt 0) { throw 'API traffic command is missing from the deploy script.' }
$readinessCheck = $deploySource.LastIndexOf('Wait-FirestoreIndexesReady -Expected $expectedIndexes', $trafficCommand)
$fieldReadinessCheck = $deploySource.LastIndexOf('Wait-FirestoreFieldOverridesReady -Expected $expectedFieldOverrides', $trafficCommand)
$rulesRead = $deploySource.LastIndexOf('$rulesAtTrafficSwitch = Get-LiveFirestoreRules', $trafficCommand)
$rulesGuard = $deploySource.LastIndexOf('Assert-FirestoreRulesUnchanged -Before $rulesAfterPublication -Now $rulesAtTrafficSwitch', $trafficCommand)
$revisionRead = $deploySource.LastIndexOf('$apiAtTrafficSwitch = Get-LiveApi', $trafficCommand)
$revisionGuard = $deploySource.LastIndexOf('Assert-ServingApiUnchanged -Before $apiBefore -Now $apiAtTrafficSwitch', $trafficCommand)
if ($readinessCheck -lt 0 -or $fieldReadinessCheck -le $readinessCheck -or
    $rulesPublication -lt 0 -or $rulesPublication -ge $fieldReadinessCheck -or
    $rulesRead -le $fieldReadinessCheck -or $rulesGuard -le $rulesRead -or
    $revisionRead -le $rulesGuard -or
    $revisionGuard -le $revisionRead -or $revisionGuard -gt $trafficCommand) {
    throw 'API traffic command is missing its immediately preceding index, field, rules and live-revision checks.'
}

& (Join-Path $PSScriptRoot 'release-scope.tests.ps1')
Write-Host 'Release guardrail tests passed.' -ForegroundColor Green
