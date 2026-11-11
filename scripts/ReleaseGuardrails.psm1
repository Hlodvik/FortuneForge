function Assert-ReleaseSha {
    param([Parameter(Mandatory)][string]$Sha)

    if ($Sha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
        throw "Invalid release commit SHA: $Sha"
    }
}

# One read-only Git boundary makes source/command-plan tests independent of
# disposable repositories. Callers inspect ExitCode; no mutation is supported.
function Invoke-ReleaseGit {
    param([string]$Repository, [string[]]$Arguments, [string[]]$InputLines)
    $allowed = @('rev-parse', 'symbolic-ref', 'status', 'merge-base', 'hash-object', 'cat-file', 'diff', 'show', 'ls-files')
    if ($Arguments.Count -eq 0 -or $Arguments[0] -cnotin $allowed -or $Arguments -ccontains '-w') {
        throw 'Release source inspection accepts read-only Git commands only.'
    }
    if ($PSBoundParameters.ContainsKey('InputLines')) {
        if (($Arguments -join ' ') -cne 'hash-object --stdin-paths') { throw 'Git stdin is supported only for read-only source hashing.' }
        # Write exact UTF-8 bytes: the Windows PowerShell native pipeline can
        # prepend a BOM, which Git interprets as part of the first filename.
        $start = [System.Diagnostics.ProcessStartInfo]::new()
        $start.FileName = (Get-Command git -CommandType Application | Select-Object -First 1).Source
        $start.WorkingDirectory = $Repository
        $start.Arguments = 'hash-object --stdin-paths'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            # .NET Framework chooses this encoding when it constructs the
            # redirected stdin writer; a BOM can be emitted during Start().
            $inputEncoding = [Console]::InputEncoding
            try {
                [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
                [void]$process.Start()
            }
            finally { [Console]::InputEncoding = $inputEncoding }
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($InputLines -join "`n") + "`n")
            $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
            $process.StandardInput.Close()
            $process.WaitForExit()
            $output = @($stdout.GetAwaiter().GetResult() -split '\r?\n' | Where-Object { $_ })
            $errorText = $stderr.GetAwaiter().GetResult()
            return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $errorText }
        }
        finally { $process.Dispose() }
    }
    $output = @(& git -C $Repository @Arguments 2>$null)
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }
}

function Assert-CommitOnMain {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Sha
    )

    Assert-ReleaseSha $Sha
    $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('merge-base', '--is-ancestor', $Sha, 'refs/heads/main')
    if ($result.ExitCode -ne 0) {
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
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('rev-parse', '--verify', "$Sha`:$relativePath")
        $committedBlob = $result.Output -join "`n"
        if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($committedBlob)) {
            throw "Release tooling $relativePath is not present in commit $Sha."
        }
        $absolutePath = Join-Path $Repository ($relativePath.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            throw "Release tooling $relativePath is missing from the active checkout."
        }
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('hash-object', "--path=$relativePath", $absolutePath)
        $workingBlob = $result.Output -join "`n"
        if ($result.ExitCode -ne 0 -or $workingBlob -cne $committedBlob) {
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
    $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('merge-base', '--is-ancestor', $LiveSha, $CandidateSha)
    if ($result.ExitCode -eq 0) { return }

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

function Get-FirestoreRulesHash {
    param([Parameter(Mandatory)][string]$Text)

    # Windows checkouts and the Rules API may differ only in line endings.
    return Get-ReleaseTextHash ($Text.Replace("`r`n", "`n").Replace("`r", "`n"))
}

function Assert-FirestoreRulesUnchanged {
    param([Parameter(Mandatory)]$Before, [Parameter(Mandatory)]$Now)

    if ($Before.ReleaseName -cne $Now.ReleaseName -or
        $Before.RulesetName -cne $Now.RulesetName -or
        $Before.ContentHash -cne $Now.ContentHash) {
        throw 'Firestore rules changed during release preparation. Refusing to overwrite that release.'
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

function Assert-NonRedundantFirestoreCompositeIndexes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Definitions)

    $redundant = [System.Collections.Generic.List[string]]::new()
    foreach ($definition in $Definitions) {
        $fields = @($definition.fields | Where-Object { $_ })
        if ($fields.Count -lt 1 -or $fields.Count -gt 2) { continue }
        $first = $fields[0]
        if ($first.fieldPath -ceq '__name__' -or $first.order -cnotin @('ASCENDING', 'DESCENDING')) {
            continue
        }
        if ($fields.Count -eq 2 -and ($fields[1].fieldPath -cne '__name__' -or
            $fields[1].order -cne $first.order)) { continue }
        $redundant.Add("$($definition.collectionGroup).$($first.fieldPath) $($first.order)")
    }
    if ($redundant.Count -gt 0) {
        throw "Firestore rejected redundant composite indexes that require single-field index controls instead: $($redundant -join '; '). Remove these composite definitions before publication."
    }
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

function Assert-SupportedFirestoreFieldOverrides {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Definitions)

    $seen = @{}
    foreach ($definition in $Definitions) {
        if ([string]::IsNullOrWhiteSpace($definition.collectionGroup) -or
            [string]::IsNullOrWhiteSpace($definition.fieldPath) -or
            -not ($definition.PSObject.Properties.Name -contains 'indexes') -or
            @($definition.PSObject.Properties.Name | Where-Object { $_ -cnotin @('collectionGroup', 'fieldPath', 'indexes') }).Count -gt 0 -or
            @($definition.indexes | Where-Object { $_ }).Count -ne 0) {
            throw 'The release readiness gate supports only explicit empty-index fieldOverrides.'
        }
        $key = "$($definition.collectionGroup)`n$($definition.fieldPath)"
        if ($seen.ContainsKey($key)) { throw "Duplicate Firestore field override: $key" }
        $seen[$key] = $true
    }
}

function Get-UnreadyFirestoreFieldOverrides {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Expected,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Actual
    )

    foreach ($definition in $Expected) {
        $matching = @($Actual | Where-Object {
            $_.collectionGroup -ceq $definition.collectionGroup -and $_.fieldPath -ceq $definition.fieldPath
        })
        if ($matching.Count -eq 1 -and $matching[0].usesAncestorConfig -ceq $false -and
            $matching[0].reverting -ceq $false -and
            @($matching[0].indexes | Where-Object { $_ }).Count -eq 0) { continue }
        $state = if ($matching.Count -eq 0) { 'MISSING' }
            elseif ($matching.Count -gt 1) { 'AMBIGUOUS' }
            elseif ($matching[0].reverting) { 'REVERTING' }
            elseif ($matching[0].usesAncestorConfig) { 'INHERITED' }
            else { 'INDEXED' }
        [pscustomobject]@{
            collectionGroup = [string]$definition.collectionGroup
            fieldPath = [string]$definition.fieldPath
            state = $state
        }
    }
}

function New-FirestoreIndexPublicationPlan {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ExpectedIndexes,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ActualIndexes,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ExpectedFieldOverrides,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ActualFieldOverrides
    )

    Assert-NonRedundantFirestoreCompositeIndexes -Definitions $ExpectedIndexes
    Assert-SupportedFirestoreFieldOverrides -Definitions $ExpectedFieldOverrides
    $missingIndexes = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $ExpectedIndexes.Count; $index++) {
        $definition = $ExpectedIndexes[$index]
        for ($prior = 0; $prior -lt $index; $prior++) {
            if ((Test-FirestoreIndexDefinitionMatches -Expected $definition -Actual $ExpectedIndexes[$prior]) -or
                (Test-FirestoreIndexDefinitionMatches -Expected $ExpectedIndexes[$prior] -Actual $definition)) {
                throw "Duplicate committed Firestore composite index in $($definition.collectionGroup)."
            }
        }
        $present = @($ActualIndexes | Where-Object {
            Test-FirestoreIndexDefinitionMatches -Expected $definition -Actual $_
        }).Count -gt 0
        # CREATING and failed indexes already have an identity: never POST them again.
        # The full readiness gate will wait for CREATING or reject NEEDS_REPAIR.
        if (-not $present) { $missingIndexes.Add($definition) }
    }

    $unreadyFields = @(Get-UnreadyFirestoreFieldOverrides -Expected $ExpectedFieldOverrides -Actual $ActualFieldOverrides)
    $fieldsToPublish = @($ExpectedFieldOverrides | Where-Object {
        $definition = $_
        @($unreadyFields | Where-Object {
            $_.collectionGroup -ceq $definition.collectionGroup -and $_.fieldPath -ceq $definition.fieldPath
        }).Count -gt 0
    })
    return [pscustomobject]@{
        Indexes = $missingIndexes.ToArray()
        FieldOverrides = $fieldsToPublish
    }
}

function Get-ReleaseToolPaths {
    @('scripts/deploy.ps1', 'scripts/ReleaseGuardrails.psm1',
      'scripts/tests/deploy-guardrails.tests.ps1', 'scripts/tests/release-scope.tests.ps1',
      'scripts/verify-release.ps1', 'scripts/verify-game-navbar.mjs',
      'scripts/tests/game-navbar-boundary.test.mjs', 'scripts/verify-ci-emulator-order.mjs',
      'scripts/tests/ci-emulator-order.test.mjs', '.github/workflows/ci.yml', '.gcloudignore')
}

function Assert-ReleaseCheckout {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Sha)
    Assert-ReleaseSha $Sha
    $head = Invoke-ReleaseGit -Repository $Repository -Arguments @('rev-parse', '--verify', 'HEAD')
    if ($head.ExitCode -ne 0 -or ($head.Output -join '').Trim() -cne $Sha) { throw 'Release checkout HEAD is not the exact selected commit; no checkout or reset will be performed.' }
    $branch = Invoke-ReleaseGit -Repository $Repository -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD')
    $main = Invoke-ReleaseGit -Repository $Repository -Arguments @('rev-parse', '--verify', 'refs/heads/main')
    if ($branch.ExitCode -ne 0 -or ($branch.Output -join '').Trim() -cne 'main' -or
        $main.ExitCode -ne 0 -or ($main.Output -join '').Trim() -cne $Sha) {
        throw 'Existing-checkout release requires the current local main branch at the exact candidate.'
    }
    $dirty = Invoke-ReleaseGit -Repository $Repository -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
    if ($dirty.ExitCode -ne 0) { throw 'Cannot inspect source cleanliness; release is blocked.' }
    if (@($dirty.Output | Where-Object { -not [string]::IsNullOrEmpty($_) }).Count -gt 0) {
        throw 'Existing-checkout release requires clean committed source. Reconcile and commit intended changes first; no stash, clean, reset, copy or worktree will be performed.'
    }
    $flags = Invoke-ReleaseGit -Repository $Repository -Arguments @('ls-files', '-v', '-z')
    if ($flags.ExitCode -ne 0 -or @((($flags.Output -join "`n").Split([char]0)) | Where-Object { $_ -and -not $_.StartsWith('H ', [System.StringComparison]::Ordinal) }).Count -gt 0) {
        throw 'Release source cannot use assume-unchanged or skip-worktree index flags that hide local changes.'
    }
    # Recheck ignored/autoloaded inputs at every caller's command/publication
    # boundary, not only once during the initial candidate preflight.
    Assert-ReleaseBuildInputs -Repository $Repository
}

function Get-ReleaseLocalPreflight {
    param([Parameter(Mandatory)][string]$Repository, [string]$Commit = 'HEAD')
    $candidate = Invoke-ReleaseGit -Repository $Repository -Arguments @('rev-parse', '--verify', "$Commit`^{commit}")
    if ($candidate.ExitCode -ne 0) { throw "Cannot resolve release candidate $Commit." }
    $sha = ($candidate.Output -join '').Trim()
    Assert-ReleaseCheckout -Repository $Repository -Sha $sha
    Assert-CommitOnMain -Repository $Repository -Sha $sha
    Assert-ReleaseToolingMatchesCommit -Repository $Repository -Sha $sha -Paths (Get-ReleaseToolPaths)
    $tree = Invoke-ReleaseGit -Repository $Repository -Arguments @('rev-parse', '--verify', "$sha`^{tree}")
    if ($tree.ExitCode -ne 0) { throw 'Cannot identify the committed candidate tree.' }
    Assert-ReleaseSha (($tree.Output -join '').Trim())
    return [pscustomobject]@{ SourceMode = 'existing-clean-checkout'; CandidateSha = $sha;
        ToolingSha = $sha; TreeSha = ($tree.Output -join '').Trim(); Repository = [System.IO.Path]::GetFullPath($Repository) }
}

function Assert-ReleaseBuildInputs {
    param([Parameter(Mandatory)][string]$Repository)
    $directories = @($Repository, (Join-Path $Repository 'fortuneforge.client'))
    $manifestPath = Join-Path $Repository 'package.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $package = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
        $directories += @($package.workspaces | ForEach-Object { Join-Path $Repository $_ })
    }
    foreach ($directory in ($directories | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '.env*' -File -Force)) {
            if ($file.Name -ceq '.env.example') { continue }
            throw "Local environment file $($file.FullName) is not accepted as an implicit release build input. Use reviewed committed configuration."
        }
    }
    $serverDirectory = Join-Path $Repository 'FortuneForge.Server'
    if (Test-Path -LiteralPath $serverDirectory -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $serverDirectory -Filter 'appsettings*.json' -File)) {
            if ($file.Name -ieq 'appsettings.Local.json' -or $file.Name -imatch '\.local\.json$') {
                throw "Local server settings $($file.FullName) cannot participate in committed-candidate verification."
            }
        }
    }
    if (@(Get-ChildItem Env: | Where-Object { $_.Name -cmatch '^VITE_' }).Count -gt 0) {
        throw 'External VITE_ variables are not accepted for reproducible release builds; use reviewed committed configuration.'
    }
}

function Enter-ReleaseWorkspaceLock {
    param([Parameter(Mandatory)][string]$Repository)
    $directory = Join-Path $Repository 'artifacts/releases'
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    try {
        # The retained ignored lock file is harmless after disposal. Never
        # delete old release folders, and never break another process's lock.
        return [System.IO.File]::Open((Join-Path $directory 'active.lock'),
            [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    }
    catch { throw 'Another release owns this checkout, or its release lock cannot be acquired.' }
}

function Enter-ReleaseSourceLock {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Sha)
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        throw 'Existing-checkout publication requires Windows file sharing locks; no unlocked fallback is allowed.'
    }
    Assert-ReleaseCheckout -Repository $Repository -Sha $Sha
    $tracked = Invoke-ReleaseGit -Repository $Repository -Arguments @('ls-files', '--stage', '-z')
    if ($tracked.ExitCode -ne 0) { throw 'Cannot enumerate committed source for immutable file locks.' }
    $streams = [System.Collections.Generic.List[System.IO.FileStream]]::new()
    $manifest = [System.Collections.Generic.List[string]]::new()
    $paths = [System.Collections.Generic.List[string]]::new()
    $trackedPaths = [System.Collections.Generic.List[string]]::new()
    $blobs = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($entry in (($tracked.Output -join "`n").Split([char]0) | Where-Object { $_ })) {
            if ($entry -cnotmatch '^(100644|100755) ([0-9a-f]+) 0\t([^\x00-\x1f\x7f]+)$') { throw 'Only tracked regular files with unambiguous paths can be locked for release.' }
            $blob = $Matches[2]
            $relative = $Matches[3]
            if ($relative -cmatch '[\\"]|(^|/)\.\.?(/|$)' -or [System.IO.Path]::IsPathRooted($relative)) { throw 'Unsafe tracked source path.' }
            $path = Join-Path $Repository $relative
            if ((Get-Item -LiteralPath $path).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point source is not supported for an existing-checkout release.' }
            $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $streams.Add($stream)
            $hash = [System.Security.Cryptography.SHA256]::Create()
            try { $hex = ([System.BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
            finally { $hash.Dispose() }
            $stream.Position = 0
            $manifest.Add("$relative`t$($stream.Length)`t$hex")
            $paths.Add('"' + $relative + '"')
            $trackedPaths.Add($relative)
            $blobs.Add($blob)
        }
        if ($streams.Count -eq 0) { throw 'No committed source files were locked.' }
        $canonical = Invoke-ReleaseGit -Repository $Repository -Arguments @('hash-object', '--stdin-paths') -InputLines $paths.ToArray()
        if ($canonical.ExitCode -ne 0 -or $canonical.Output.Count -ne $blobs.Count) { throw 'Cannot verify locked source against Git objects.' }
        for ($i = 0; $i -lt $blobs.Count; $i++) {
            if ($canonical.Output[$i] -cne $blobs[$i]) { throw 'Locked source bytes differ from the committed index; release is blocked.' }
        }
        Assert-ReleaseCheckout -Repository $Repository -Sha $Sha
        $manifestLines = $manifest.ToArray()
        [System.Array]::Sort($manifestLines, [System.StringComparer]::Ordinal)
        return [pscustomobject]@{ Streams = $streams; FileCount = $streams.Count; TrackedPaths = $trackedPaths.ToArray();
            ManifestSha256 = Get-ReleaseTextHash ($manifestLines -join "`n") }
    }
    catch { foreach ($stream in $streams) { $stream.Dispose() }; throw }
}

function Exit-ReleaseSourceLock {
    param($Lock)
    if ($Lock) { foreach ($stream in $Lock.Streams) { $stream.Dispose() } }
}

function Enter-ReleaseHostingLock {
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$CandidateSha)
    Assert-ReleaseSha $CandidateSha
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) { throw 'Hosting artifact locking requires Windows sharing semantics.' }
    $root = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Directory)
    $streams = [System.Collections.Generic.List[System.IO.FileStream]]::new()
    $manifest = [System.Collections.Generic.List[string]]::new()
    $files = [System.Collections.Generic.List[object]]::new()
    try {
        while ($pending.Count) {
            foreach ($entry in @(Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
                if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Hosting artifacts cannot contain reparse points.' }
                if (-not $entry.FullName.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Hosting artifact escaped the verified directory.' }
                if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
                $relative = $entry.FullName.Substring($root.Length).Replace('\', '/')
                if ($relative -cmatch '[\x00-\x1f\x7f]') { throw 'Ambiguous Hosting artifact path.' }
                $stream = [System.IO.File]::Open($entry.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                $streams.Add($stream)
                $hash = [System.Security.Cryptography.SHA256]::Create()
                try { $hex = ([System.BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
                finally { $hash.Dispose() }
                $stream.Position = 0
                $manifest.Add("$relative`t$($stream.Length)`t$hex")
                $files.Add([pscustomobject]@{ Path = $relative; Bytes = $stream.Length; Sha256 = $hex; Stream = $stream })
            }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $Directory 'index.html')) -or -not (Test-Path -LiteralPath (Join-Path $Directory 'release.json'))) { throw 'Verified Hosting artifacts require index.html and a candidate stamp.' }
        $marker = [System.IO.File]::ReadAllText((Join-Path $Directory 'release.json')) | ConvertFrom-Json -ErrorAction Stop
        if ($marker.schemaVersion -ne 1 -or $marker.target -cne 'hosting' -or $marker.sourceSha -cne $CandidateSha) { throw 'Locked Hosting stamp does not match the verified candidate.' }
        $lines = $manifest.ToArray()
        [System.Array]::Sort($lines, [System.StringComparer]::Ordinal)
        return [pscustomobject]@{ Streams = $streams; Files = $files.ToArray(); FileCount = $streams.Count; ManifestSha256 = Get-ReleaseTextHash ($lines -join "`n") }
    }
    catch { foreach ($stream in $streams) { $stream.Dispose() }; throw }
}

function Assert-HostingApiCompatibility {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$CandidateSha,
        [AllowEmptyString()][string]$LiveApiSha = '', [switch]$RequireExact)
    Assert-ReleaseSha $CandidateSha
    if ($LiveApiSha -ceq $CandidateSha) { return }
    if ($RequireExact -or [string]::IsNullOrWhiteSpace($LiveApiSha)) {
        throw 'Hosting publication requires a compatible stamped API. Release API and Hosting together; an unstamped or different API cannot be assumed compatible.'
    }
    Assert-ReleaseSha $LiveApiSha
    $available = Invoke-ReleaseGit -Repository $Repository -Arguments @('cat-file', '-e', "$LiveApiSha`^{commit}")
    $ancestor = Invoke-ReleaseGit -Repository $Repository -Arguments @('merge-base', '--is-ancestor', $LiveApiSha, $CandidateSha)
    if ($available.ExitCode -ne 0 -or $ancestor.ExitCode -ne 0) { throw 'The serving API source is unavailable or not a candidate ancestor; use a reconciled combined release.' }
    $diff = Invoke-ReleaseGit -Repository $Repository -Arguments @('diff', '--name-only', '-z', '--no-renames', '--no-ext-diff', '--no-textconv', $LiveApiSha, $CandidateSha, '--')
    if ($diff.ExitCode -ne 0) { throw 'Cannot prove frontend/API source compatibility.' }
    $raw = $diff.Output -join "`n"
    if ($raw -and -not $raw.EndsWith([string][char]0)) { throw 'Incomplete frontend/API compatibility diff; release is blocked.' }
    foreach ($path in ($raw.Split([char]0) | Where-Object { $_ })) {
        # Positive allowlist: API/runtime/catalogue/configuration or unknown
        # changes require the API first. A test waiver never changes this gate.
        if ($path -cmatch '[\\\x00-\x1f\x7f]|(^|/)\.\.?(/|$)' -or
            $path -cnotmatch '^(fortuneforge\.client/|packages/client-presentation/|games/[A-Za-z0-9-]+/client/|FortuneForge\.Server\.Tests/|games/.+/tests/|docs/|\.github/|scripts/(deploy\.ps1$|ReleaseGuardrails\.psm1$|verify-release\.ps1$|verify-game-navbar\.mjs$|verify-ci-emulator-order\.mjs$|tests/)|README\.md$|package(-lock)?\.json$)') {
            throw "Hosting candidate changes API or unclassified source ($path). Release API and Hosting together."
        }
    }
}

function Assert-ReleaseUploadPaths {
    param([Parameter(Mandatory)][string[]]$TrackedPaths,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$UploadPaths)
    $tracked = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $TrackedPaths) { [void]$tracked.Add($path) }
    $selected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($value in $UploadPaths) {
        if ($value -isnot [string]) { throw 'Unexpected source upload inventory format.' }
        $path = $value.Replace('\', '/')
        if ($path.StartsWith('./', [System.StringComparison]::Ordinal)) { $path = $path.Substring(2) }
        if (-not $path -or $path -cmatch '[\x00-\x1f\x7f\[\]*?!#]|(^|/)\.\.?(/|$)' -or
            [System.IO.Path]::IsPathRooted($path) -or -not $tracked.Contains($path)) {
            throw "Source upload would include an untracked, ignored or unsafe local file ($path). Add reviewed upload exclusions; do not remove private folders."
        }
        if (-not $selected.Add($path)) { throw 'Duplicate source upload inventory entry.' }
    }
    if ($selected.Count -eq 0 -or -not $selected.Contains('Dockerfile')) { throw 'Source upload must contain the committed Dockerfile and tracked source.' }
    $paths = [string[]]@($selected)
    [System.Array]::Sort($paths, [System.StringComparer]::Ordinal)
    return [pscustomobject]@{ Paths = $paths; FileCount = $paths.Length; ManifestSha256 = Get-ReleaseTextHash ($paths -join "`n") }
}

function New-ReleaseUploadIgnoreText {
    param([Parameter(Mandatory)][string[]]$Paths)
    function Escape-UploadPattern([string]$Path) {
        $builder = [System.Text.StringBuilder]::new()
        foreach ($character in $Path.ToCharArray()) {
            if ($character -cin @('\', '*', '?', '[', ']', '!', '#', ' ')) { [void]$builder.Append('\') }
            [void]$builder.Append($character)
        }
        return $builder.ToString()
    }
    $directories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        if (-not $path -or $path -cmatch '[\\\x00-\x1f\x7f\[\]*?!#]|(^|/)\.\.?(/|$)' -or [System.IO.Path]::IsPathRooted($path)) {
            throw 'Unsupported literal upload path; do not interpret source names as ignore-file patterns.'
        }
        $parts = $path.Split('/')
        for ($i = 1; $i -lt $parts.Length; $i++) { [void]$directories.Add(($parts[0..($i - 1)] -join '/')) }
    }
    $directoryPaths = [string[]]@($directories)
    [System.Array]::Sort($directoryPaths, [System.StringComparer]::Ordinal)
    $filePaths = [string[]]$Paths.Clone()
    [System.Array]::Sort($filePaths, [System.StringComparer]::Ordinal)
    $lines = @('# Generated from locked committed upload paths; reject every other file.', '**')
    $lines += @($directoryPaths | ForEach-Object { '!/' + (Escape-UploadPattern $_) + '/' })
    $lines += @($filePaths | ForEach-Object { '!/' + (Escape-UploadPattern $_) })
    return ($lines -join "`n") + "`n"
}

function Get-ReleaseBuiltImage {
    param([Parameter(Mandatory)]$Build, [Parameter(Mandatory)][string]$ExpectedTag)
    $id = [guid]::Empty
    if ($Build.status -cne 'SUCCESS' -or -not [guid]::TryParse([string]$Build.id, [ref]$id)) {
        throw 'Cloud Build did not report an identified successful build; API traffic is unchanged.'
    }
    if ($ExpectedTag -cnotmatch '^[a-z0-9-]+-docker\.pkg\.dev/[a-z0-9-]+/[a-z0-9/_-]+:[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$') {
        throw 'Unsupported configured Artifact Registry image tag.'
    }
    $images = @($Build.results.images | Where-Object { $_ })
    if ($images.Count -ne 1 -or $images[0].name -cne $ExpectedTag -or
        [string]$images[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw 'Cloud Build returned missing, ambiguous or mismatched image evidence; never resolve the mutable tag afterward.'
    }
    $image = $ExpectedTag.Substring(0, $ExpectedTag.LastIndexOf(':')) + '@' + $images[0].digest
    return [pscustomobject]@{ BuildId = [string]$Build.id; Tag = $ExpectedTag; Digest = [string]$images[0].digest; Image = $image }
}

function New-ReleaseHostingIgnorePatterns {
    param([Parameter(Mandatory)][string[]]$Paths)
    $children = @{}
    foreach ($path in $Paths) {
        $parts = $path.Split('/')
        if ($parts.Count -eq 0 -or @($parts | Where-Object { $_ -cnotmatch '^[A-Za-z0-9_.-]+$' -or $_ -cin @('.', '..') }).Count -gt 0) {
            throw 'Unsupported Hosting artifact filename; exact SDK extglob alternatives require safe literal segments.'
        }
        for ($i = 0; $i -lt $parts.Length; $i++) {
            $parent = if ($i -eq 0) { '' } else { $parts[0..($i - 1)] -join '/' }
            if (-not $children.ContainsKey($parent)) { $children[$parent] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal) }
            [void]$children[$parent].Add($parts[$i])
        }
    }
    if ($children.Count -eq 0) { throw 'Hosting artifact manifest cannot be empty.' }
    $directories = [string[]]@($children.Keys)
    [System.Array]::Sort($directories, [System.StringComparer]::Ordinal)
    $patterns = [System.Collections.Generic.List[string]]::new()
    foreach ($directory in $directories) {
        $names = [string[]]@($children[$directory])
        [System.Array]::Sort($names, [System.StringComparer]::Ordinal)
        $prefix = if ($directory) { $directory + '/' } else { '' }
        # Firebase14.4 uses glob10 with nonegate:true, so Git-style negations
        # cannot form an allowlist. Exclude the exact per-directory complement.
        $pattern = $prefix + '!(' + ($names -join '|') + ')'
        if ($pattern.Length -gt 60000) { throw 'Hosting artifact whitelist exceeds the verified SDK pattern limit.' }
        $patterns.Add($pattern)
        $patterns.Add($pattern + '/**')
    }
    return $patterns.ToArray()
}

function Assert-ApiRevisionImage {
    param([Parameter(Mandatory)]$Revision, [Parameter(Mandatory)][string]$ExpectedImage)
    $containers = @($Revision.spec.containers | Where-Object { $_ })
    if ($containers.Count -ne 1 -or $ExpectedImage -cnotmatch '@sha256:[0-9a-f]{64}$' -or $containers[0].image -cne $ExpectedImage) {
        throw 'New API revision does not reference the exact image digest returned by the candidate build.'
    }
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
        -not $selected.HasCheck) {
        $plan.reason = 'A registered game with a check script and a visual change is required.'; return [pscustomobject]$plan
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
        [switch]$Rollback, [switch]$Adoption, [switch]$LivePlaytest
    )
    Assert-ReleaseSha $CandidateSha
    if ($LivePlaytest) {
        if ($Target -cnotin @('api', 'all')) { throw '-LivePlaytest requires an API or combined release.' }
        $plan = New-ReleaseVerificationPlan -Target $Target -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason 'Explicit live playtest: broad application suites deferred; fixed focused backend tests remain mandatory.'
        $plan.mode = 'live-playtest'
        return $plan
    }
    $reason = ''
    if ($Target -cne 'hosting') { $reason = 'API or combined release requires full verification.' }
    elseif ($Rollback -or $Adoption) { $reason = 'Rollback or adoption requires full verification.' }
    elseif ($HostingBaseSha -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') { $reason = 'No valid live Hosting baseline.' }
    if ($reason) { return New-ReleaseVerificationPlan -Target $Target -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason $reason }
    try {
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('cat-file', '-e', "$HostingBaseSha`^{commit}")
        if ($result.ExitCode -ne 0) { throw 'Missing baseline object.' }
    }
    catch { return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason 'Live Hosting commit is unavailable locally.' }
    try {
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('merge-base', '--is-ancestor', $HostingBaseSha, $CandidateSha)
        if ($result.ExitCode -ne 0) { throw 'Nonancestor baseline.' }
    }
    catch { return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason 'Live Hosting baseline is not an ancestor.' }
    try {
        # -z keeps file names intact; disabling rename detection exposes both
        # sides, so any deletion falls back to full checks.
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('diff', '--raw', '-z', '--no-renames', '--no-ext-diff', '--no-textconv', $HostingBaseSha, $CandidateSha, '--')
        if ($result.ExitCode -ne 0) { throw 'Cannot read live-to-candidate diff.' }
        $rawLines = @($result.Output)
        $raw = $rawLines -join "`n"
        $parts = $raw.Split([char]0)
        if ($raw -and (-not $raw.EndsWith([string][char]0) -or $parts.Length % 2 -ne 1)) { throw 'Incomplete Git diff record.' }
        $changes = @()
        for ($index = 0; $index -lt $parts.Length - 1; $index += 2) {
            if ($parts[$index] -cnotmatch '^:([0-7]{6}) ([0-7]{6}) [0-9a-f]+ [0-9a-f]+ ([A-Z][0-9]*)$') { throw 'Unsupported Git diff record.' }
            $changes += [pscustomobject]@{ OldMode = $Matches[1]; NewMode = $Matches[2]; Status = $Matches[3]; Path = $parts[$index + 1] }
        }
        $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('show', "${CandidateSha}:package.json")
        if ($result.ExitCode -ne 0) { throw 'Missing root workspace manifest.' }
        $packageText = @($result.Output)
        $package = ($packageText -join "`n") | ConvertFrom-Json -ErrorAction Stop
        $workspaces = @()
        foreach ($path in @($package.workspaces)) {
            if ($path -cnotmatch '^games/[A-Za-z0-9-]+/client/[A-Za-z0-9.-]+$') { continue }
            $result = Invoke-ReleaseGit -Repository $Repository -Arguments @('show', "${CandidateSha}:$path/package.json")
            if ($result.ExitCode -ne 0) { throw "Missing workspace manifest: $path" }
            $manifestText = @($result.Output)
            $manifest = ($manifestText -join "`n") | ConvertFrom-Json -ErrorAction Stop
            $workspaces += [pscustomobject]@{ Path = $path; Name = [string]$manifest.name;
                HasCheck = -not [string]::IsNullOrWhiteSpace($manifest.scripts.check) }
        }
        return New-ReleaseVerificationPlan -Target $Target -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -Changes $changes -Workspaces $workspaces
    }
    catch {
        return New-ReleaseVerificationPlan -BaseSha $HostingBaseSha -CandidateSha $CandidateSha -FullReason "Cannot safely classify scope: $($_.Exception.Message)"
    }
}

function Assert-ReleaseTestWaiver {
    param(
        [string]$Target = 'all', [switch]$SkipTestSuites, [string]$TestWaiverReason = '',
        [switch]$LivePlaytest, [switch]$TestFree
    )
    if ($TestFree -and ($SkipTestSuites -or $LivePlaytest)) {
        throw '-TestFree is a separate no-test route and cannot be combined with -SkipTestSuites or -LivePlaytest.'
    }
    if ($LivePlaytest -and ($Target -cnotin @('api', 'all') -or -not $SkipTestSuites)) {
        throw '-LivePlaytest requires -Target api/all and -SkipTestSuites with an explicit reason.'
    }
    if ($SkipTestSuites) {
        if ($Target -cne 'hosting' -and -not $LivePlaytest) { throw '-SkipTestSuites requires Hosting-only scope or explicit -LivePlaytest for API/all.' }
        if ([string]::IsNullOrWhiteSpace($TestWaiverReason)) { throw '-SkipTestSuites requires a nonempty -TestWaiverReason.' }
    }
    elseif ($TestFree) {
        if ([string]::IsNullOrWhiteSpace($TestWaiverReason)) { throw '-TestFree requires a nonempty -TestWaiverReason.' }
    }
    elseif (-not [string]::IsNullOrEmpty($TestWaiverReason)) { throw '-TestWaiverReason requires -SkipTestSuites or -TestFree.' }
}

function Get-ReleaseFocusedTestScopes {
    # Fixed committed policy, never a caller-provided filter or an empty suite.
    foreach ($scope in @(
        @('slots', 'FortuneForge.Server.Tests.Slots.'),
        @('paid-craps-store', 'FortuneForge.Server.Tests.Games.Craps.FirestorePaidCrapsStoreTests'),
        @('paid-craps-audit', 'FortuneForge.Server.Tests.SecurityAudit.PaidCraps'),
        @('activity', 'FortuneForge.Server.Tests.Activity.'),
        @('history', 'FortuneForge.Server.Tests.Cards.History.CardRoomHistoryQueryTests'),
        @('investigation', 'FortuneForge.Server.Tests.SecurityAudit.InvestigationFirestoreTests')
    )) {
        [pscustomobject]@{ Name = $scope[0]; Filter = 'FullyQualifiedName~' + $scope[1] }
    }
}

function Assert-ReleaseFocusedTestResult {
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { throw "Focused test report is missing: $Path" }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $report = [System.Xml.XmlDocument]::new()
        $report.XmlResolver = $null
        $report.Load($reader)
    }
    finally { $reader.Dispose() }
    $ns = [System.Xml.XmlNamespaceManager]::new($report.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $summary = $report.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
    $counters = $report.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
    $results = @($report.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    $total = 0
    $executed = 0
    $passed = 0
    if (-not $summary -or -not $counters -or $summary.GetAttribute('outcome') -cne 'Completed' -or
        -not [int]::TryParse($counters.GetAttribute('total'), [ref]$total) -or
        -not [int]::TryParse($counters.GetAttribute('executed'), [ref]$executed) -or
        -not [int]::TryParse($counters.GetAttribute('passed'), [ref]$passed) -or
        $total -le 0 -or $executed -ne $total -or $passed -ne $total -or $results.Count -ne $total -or
        @($results | Where-Object { $_.GetAttribute('outcome') -cne 'Passed' }).Count -ne 0) {
        throw "Focused tests must execute a nonempty suite with every test passed (no skipped/failed/aborted results): $Path"
    }
}

function Get-ReleaseVerificationCommands {
    param(
        [Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][string]$Repository,
        [string]$Target = 'all', [switch]$SkipTestSuites, [string]$TestWaiverReason = '',
        [switch]$LivePlaytest, [switch]$TestFree, [string]$FocusedResultsDirectory = ''
    )
    Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason -LivePlaytest:$LivePlaytest -TestFree:$TestFree
    if ($Plan.mode -cnotin @('full', 'game-visual', 'live-playtest')) { throw 'Unknown verification mode.' }
    if (($Plan.mode -ceq 'live-playtest') -ne [bool]$LivePlaytest) { throw 'Live-playtest plan and explicit verification flag must match.' }
    if ($LivePlaytest -and [string]::IsNullOrWhiteSpace($FocusedResultsDirectory)) { throw 'Live playtest requires fresh focused test reports.' }
    if ($Plan.mode -ceq 'game-visual' -and $Plan.workspace -cnotmatch '^@fortuneforge/games-[a-z0-9-]+$') { throw 'Invalid scoped workspace.' }
    $commands = [System.Collections.Generic.List[object]]::new()
    foreach ($script in @('verify:game-source', 'verify:game-routes')) {
        $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', $script) })
    }
    if ($TestFree) {
        # The public npm verification commands prepend node:test policy suites.
        # Run only their underlying deterministic checks on the no-test route.
        foreach ($path in @('scripts/verify-game-navbar.mjs', 'scripts/verify-ci-emulator-order.mjs')) {
            $commands.Add([pscustomobject]@{ Executable = 'node.exe'; Arguments = @((Join-Path $Repository $path)) })
        }
    }
    else {
        foreach ($script in @('verify:game-navbar', 'verify:ci-emulator')) {
            $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', $script) })
        }
    }
    $checkArgs = if ($Plan.mode -ceq 'game-visual') { @('run', 'check', '--workspace', $Plan.workspace) } else { @('run', 'games:check') }
    $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = $checkArgs })
    $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'games:build') })
    if (-not ($SkipTestSuites -or $TestFree)) {
        if ($Plan.mode -ceq 'game-visual') {
            $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'test', '--workspace', $Plan.workspace, '--if-present') })
            $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'web:test') })
        }
        else { $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', 'test') }) }
    }
    foreach ($script in @('lint', 'web:build')) { $commands.Add([pscustomobject]@{ Executable = 'npm.cmd'; Arguments = @('run', $script) }) }
    if ($LivePlaytest) {
        $project = Join-Path $Repository 'FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj'
        $commands.Add([pscustomobject]@{ Executable = 'dotnet'; Arguments = @('build', $project, '--configuration', 'Release') })
        foreach ($scope in @(Get-ReleaseFocusedTestScopes)) {
            $commands.Add([pscustomobject]@{ Executable = 'dotnet';
                Arguments = @('test', $project, '--configuration', 'Release', '--no-build', '--no-restore',
                    '--filter', $scope.Filter, '--results-directory', $FocusedResultsDirectory,
                    '--logger', "trx;LogFileName=$($scope.Name).trx", '--', 'RunConfiguration.TreatNoTestsAsError=true');
                FocusedResultPath = (Join-Path $FocusedResultsDirectory ($scope.Name + '.trx')) })
        }
    }
    if ($Plan.mode -ceq 'full' -and -not ($SkipTestSuites -or $TestFree)) {
        $project = Join-Path $Repository 'FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj'
        $commands.Add([pscustomobject]@{ Executable = 'dotnet'; Arguments = @('test', $project, '--configuration', 'Release') })
    }
    return $commands.ToArray()
}

Export-ModuleMember -Function Assert-ReleaseSha, Assert-CommitOnMain, Assert-ReleaseToolingMatchesCommit, Assert-LiveReleaseSource, Assert-HostingAdoptionRelease, Get-ReleaseTextHash, Get-FirestoreRulesHash, Assert-FirestoreRulesUnchanged, Get-HostingSourceSha, Get-ServingApiRevision, Get-ApiRevisionSourceSha, Assert-ServingApiUnchanged, Test-FirestoreIndexDefinitionMatches, Test-FirestoreIndexBelongsToGroup, Assert-NonRedundantFirestoreCompositeIndexes, Get-UnreadyFirestoreIndexes, Assert-SupportedFirestoreFieldOverrides, Get-UnreadyFirestoreFieldOverrides, New-FirestoreIndexPublicationPlan, Get-ReleaseToolPaths, Assert-ReleaseCheckout, Get-ReleaseLocalPreflight, Enter-ReleaseWorkspaceLock, Enter-ReleaseSourceLock, Exit-ReleaseSourceLock, Enter-ReleaseHostingLock, Assert-HostingApiCompatibility, Assert-ReleaseUploadPaths, New-ReleaseUploadIgnoreText, New-ReleaseHostingIgnorePatterns, Get-ReleaseBuiltImage, Assert-ApiRevisionImage, New-ReleaseVerificationPlan, Get-ReleaseVerificationPlan, Assert-ReleaseTestWaiver, Get-ReleaseFocusedTestScopes, Assert-ReleaseFocusedTestResult, Get-ReleaseVerificationCommands
