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

# Exercise the actual read-only native boundary with the encoding that caused
# source locking to fail on Windows PowerShell. No temporary repository needed.
$nativeBoundary = & (Get-Module ReleaseGuardrails) {
    param($Repository)
    $OutputEncoding = [System.Text.UTF8Encoding]::new($true)
    $consoleEncoding = [Console]::InputEncoding
    try {
        [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($true)
        $expected = Invoke-ReleaseGit -Repository $Repository -Arguments @('hash-object', '.dockerignore', 'scripts/ReleaseGuardrails.psm1')
        $actual = Invoke-ReleaseGit -Repository $Repository -Arguments @('hash-object', '--stdin-paths') `
            -InputLines @('".dockerignore"', '"scripts/ReleaseGuardrails.psm1"')
        [pscustomobject]@{ Expected = $expected; Actual = $actual; CallerPreambleLength = $OutputEncoding.GetPreamble().Length;
            ConsolePreambleLength = [Console]::InputEncoding.GetPreamble().Length }
    }
    finally { [Console]::InputEncoding = $consoleEncoding }
} $repoRoot
Assert-Equal $nativeBoundary.Actual.ExitCode 0 'BOM-free Git stdin succeeds'
Assert-Equal ($nativeBoundary.Actual.Output -join ',') ($nativeBoundary.Expected.Output -join ',') 'native stdin preserves both source filenames'
Assert-Equal $nativeBoundary.CallerPreambleLength 3 'native helper leaves caller encoding unchanged'
Assert-Equal $nativeBoundary.ConsolePreambleLength 3 'native helper restores console encoding'

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
$parent = (& git -C $repoRoot rev-parse 'HEAD^').Trim()
Assert-CommitOnMain -Repository $repoRoot -Sha $head
# Exact tooling success/mismatch is exercised with synthetic Git object
# responses below, so tests also run while this reviewed tooling is uncommitted.
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
$redundantCases = @(
    @('securityAuditInvestigations', 'createdAtUtc', 'DESCENDING'),
    @('securityAuditInvestigationInstances', 'caseId', 'ASCENDING'),
    @('securityAuditPairAnalysisQueue', 'status', 'ASCENDING'),
    @('balanceTransactions', 'roundId', 'ASCENDING'),
    @('balanceTransactions', 'idempotencyKey', 'ASCENDING'),
    @('hiLoEvents', 'roundId', 'ASCENDING'),
    @('houseHoldemRoundEvents', 'roundId', 'ASCENDING')
)
$redundantIndexes = @($redundantCases | ForEach-Object {
    [pscustomobject]@{
        collectionGroup = $_[0]; queryScope = 'COLLECTION'; fields = @(
            [pscustomobject]@{ fieldPath = $_[1]; order = $_[2] },
            [pscustomobject]@{ fieldPath = '__name__'; order = $_[2] }
        )
    }
})
try {
    Assert-NonRedundantFirestoreCompositeIndexes -Definitions $redundantIndexes
    throw 'Redundant Firestore composite indexes were not rejected.'
}
catch {
    foreach ($case in $redundantCases) {
        if ($_.Exception.Message -cnotlike "*$($case[0]).$($case[1])*") {
            throw "The redundant-index validation did not identify $($case[0]).$($case[1])."
        }
    }
}
Assert-Throws {
    Assert-NonRedundantFirestoreCompositeIndexes -Definitions @([pscustomobject]@{
        collectionGroup = 'oneField'; queryScope = 'COLLECTION'; fields = @(
            [pscustomobject]@{ fieldPath = 'createdAtUtc'; order = 'DESCENDING' }
        )
    })
} 'single ordered field cannot be a composite index'
Assert-NonRedundantFirestoreCompositeIndexes -Definitions @(
    [pscustomobject]@{ collectionGroup = 'oppositeNameOrder'; queryScope = 'COLLECTION'; fields = @(
        [pscustomobject]@{ fieldPath = 'createdAtUtc'; order = 'DESCENDING' },
        [pscustomobject]@{ fieldPath = '__name__'; order = 'ASCENDING' }
    ) },
    [pscustomobject]@{ collectionGroup = 'twoRegularFields'; queryScope = 'COLLECTION'; fields = @(
        [pscustomobject]@{ fieldPath = 'status'; order = 'ASCENDING' },
        [pscustomobject]@{ fieldPath = 'createdAtUtc'; order = 'DESCENDING' }
    ) },
    [pscustomobject]@{ collectionGroup = 'arrayField'; queryScope = 'COLLECTION'; fields = @(
        [pscustomobject]@{ fieldPath = 'players'; arrayConfig = 'CONTAINS' },
        [pscustomobject]@{ fieldPath = '__name__'; order = 'ASCENDING' }
    ) }
)
$committedFirestore = Get-Content -LiteralPath (Join-Path $repoRoot 'firestore.indexes.json') -Raw | ConvertFrom-Json
Assert-NonRedundantFirestoreCompositeIndexes -Definitions @($committedFirestore.indexes)
foreach ($case in $redundantCases) {
    $disabledQueryField = @($committedFirestore.fieldOverrides | Where-Object {
        $_.collectionGroup -ceq $case[0] -and $_.fieldPath -cin @('*', $case[1])
    })
    Assert-Equal $disabledQueryField.Count 0 "default single-field indexing retained for $($case[0]).$($case[1])"
}
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
$creatingIndex.state = 'NEEDS_REPAIR'
$publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes @($explicitNameIndex) -ActualIndexes @($creatingIndex) `
    -ExpectedFieldOverrides @() -ActualFieldOverrides @()
Assert-Equal $publicationPlan.Indexes.Count 0 'failed index identity is not posted again'
Assert-Equal @(Get-UnreadyFirestoreIndexes -Expected @($explicitNameIndex) -Actual @($creatingIndex)).Count 1 'failed index still blocks readiness'
$unrelatedLiveIndex = [pscustomobject]@{ collectionGroup = 'liveOnly'; queryScope = 'COLLECTION'; fields = @(
    [pscustomobject]@{ fieldPath = 'existing'; order = 'ASCENDING' },
    [pscustomobject]@{ fieldPath = 'other'; order = 'ASCENDING' }
); state = 'READY' }
$publicationPlan = New-FirestoreIndexPublicationPlan -ExpectedIndexes @($explicitNameIndex, $missingComposite) `
    -ActualIndexes @($creatingIndex, $unrelatedLiveIndex) -ExpectedFieldOverrides @() -ActualFieldOverrides @()
Assert-Equal $publicationPlan.Indexes.Count 1 'unrelated existing indexes do not enter publication'
Assert-Equal $publicationPlan.Indexes[0].collectionGroup 'gameRoundAudits' 'publication contains only the missing committed definition'
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
$verifySource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/verify-release.ps1') -Raw
# Import only pure validation/flag functions and input locks, never execute the
# deployment entry point. Fixtures stay in ignored artifacts, not repositories.
$runtimeFunctions = @('ConvertFrom-HostRuntimeConfiguration', 'Get-DeploymentInputHash', 'Read-HostRuntimeConfiguration',
    'Assert-HostRuntimeConfigurationUnchanged', 'Enter-HostRuntimeConfigurationLock', 'Get-ApiWorkerDeployFlags', 'New-ApiRuntimeFlagsContext', 'Assert-ApiHostRuntimeConfiguration',
    'Get-ApiLegacyWorkerConfiguration', 'Assert-ApiSharedBackendConfiguration', 'Enter-SharedBackendUploadLocks', 'Assert-SharedBackendUploadUnchanged')
$runtimeAst = [System.Management.Automation.Language.Parser]::ParseInput($deploySource, [ref]$null, [ref]$null)
foreach ($definition in $runtimeAst.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -cin $runtimeFunctions
}, $false)) { . ([scriptblock]::Create($definition.Extent.Text)) }
$runtimeBindings = @(
    [pscustomobject]@{ gameId = 'hearts'; url = 'https://hearts.example.run.app'; signingSecret = 'hearts-worker:1' },
    [pscustomobject]@{ gameId = 'horse-flight'; url = 'https://horse.example.run.app'; signingSecret = 'horse-worker:1' }
)
$runtimeJson = '{"environment":{"GameWorkers__hearts__ForwardPublicRoutes":true,"GameWorkers__horse-flight__ForwardFreeRoutes":true,"Slots__ArcaneWorkerRoundsEnabled":true,"Slots__NeonWorkerRoundsEnabled":false,"GameWallet__Games__hearts__AllowMultiAccountBatch":true,"ManagedPlayerRoster__Games__hearts__Enabled":true},"secrets":{"GameWallet__Games__hearts__SigningKey":"hearts-wallet:2","GameWallet__Games__hearts__BatchSigningKey":"hearts-batch:3","ManagedPlayerRoster__Games__hearts__SigningKey":"hearts-roster:4"}}'
$runtimeConfiguration = ConvertFrom-HostRuntimeConfiguration -Json $runtimeJson -Bindings $runtimeBindings -Repository $repoRoot
Assert-Equal $runtimeConfiguration.Environment['GameWorkers__hearts__ForwardPublicRoutes'] 'true' 'runtime JSON booleans normalize for Cloud Run'
Assert-Equal $runtimeConfiguration.Environment['Slots__NeonWorkerRoundsEnabled'] 'false' 'runtime JSON false is preserved'
$runtimeFlags = @(Get-ApiWorkerDeployFlags -SourceSha $head -Bindings $runtimeBindings -RuntimeConfiguration $runtimeConfiguration)
Assert-Equal $runtimeFlags.Count 2 'one combined environment flag and one secret flag'
Assert-Equal $runtimeFlags[0].Contains('GameWorkers__hearts__ForwardPublicRoutes=true') $true 'runtime switches reach candidate deployment'
Assert-Equal $runtimeFlags[0].Contains("FF_RELEASE_SHA=$head") $true 'runtime deployment retains source stamp'
Assert-Equal $runtimeFlags[1].Contains('GameWallet__Games__hearts__BatchSigningKey=hearts-batch:3') $true 'separate batch reference reaches candidate deployment'
foreach ($invalid in @(
    '{"environment":{"FF_RELEASE_SHA":true},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__Url":true},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__SigningKey":true},"secrets":{}}',
    '{"environment":{"GameWorkers__missing__ForwardPublicRoutes":true},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__ForwardFreeRoutes":true},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__ForwardPublicRoutes":"true"},"secrets":{}}',
    '{"environment":{"gameworkers__hearts__ForwardPublicRoutes":true},"secrets":{}}',
    '{"environment":{"Slots__TypoWorkerRoundsEnabled":true},"secrets":{}}',
    '{"environment":{"GameWallet__Games__hearts__AllowAnything":true},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__ForwardPublicRoutes":true,"GameWorkers__hearts__ForwardPublicRoutes":false},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__ForwardPublicRoutes":true},"environment":{"GameWorkers__hearts__ForwardPublicRoutes":false},"secrets":{}}',
    '{"environment":{"GameWorkers__hearts__ForwardPublicRout\u0065s":true},"secrets":{}}',
    '{"environment":{},"secrets":{}}',
    '{"environment":[],"secrets":{}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"private-key-value"}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"wallet:latest"}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"wallet:0"}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"projects/other/secrets/wallet:1"}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"hearts-worker:2"}}',
    '{"environment":{},"secrets":{"GameWallet__Games__hearts__SigningKey":"shared:1","GameWallet__Games__hearts__BatchSigningKey":"shared:2"}}',
    '{"environment":{},"secrets":{"ManagedPlayerRoster__Games__missing__SigningKey":"roster:1"}}',
    '{"environment":{},"secrets":{"GameWorkers__hearts__SigningKey":"override:1"}}',
    '{"environment":{"GameWorkers__hearts__ForwardPublicRoutes":true},"secrets":{},"extra":{}}'
)) {
    Assert-Throws { ConvertFrom-HostRuntimeConfiguration -Json $invalid -Bindings $runtimeBindings -Repository $repoRoot } 'unsafe host runtime configuration'
}
Assert-Throws { ConvertFrom-HostRuntimeConfiguration -Json $runtimeJson -Bindings @() -Repository $repoRoot } 'runtime configuration without reviewed bindings'
foreach ($grant in @('LegacySlotLedger', 'AllowLegacyFreeGames', 'AllowAccountBalanceRead', 'AllowCashWagerWithLegacyFreeGame',
    'AllowWagerReservations', 'AllowPlayerNameRead', 'AllowMultiAccountBatch', 'AllowArcadeCompetition')) {
    $grantJson = '{"environment":{"GameWallet__Games__hearts__' + $grant + '":true},"secrets":{}}'
    $grantConfiguration = ConvertFrom-HostRuntimeConfiguration -Json $grantJson -Bindings $runtimeBindings -Repository $repoRoot
    Assert-Equal $grantConfiguration.Environment.Count 1 'real wallet grant is accepted'
}
$runtimeEnv = @()
foreach ($name in $runtimeConfiguration.Environment.Keys) { $runtimeEnv += [pscustomobject]@{ name = $name; value = $runtimeConfiguration.Environment[$name] } }
foreach ($name in $runtimeConfiguration.Secrets.Keys) {
    $secretName, $secretVersion = $runtimeConfiguration.Secrets[$name] -split ':', 2
    $runtimeEnv += [pscustomobject]@{ name = $name; valueFrom = [pscustomobject]@{ secretKeyRef = [pscustomobject]@{ name = $secretName; key = $secretVersion } } }
}
$runtimeRevision = [pscustomobject]@{ spec = [pscustomobject]@{ containers = @([pscustomobject]@{ env = $runtimeEnv }) } }
Assert-ApiHostRuntimeConfiguration -Revision $runtimeRevision -Configuration $runtimeConfiguration
$runtimeEnv[0].value = 'false'
Assert-Throws { Assert-ApiHostRuntimeConfiguration -Revision $runtimeRevision -Configuration $runtimeConfiguration } 'wrong candidate runtime switch'
$runtimeEnv[0].value = 'true'
$runtimeEnv[-1].valueFrom.secretKeyRef.key = 'latest'
Assert-Throws { Assert-ApiHostRuntimeConfiguration -Revision $runtimeRevision -Configuration $runtimeConfiguration } 'wrong candidate runtime secret version'
$runtimeEnv[-1].valueFrom.secretKeyRef.key = '4'
$runtimeRevision.spec.containers[0].env += [pscustomobject]@{ name = $runtimeEnv[0].name.ToLowerInvariant(); value = 'true' }
Assert-Throws { Assert-ApiHostRuntimeConfiguration -Revision $runtimeRevision -Configuration $runtimeConfiguration } 'case-conflicting candidate runtime setting'
$runtimeFixtureDirectory = Join-Path $repoRoot 'artifacts/releases'
[System.IO.Directory]::CreateDirectory($runtimeFixtureDirectory) | Out-Null
$runtimeFixture = Join-Path $runtimeFixtureDirectory ('runtime-guard-test-' + [guid]::NewGuid().ToString('N') + '.json')
[System.IO.File]::WriteAllText($runtimeFixture, $runtimeJson, [System.Text.UTF8Encoding]::new($false))
$runtimeFileLock = $null
$runtimeFlagsContext = $null
try {
    $runtimeFileConfiguration = Read-HostRuntimeConfiguration -Path $runtimeFixture -Bindings $runtimeBindings -Repository $repoRoot
    $runtimeFileLock = Enter-HostRuntimeConfigurationLock -Configuration $runtimeFileConfiguration
    Assert-HostRuntimeConfigurationUnchanged -Configuration $runtimeFileConfiguration -LockedStream $runtimeFileLock
    Assert-Throws { [System.IO.File]::WriteAllText($runtimeFixture, '{}') } 'runtime config denies concurrent writes'
    $runtimeFlagsContext = New-ApiRuntimeFlagsContext -SourceSha $head -Bindings $runtimeBindings -RuntimeConfiguration $runtimeFileConfiguration -RecordPath $runtimeFixture
    $flagsDocument = Get-Content -LiteralPath $runtimeFlagsContext.Path -Raw | ConvertFrom-Json
    Assert-Equal $flagsDocument.'--update-env-vars' ($runtimeFlags[0] -split '=', 2)[1] 'JSON flags preserve the complete environment dictionary'
    Assert-Equal $flagsDocument.'--update-secrets' ($runtimeFlags[1] -split '=', 2)[1] 'JSON flags preserve every pinned secret reference'
    Assert-Throws { [System.IO.File]::WriteAllText($runtimeFlagsContext.Path, '{}') } 'generated API flags deny concurrent writes'
    $runtimeFileLock.Dispose(); $runtimeFileLock = $null
    [System.IO.File]::WriteAllText($runtimeFixture, $runtimeJson + ' ')
    Assert-Throws { Enter-HostRuntimeConfigurationLock -Configuration $runtimeFileConfiguration } 'runtime config byte changes after preflight'
}
finally {
    if ($runtimeFlagsContext) { $runtimeFlagsContext.Stream.Dispose(); Remove-Item -LiteralPath $runtimeFlagsContext.Path -Force }
    if ($runtimeFileLock) { $runtimeFileLock.Dispose() }
    Remove-Item -LiteralPath $runtimeFixture -Force
}
Assert-Equal $deploySource.Contains('$apiDeployArguments += "--flags-file=$($apiRuntimeFlagsContext.Path)"') $true 'catalogue configuration uses a locked flags file instead of a long shell argument'

$sharedFixture = Join-Path $repoRoot ('artifacts/releases/shared-upload-test-' + [guid]::NewGuid().ToString('N'))
$selectedRelease = '12345678-1234-4234-8234-123456789abc'
$sharedDirectory = Join-Path $sharedFixture 'artifacts/shared-backend-upload/game-packages'
$selectedDirectory = Join-Path $sharedDirectory ('hearts/' + $selectedRelease)
[IO.Directory]::CreateDirectory($selectedDirectory) | Out-Null
$selectedFile = Join-Path $selectedDirectory 'Worker.dll'
[IO.File]::WriteAllText($selectedFile, 'synthetic frozen package bytes')
$selectedHash = (Get-FileHash -LiteralPath $selectedFile -Algorithm SHA256).Hash.ToLowerInvariant()
$selectedSize = (Get-Item -LiteralPath $selectedFile).Length
$selectedComposition = [pscustomobject]@{ contractVersion = 1; packages = @([pscustomobject]@{
    runtimeGameId = 'hearts'; releaseId = $selectedRelease;
    files = @([pscustomobject]@{ path = 'Worker.dll'; size = $selectedSize; sha256 = $selectedHash })
}) }
$compositionFile = Join-Path $sharedDirectory 'composition.json'
[IO.File]::WriteAllText($compositionFile, ($selectedComposition | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$compositionHash = (Get-FileHash -LiteralPath $compositionFile -Algorithm SHA256).Hash.ToLowerInvariant()
$sharedLock = $null
try {
    $sharedLock = Enter-SharedBackendUploadLocks -Checkout $sharedFixture -Composition $selectedComposition `
        -CompositionSha256 $compositionHash -ManifestSha256 ('a' * 64)
    Assert-Equal $sharedLock.FileCount 2 'shared upload locks only selected package and composition files'
    Assert-SharedBackendUploadUnchanged $sharedLock
    Assert-Throws { [IO.File]::WriteAllText($selectedFile, 'changed') } 'frozen package bytes deny concurrent writes'
    $sharedFlags = @(Get-ApiWorkerDeployFlags -SourceSha $head -Bindings @() -SharedPackages $sharedLock)
    Assert-Equal $sharedFlags.Count 1 'shared backend requires no external worker secrets'
    Assert-Equal $sharedFlags[0].Contains('GamePackages__Required=true') $true 'shared backend cannot silently fall back to split workers'
    Assert-Equal $sharedFlags[0].Contains("GamePackages__ManifestSha256=$compositionHash") $true 'candidate binds the exact materialized composition'
    Assert-Throws { Get-ApiWorkerDeployFlags -SourceSha $head -Bindings $runtimeBindings -SharedPackages $sharedLock } 'shared backend rejects external worker bindings'
    Assert-Throws { Get-ApiWorkerDeployFlags -SourceSha $head -Bindings @() -RuntimeConfiguration $runtimeConfiguration -SharedPackages $sharedLock } 'shared backend rejects external runtime settings'
    $approvedPaths = @('Dockerfile', 'tracked.cs') + $sharedLock.Paths
    $approvedUpload = Assert-ReleaseUploadPaths -TrackedPaths $approvedPaths -UploadPaths $approvedPaths
    Assert-Equal $approvedUpload.FileCount 4 'exact selected locked package paths can join tracked source upload'
    Assert-Throws { Assert-ReleaseUploadPaths -TrackedPaths $approvedPaths -UploadPaths ($approvedPaths + 'artifacts/unselected/private.json') } 'shared packages do not permit broad artifact uploads'
    Exit-ReleaseSourceLock $sharedLock.Lock; $sharedLock = $null
    [IO.File]::WriteAllText($selectedFile, 'corrupt')
    Assert-Throws { Enter-SharedBackendUploadLocks -Checkout $sharedFixture -Composition $selectedComposition -CompositionSha256 $compositionHash -ManifestSha256 ('a' * 64) } 'selected package hash mismatch'
    [IO.File]::WriteAllText($selectedFile, 'synthetic frozen package bytes')
    [IO.File]::WriteAllText((Join-Path $sharedDirectory 'unselected.json'), '{}')
    Assert-Throws { Enter-SharedBackendUploadLocks -Checkout $sharedFixture -Composition $selectedComposition -CompositionSha256 $compositionHash -ManifestSha256 ('a' * 64) } 'unselected materialized file'
}
finally {
    if ($sharedLock) { Exit-ReleaseSourceLock $sharedLock.Lock }
    $resolvedFixture = [IO.Path]::GetFullPath($sharedFixture)
    $fixtureBoundary = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/releases')).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($fixtureBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test fixture cleanup boundary.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
$sharedRevision = [pscustomobject]@{
    metadata = [pscustomobject]@{ annotations = [pscustomobject]@{ 'autoscaling.knative.dev/maxScale' = '1'; 'autoscaling.knative.dev/minScale' = '0'; 'run.googleapis.com/cpu-throttling' = 'true' } }
    status = [pscustomobject]@{ conditions = @([pscustomobject]@{ type = 'Ready'; status = 'True' }) }
    spec = [pscustomobject]@{ containers = @([pscustomobject]@{
        env = @([pscustomobject]@{ name = 'GamePackages__Required'; value = 'true' },
            [pscustomobject]@{ name = 'GamePackages__Directory'; value = '/app/game-packages' },
            [pscustomobject]@{ name = 'GamePackages__ManifestSha256'; value = $compositionHash })
        startupProbe = [pscustomobject]@{ httpGet = [pscustomobject]@{ path = '/healthz'; port = 8080 }; timeoutSeconds = 5; periodSeconds = 5; failureThreshold = 48 }
    }) }
}
$sharedExpected = [pscustomobject]@{ CompositionSha256 = $compositionHash }
Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected
$sharedService = [pscustomobject]@{ metadata = [pscustomobject]@{ annotations = [pscustomobject]@{ 'run.googleapis.com/minScale' = '1' } } }
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected -Service $sharedService } 'service-level minimum cannot keep shared backend always warm'
$sharedService.metadata.annotations.'run.googleapis.com/minScale' = '0'
Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected -Service $sharedService
$legacyRevision = [pscustomobject]@{ spec = [pscustomobject]@{ containers = @([pscustomobject]@{ env = @(
    [pscustomobject]@{ name = 'GameWorkers__hearts__Url'; value = 'https://old.example.run.app' },
    [pscustomobject]@{ name = 'GameWorkers__hearts__SigningKey'; valueFrom = [pscustomobject]@{ secretKeyRef = [pscustomobject]@{ name = 'legacy-key'; key = '1' } } },
    [pscustomobject]@{ name = 'GAMEWORKERS__horse-flight__URL'; value = 'https://other.example.run.app' },
    [pscustomobject]@{ name = 'Features__HeartsEnabled'; value = 'true' }
) }) } }
$legacyConfiguration = Get-ApiLegacyWorkerConfiguration $legacyRevision
Assert-Equal $legacyConfiguration.Environment.Count 2 'derive only existing external worker environment names'
Assert-Equal $legacyConfiguration.Secrets.Count 1 'derive only existing external worker secret variable names'
$legacyRemoval = @(Get-ApiWorkerDeployFlags -SourceSha $head -Bindings @() -SharedPackages $sharedExpected -LegacyWorkerConfiguration $legacyConfiguration)
Assert-Equal $legacyRemoval.Count 3 'shared flags remove legacy environment and secret bindings alongside required composition'
Assert-Equal ($legacyRemoval -join ',').Contains('Features__HeartsEnabled') $false 'shared release preserves unrelated feature environment'
$sharedRevision.spec.containers[0].env += $legacyRevision.spec.containers[0].env[0]
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'shared candidate retains external worker URL'
$sharedRevision.spec.containers[0].env = @($sharedRevision.spec.containers[0].env | Where-Object { $_.name -cne 'GameWorkers__hearts__Url' })
$sharedRevision.spec.containers[0].env[2].value = 'wrong'
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'candidate composition digest mismatch'
$sharedRevision.spec.containers[0].env[2].value = $compositionHash
$sharedRevision.metadata.annotations.'run.googleapis.com/cpu-throttling' = 'false'
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'shared backend cannot silently use always-on CPU'
$sharedRevision.metadata.annotations.'run.googleapis.com/cpu-throttling' = 'true'
$sharedRevision.metadata.annotations.'autoscaling.knative.dev/minScale' = '1'
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'shared backend must scale to zero'
$sharedRevision.metadata.annotations.'autoscaling.knative.dev/minScale' = '0'
$sharedRevision.spec.containers[0].startupProbe.httpGet.path = '/'
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'shared backend cannot use a TCP-only or unrelated startup check'
$sharedRevision.spec.containers[0].startupProbe.httpGet.path = '/healthz'
$sharedRevision.status.conditions[0].status = 'False'
Assert-Throws { Assert-ApiSharedBackendConfiguration -Revision $sharedRevision -Packages $sharedExpected } 'shared backend must be ready before traffic'
$dockerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'Dockerfile') -Raw
Assert-Equal $dockerSource.Contains('COPY artifacts/shared-backend-upload/game-packages/ ./game-packages/') $true 'image copies selected frozen publish artifacts'
Assert-Equal $dockerSource.Contains('ENV GamePackages__Required=true') $true 'image refuses absent package composition'
Assert-Equal $deploySource.Contains("'verify-materialized', '--manifest'") $true 'reused package tree is independently verified'
Assert-Equal $deploySource.Contains('Assert-ApiSharedBackendConfiguration -Revision $candidateAtTrafficSwitch') $true 'candidate composition verified immediately before traffic'
foreach ($source in @($deploySource, $verifySource)) {
    Assert-Equal $source.Contains('[switch]$LivePlaytest') $true 'both release entry points expose explicit live-playtest mode'
    Assert-Equal $source.Contains('-LivePlaytest:$LivePlaytest') $true 'both release entry points independently bind playtest policy'
    Assert-Equal $source.Contains('[switch]$TestFree') $true 'both release entry points expose the explicit test-free route'
    Assert-Equal $source.Contains('-TestFree:$TestFree') $true 'both release entry points independently bind test-free policy'
}
Assert-Throws { Assert-ReleaseTestWaiver -Target all -SkipTestSuites -TestWaiverReason 'Owner request' } 'default combined release cannot waive tests'
Assert-Throws { Assert-ReleaseTestWaiver -Target all -LivePlaytest -SkipTestSuites } 'playtest requires a recorded reason'
Assert-ReleaseTestWaiver -Target all -LivePlaytest -SkipTestSuites -TestWaiverReason 'Explicit incremental playtest; broad assurance deferred.'
Assert-Throws { Assert-ReleaseTestWaiver -Target all -TestFree } 'test-free route requires a recorded reason'
Assert-Throws { Assert-ReleaseTestWaiver -Target all -TestFree -LivePlaytest -SkipTestSuites -TestWaiverReason 'Conflicting modes' } 'test-free route rejects other waiver modes'
Assert-ReleaseTestWaiver -Target all -TestFree -TestWaiverReason 'Owner explicitly requested the test-free route.'
Assert-Equal ($verifySource.IndexOf('Assert-NonRedundantFirestoreCompositeIndexes -Definitions') -lt $verifySource.IndexOf('function Invoke-Checked')) $true 'manifest validated before standalone verification commands'
$expectedIndexRead = $deploySource.IndexOf('$expectedIndexes = @(Get-ExpectedFirestoreIndexes -Checkout $releaseCheckout)')
$redundantIndexGuard = $deploySource.IndexOf('Assert-NonRedundantFirestoreCompositeIndexes -Definitions $expectedIndexes', $expectedIndexRead)
$currentIndexRead = $deploySource.IndexOf('$currentIndexes = @(Get-CurrentFirestoreIndexes -Expected $expectedIndexes)', $expectedIndexRead)
if ($expectedIndexRead -lt 0 -or $redundantIndexGuard -le $expectedIndexRead -or
    $currentIndexRead -le $redundantIndexGuard) {
    throw 'Redundant Firestore composite indexes must be rejected before reading or publishing production indexes.'
}
$firebaseAccountFlag = "'--account', " + '$firebaseAccount'
Assert-Equal ($deploySource.Split(@($firebaseAccountFlag), [System.StringSplitOptions]::None).Count - 1) 3 'explicit account for all Firebase publications'
$indexPublication = $deploySource.IndexOf("'--only', 'firestore:indexes'")
$indexConfig = $deploySource.IndexOf("'--config', " + '$indexContext.ConfigPath', $indexPublication)
$indexNonInteractive = $deploySource.IndexOf("'--non-interactive'", $indexConfig)
$rulesPublication = $deploySource.IndexOf("'--only', 'firestore:rules'")
if ($indexPublication -lt 0 -or $indexConfig -le $indexPublication -or
    $indexNonInteractive -le $indexConfig -or $indexNonInteractive -ge $rulesPublication -or
    $deploySource.Substring($indexPublication, $rulesPublication - $indexPublication).Contains("'--force'")) {
    throw 'Filtered Firestore index publication must use a temporary config, noninteractive mode and no force deletion.'
}
$trafficCommand = $deploySource.LastIndexOf("'run', 'services', 'update-traffic'")
if ($trafficCommand -lt 0) { throw 'API traffic command is missing from the deploy script.' }
$runtimeRevisionRead = $deploySource.LastIndexOf('$candidateAtTrafficSwitch = Get-ApiRevision $newApiRevision', $trafficCommand)
$runtimeRevisionGuard = $deploySource.LastIndexOf('Assert-ApiHostRuntimeConfiguration -Revision $candidateAtTrafficSwitch -Configuration $hostRuntimeConfiguration', $trafficCommand)
if ($runtimeRevisionRead -lt 0 -or $runtimeRevisionGuard -le $runtimeRevisionRead -or $runtimeRevisionGuard -ge $trafficCommand) {
    throw 'Candidate runtime configuration must be read and verified before the API traffic switch.'
}
$readinessCheck = $deploySource.LastIndexOf('Wait-FirestoreIndexesReady -Expected $expectedIndexes', $trafficCommand)
$fieldReadinessCheck = $deploySource.LastIndexOf('Wait-FirestoreFieldOverridesReady -Expected $expectedFieldOverrides', $trafficCommand)
$rulesPublication = $deploySource.IndexOf("'--only', 'firestore:rules'")
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
