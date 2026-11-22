# Edited: 2026-11-18T22:46:29-06:00 | Computer: VINNABOK
[CmdletBinding()]
param(
    [ValidateSet('all', 'api', 'hosting', 'site')]
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
    [string]$TestWaiverReason = '',
    [string]$GameWorkerBindingsFile,
    [string]$HostRuntimeConfigurationFile
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = 'fortuneforgegame'
$region = 'us-east4'
$serviceName = 'fortuneforge-api'
$baseUrl = 'https://fortuneforgegame.web.app'
$sharedBackendManifestPath = Join-Path $repoRoot 'config/backend-packages.lock.json'
$sharedBackend = Test-Path -LiteralPath $sharedBackendManifestPath -PathType Leaf
Import-Module (Join-Path $PSScriptRoot 'ReleaseGuardrails.psm1') -Force
Assert-ReleaseTestWaiver -Target $Target -SkipTestSuites:$SkipTestSuites -TestWaiverReason $TestWaiverReason -LivePlaytest:$LivePlaytest -TestFree:$TestFree
if ($sharedBackend -and ($PSBoundParameters.ContainsKey('GameWorkerBindingsFile') -or $PSBoundParameters.ContainsKey('HostRuntimeConfigurationFile'))) {
    throw 'The committed shared backend composition cannot be combined with external worker bindings or runtime configuration.'
}
if ($sharedBackend -and $ContinuousAudit) { throw 'Shared backend releases use request-based CPU and scale to zero; -ContinuousAudit is incompatible.' }
if ($ContinuousAudit -and $Target -in @('hosting', 'site')) { throw '-ContinuousAudit applies only to an API release.' }
if ($PSBoundParameters.ContainsKey('GameWorkerBindingsFile') -and $Target -in @('hosting', 'site')) {
    throw '-GameWorkerBindingsFile applies only to a release that includes the API.'
}
if ($PSBoundParameters.ContainsKey('HostRuntimeConfigurationFile') -and ($Target -in @('hosting', 'site') -or $InspectLive)) {
    throw '-HostRuntimeConfigurationFile applies only to an API release, not live inspection.'
}

function Read-GameWorkerBindings {
    param([string]$Path)

    try {
        $file = Get-Item -LiteralPath $Path -ErrorAction Stop
        if ($file -isnot [System.IO.FileInfo]) { throw 'Expected a bindings file.' }
        $json = [System.IO.File]::ReadAllText($file.FullName)
        if (-not $json.TrimStart().StartsWith('{')) { throw 'Expected a JSON object.' }
        $document = ConvertFrom-Json -InputObject $json -ErrorAction Stop
    }
    catch { throw 'Could not read the game worker bindings JSON object.' }

    $keys = @($document.PSObject.Properties.Name)
    if ($keys.Count -ne 1 -or $keys[0] -cne 'bindings' -or
        $document.bindings -isnot [array] -or $document.bindings.Count -eq 0) {
        throw 'Game worker bindings require a nonempty bindings array.'
    }

    $gameIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $origins = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $index = 0
    foreach ($binding in $document.bindings) {
        $index++
        $names = @($binding.PSObject.Properties.Name)
        if ($binding -isnot [pscustomobject] -or $names.Count -ne 3 -or
            $names -cnotcontains 'gameId' -or $names -cnotcontains 'url' -or $names -cnotcontains 'signingSecret') {
            throw "Game worker binding $index requires only gameId, url, and signingSecret."
        }
        if ($binding.gameId -isnot [string] -or $binding.gameId.Length -gt 128 -or
            $binding.gameId -cnotmatch '\A[a-z0-9]+(?:-[a-z0-9]+)*\z') {
            throw "Game worker binding $index has an invalid runtime game ID."
        }
        if ($binding.url -isnot [string] -or $binding.url.Length -gt 253 -or
            $binding.url -cnotmatch '\Ahttps://(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+run\.app/?\z') {
            throw "Game worker binding $index requires a Cloud Run HTTPS origin."
        }
        if ($binding.signingSecret -isnot [string] -or
            $binding.signingSecret -cnotmatch '\A[A-Za-z0-9_-]{1,255}:[1-9][0-9]{0,18}\z') {
            throw "Game worker binding $index requires a same-project secret name with a pinned numeric version."
        }

        $origin = $binding.url.TrimEnd('/')
        if (-not $gameIds.Add($binding.gameId) -or -not $origins.Add($origin)) {
            throw "Game worker binding $index duplicates a runtime game ID or worker origin."
        }
        [pscustomobject]@{ gameId = $binding.gameId; url = $origin; signingSecret = $binding.signingSecret }
    }
}

function ConvertFrom-HostRuntimeConfiguration {
    param([string]$Json, [object[]]$Bindings, [string]$Repository)

    try { $document = ConvertFrom-Json -InputObject $Json -ErrorAction Stop }
    catch { throw 'Host runtime configuration must be a valid JSON object.' }
    $names = @($document.PSObject.Properties.Name)
    if ($document -isnot [pscustomobject] -or $names.Count -ne 2 -or
        $names -cnotcontains 'environment' -or $names -cnotcontains 'secrets' -or
        $document.environment -isnot [pscustomobject] -or $document.secrets -isnot [pscustomobject]) {
        throw 'Host runtime configuration requires only environment and secrets objects.'
    }
    $properties = @($document.environment.PSObject.Properties) + @($document.secrets.PSObject.Properties)
    # ConvertFrom-Json can collapse duplicate names. This flat, scalar schema
    # accepts only literal ASCII property names, so count the raw JSON names too.
    $rawNames = [regex]::Matches($Json, '"(?<name>(?:[^"\\]|\\.)*)"\s*:')
    if ($rawNames.Count -ne 2 + $properties.Count -or
        @($rawNames | Where-Object { $_.Groups['name'].Value -cnotmatch '\A[A-Za-z0-9_-]+\z' }).Count -gt 0 -or
        $properties.Count -eq 0 -or $Bindings.Count -eq 0) {
        throw 'Host runtime configuration requires unique literal setting names and reviewed worker bindings.'
    }
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $secretNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($binding in $Bindings) {
        [void]$ids.Add($binding.gameId)
        if (-not $secretNames.Add(($binding.signingSecret -split ':', 2)[0])) {
            throw 'Worker, wallet, batch and roster signing secrets must use separate secret names.'
        }
    }
    # The host owns these setting names. Read the registry and Arcane's explicit
    # route switch rather than maintaining a second list of slot identifiers.
    $slotSettings = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in @('FortuneForge.Server/GameWorkers/PreparedSlotWorkerRegistry.cs', 'FortuneForge.Server/Controllers/SlotsController.cs')) {
        $text = [System.IO.File]::ReadAllText((Join-Path $Repository $source))
        foreach ($match in [regex]::Matches($text, '"(?<setting>Slots:[A-Za-z]+WorkerRoundsEnabled)"')) {
            [void]$slotSettings.Add($match.Groups['setting'].Value.Replace(':', '__'))
        }
    }
    if ($slotSettings.Count -eq 0) { throw 'Cannot read the authoritative host slot switches.' }
    $environment = [ordered]@{}
    $secrets = [ordered]@{}
    $used = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $gamePattern = '[a-z0-9]+(?:-[a-z0-9]+)*'
    foreach ($property in $document.environment.PSObject.Properties) {
        $name = $property.Name
        $id = $null
        $allowed = $slotSettings.Contains($name)
        if ($name -cmatch "\AGameWorkers__(?<id>$gamePattern)__(?<flag>ForwardPublicRoutes|ForwardFreeRoutes|ForwardPaidCompetitionRoutes)\z") {
            $id = $Matches.id
            $allowed = $Matches.flag -ceq 'ForwardPublicRoutes' -or $id -cin @('asteroids', 'flappy', 'horse-flight')
        }
        elseif ($name -cmatch "\AGameWallet__Games__(?<id>$gamePattern)__(LegacySlotLedger|AllowLegacyFreeGames|AllowAccountBalanceRead|AllowCashWagerWithLegacyFreeGame|AllowWagerReservations|AllowPlayerNameRead|AllowMultiAccountBatch|AllowArcadeCompetition)\z" -or
            $name -cmatch "\AManagedPlayerRoster__Games__(?<id>$gamePattern)__Enabled\z") {
            $id = $Matches.id
            $allowed = $true
        }
        if (-not $allowed -or ($id -and -not $ids.Contains($id)) -or $property.Value -isnot [bool] -or -not $used.Add($name)) {
            throw 'Host runtime environment settings must be unique, approved boolean switches for bound runtime game IDs.'
        }
        $environment[$name] = $property.Value.ToString().ToLowerInvariant()
    }
    foreach ($property in $document.secrets.PSObject.Properties) {
        $name = $property.Name
        $allowed = $name -cmatch "\AGameWallet__Games__(?<id>$gamePattern)__(SigningKey|BatchSigningKey)\z" -or
            $name -cmatch "\AManagedPlayerRoster__Games__(?<id>$gamePattern)__SigningKey\z"
        if (-not $allowed -or -not $ids.Contains($Matches.id) -or -not $used.Add($name) -or
            $property.Value -isnot [string] -or $property.Value -cnotmatch '\A[A-Za-z0-9_-]{1,255}:[1-9][0-9]{0,18}\z') {
            throw 'Host runtime secrets require approved per-game names and same-project pinned numeric secret references.'
        }
        if (-not $secretNames.Add(($property.Value -split ':', 2)[0])) {
            throw 'Worker, wallet, batch and roster signing secrets must use separate secret names.'
        }
        $secrets[$name] = $property.Value
    }
    [pscustomobject]@{ Environment = $environment; Secrets = $secrets }
}

function Get-DeploymentInputHash {
    param([System.IO.Stream]$Stream)
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        $Stream.Position = 0
        ([BitConverter]::ToString($hash.ComputeHash($Stream))).Replace('-', '').ToLowerInvariant()
    }
    finally { $Stream.Position = 0; $hash.Dispose() }
}

function Read-HostRuntimeConfiguration {
    param([string]$Path, [object[]]$Bindings, [string]$Repository)
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file -isnot [System.IO.FileInfo] -or $file.Length -gt 262144 -or
        ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'Host runtime configuration requires a regular JSON file of at most 256 KiB.'
    }
    $stream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        if ($stream.Length -gt 262144) { throw 'Host runtime configuration exceeds 256 KiB.' }
        $digest = Get-DeploymentInputHash $stream
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false, $true), $true, 4096, $true)
        try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $configuration = ConvertFrom-HostRuntimeConfiguration -Json $json -Bindings $Bindings -Repository $Repository
        $configuration | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName
        $configuration | Add-Member -NotePropertyName Sha256 -NotePropertyValue $digest
        return $configuration
    }
    finally { $stream.Dispose() }
}

function Assert-HostRuntimeConfigurationUnchanged {
    param($Configuration, [System.IO.Stream]$LockedStream)
    if ($Configuration -and ((Get-DeploymentInputHash $LockedStream) -cne $Configuration.Sha256 -or
        (Get-FileHash -LiteralPath $Configuration.Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Configuration.Sha256)) {
        throw 'Host runtime configuration changed after preflight; publication is blocked.'
    }
}

function Enter-HostRuntimeConfigurationLock {
    param($Configuration)
    $file = Get-Item -LiteralPath $Configuration.Path -ErrorAction Stop
    if ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Host runtime configuration cannot be a reparse point.' }
    $stream = [System.IO.File]::Open($Configuration.Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try { Assert-HostRuntimeConfigurationUnchanged -Configuration $Configuration -LockedStream $stream; return $stream }
    catch { $stream.Dispose(); throw }
}

function Get-ApiWorkerDeployFlags {
    param([string]$SourceSha, [object[]]$Bindings, $RuntimeConfiguration, $SharedPackages, $LegacyWorkerConfiguration)

    $variables = @("FF_RELEASE_SHA=$SourceSha")
    $secretReferences = @()
    if ($SharedPackages) {
        if ($Bindings.Count -gt 0 -or $RuntimeConfiguration) { throw 'Shared packages cannot use external game worker configuration.' }
        $variables += @('GamePackages__Required=true', 'GamePackages__Directory=/app/game-packages',
            "GamePackages__ManifestSha256=$($SharedPackages.CompositionSha256)")
    }
    foreach ($binding in $Bindings) {
        $prefix = "GameWorkers__$($binding.gameId)__"
        $variables += "${prefix}Url=$($binding.url)"
        $secretReferences += "${prefix}SigningKey=$($binding.signingSecret)"
    }
    if ($RuntimeConfiguration) {
        foreach ($name in $RuntimeConfiguration.Environment.Keys) { $variables += "$name=$($RuntimeConfiguration.Environment[$name])" }
        foreach ($name in $RuntimeConfiguration.Secrets.Keys) { $secretReferences += "$name=$($RuntimeConfiguration.Secrets[$name])" }
    }
    "--update-env-vars=$($variables -join ',')"
    if ($secretReferences.Count -gt 0) { "--update-secrets=$($secretReferences -join ',')" }
    if ($SharedPackages -and $LegacyWorkerConfiguration) {
        if ($LegacyWorkerConfiguration.Environment.Count -gt 0) { "--remove-env-vars=$($LegacyWorkerConfiguration.Environment -join ',')" }
        if ($LegacyWorkerConfiguration.Secrets.Count -gt 0) { "--remove-secrets=$($LegacyWorkerConfiguration.Secrets -join ',')" }
    }
}

function Get-ApiLegacyWorkerConfiguration {
    param($Revision)
    $environment = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $secrets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($variable in @($Revision.spec.containers[0].env)) {
        if ($variable.name -imatch '\AGameWorkers__[a-z0-9]+(?:-[a-z0-9]+)*__(Url|SigningKey)\z') {
            if ($variable.valueFrom.secretKeyRef) { [void]$secrets.Add($variable.name) }
            else { [void]$environment.Add($variable.name) }
        }
    }
    [pscustomobject]@{ Environment = [string[]]@($environment); Secrets = [string[]]@($secrets) }
}

function Get-ApiCandidateObject {
    param($Parent, [string]$Name)
    if (-not $Parent.$Name) { $Parent | Add-Member -NotePropertyName $Name -NotePropertyValue ([pscustomobject]@{}) -Force }
    if ($Parent.$Name -isnot [pscustomobject]) { throw 'Unexpected Cloud Run service object shape.' }
    return $Parent.$Name
}

function ConvertTo-SharedBackendCandidateService {
    param($Service, [string]$ServiceName, [string]$ServingRevision, [string]$SourceSha, [string]$Image, $Packages)
    Assert-ReleaseSha $SourceSha
    if (-not $Packages -or $Packages.CompositionSha256 -cnotmatch '\A[a-f0-9]{64}\z' -or
        $Image -cnotmatch '\A[a-z0-9][a-z0-9./:_-]*@sha256:[a-f0-9]{64}\z' -or
        $Service.metadata.name -cne $ServiceName -or [string]::IsNullOrWhiteSpace($Service.metadata.resourceVersion) -or
        (Get-ServingApiRevision $Service) -cne $ServingRevision -or @($Service.spec.template.spec.containers).Count -ne 1) {
        throw 'Shared backend replacement requires the verified image, composition, current service version and single serving revision.'
    }
    $candidate = $Service | ConvertTo-Json -Depth 60 | ConvertFrom-Json
    $candidate.PSObject.Properties.Remove('status')
    foreach ($name in @('uid', 'creationTimestamp', 'generation', 'selfLink', 'managedFields')) { $candidate.metadata.PSObject.Properties.Remove($name) }
    $candidate.spec.template.metadata.PSObject.Properties.Remove('name')
    foreach ($metadata in @($candidate.metadata, $candidate.spec.template.metadata)) {
        $labels = Get-ApiCandidateObject $metadata 'labels'
        $labels | Add-Member -NotePropertyName 'ff-source-sha' -NotePropertyValue $SourceSha -Force
    }
    $serviceAnnotations = Get-ApiCandidateObject $candidate.metadata 'annotations'
    $serviceAnnotations | Add-Member -NotePropertyName 'run.googleapis.com/minScale' -NotePropertyValue '0' -Force
    $revisionAnnotations = Get-ApiCandidateObject $candidate.spec.template.metadata 'annotations'
    foreach ($pair in @(@('autoscaling.knative.dev/minScale', '0'), @('autoscaling.knative.dev/maxScale', '1'), @('run.googleapis.com/cpu-throttling', 'true'))) {
        $revisionAnnotations | Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1] -Force
    }
    $container = $candidate.spec.template.spec.containers[0]
    $container.image = $Image
    $replaced = @('FF_RELEASE_SHA', 'GamePackages__Required', 'GamePackages__Directory', 'GamePackages__ManifestSha256')
    $environment = @($container.env | Where-Object {
        $_ -and $_.name -inotmatch '\AGameWorkers__[a-z0-9]+(?:-[a-z0-9]+)*__(Url|SigningKey)\z' -and $_.name -notin $replaced
    })
    $environment += @([pscustomobject]@{ name = 'FF_RELEASE_SHA'; value = $SourceSha },
        [pscustomobject]@{ name = 'GamePackages__Required'; value = 'true' },
        [pscustomobject]@{ name = 'GamePackages__Directory'; value = '/app/game-packages' },
        [pscustomobject]@{ name = 'GamePackages__ManifestSha256'; value = $Packages.CompositionSha256 })
    $container | Add-Member -NotePropertyName env -NotePropertyValue $environment -Force
    $container | Add-Member -NotePropertyName startupProbe -NotePropertyValue ([pscustomobject]@{
        httpGet = [pscustomobject]@{ path = '/healthz'; port = 8080 }; timeoutSeconds = 5; periodSeconds = 5; failureThreshold = 48
    }) -Force
    $traffic = @([pscustomobject]@{ revisionName = $ServingRevision; percent = 100; latestRevision = $false })
    $tags = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($original in @($Service.spec.traffic | Where-Object { $_.tag })) {
        if ($original.tag -cnotmatch '\A[a-z][a-z0-9-]{0,62}\z' -or -not $tags.Add($original.tag)) { throw 'Invalid existing Cloud Run traffic tag.' }
        $revision = [string]$original.revisionName
        if (-not $revision -or $original.latestRevision -eq $true) {
            $resolved = @($Service.status.traffic | Where-Object { $_.tag -ceq $original.tag -and $_.revisionName })
            if ($resolved.Count -ne 1) { throw 'Cannot preserve an unresolved Cloud Run traffic tag.' }
            $revision = [string]$resolved[0].revisionName
        }
        if (-not $revision.StartsWith($ServiceName + '-', [StringComparison]::Ordinal) -or $revision -cnotmatch '\A[a-z0-9-]+\z') { throw 'Existing traffic tag does not name this service revision.' }
        $traffic += [pscustomobject]@{ tag = $original.tag; revisionName = $revision; percent = 0; latestRevision = $false }
    }
    $candidate.spec | Add-Member -NotePropertyName traffic -NotePropertyValue $traffic -Force
    return $candidate
}

function New-ApiServiceReplaceContext {
    param($Service, [string]$ServiceName, [string]$ServingRevision, [string]$SourceSha, [string]$Image, $Packages, [string]$RecordPath)
    $candidate = ConvertTo-SharedBackendCandidateService -Service $Service -ServiceName $ServiceName -ServingRevision $ServingRevision -SourceSha $SourceSha -Image $Image -Packages $Packages
    $path = $RecordPath + '.api-service.json'
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($candidate | ConvertTo-Json -Depth 60))
    $writer = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $writer.Write($bytes, 0, $bytes.Length) } finally { $writer.Dispose() }
    $expected = [IO.MemoryStream]::new($bytes, $false)
    try { $digest = Get-DeploymentInputHash $expected } finally { $expected.Dispose() }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $context = [pscustomobject]@{ Path = $path; Sha256 = $digest; Stream = $stream; ResourceVersion = [string]$Service.metadata.resourceVersion }
    try { Assert-HostRuntimeConfigurationUnchanged -Configuration $context -LockedStream $stream; return $context }
    catch { $stream.Dispose(); throw }
}

function Assert-ApiServiceFieldSubset {
    param($Expected, $Actual)
    if ($Expected -is [pscustomobject]) {
        foreach ($property in $Expected.PSObject.Properties) {
            $found = @($Actual.PSObject.Properties | Where-Object { $_.Name -ceq $property.Name })
            if ($found.Count -ne 1) { throw 'Cloud Run response omitted a reviewed candidate configuration field.' }
            Assert-ApiServiceFieldSubset -Expected $property.Value -Actual $found[0].Value
        }
    }
    elseif ($Expected -is [array]) {
        $actualItems = @($Actual)
        if ($Expected.Count -ne $actualItems.Count) { throw 'Cloud Run response changed a reviewed candidate configuration collection.' }
        $expectedItems = @($Expected)
        if ($Expected.Count -gt 0 -and @($Expected | Where-Object { $_ -isnot [pscustomobject] -or -not $_.name }).Count -eq 0) {
            $expectedItems = @($Expected | Sort-Object name -CaseSensitive)
            $actualItems = @($actualItems | Sort-Object name -CaseSensitive)
        }
        for ($i = 0; $i -lt $expectedItems.Count; $i++) { Assert-ApiServiceFieldSubset -Expected $expectedItems[$i] -Actual $actualItems[$i] }
    }
    elseif ($Expected -cne $Actual) { throw 'Cloud Run response changed a reviewed candidate configuration value.' }
}

function Assert-SharedBackendCandidateService {
    param($Service, $Expected, [string]$SourceSha, [string]$Image, [string]$ServingRevision, $Packages)
    if ($Service.metadata.name -cne $Expected.metadata.name -or
        $Service.metadata.labels.'ff-source-sha' -cne $SourceSha -or
        $Service.spec.template.metadata.labels.'ff-source-sha' -cne $SourceSha -or
        (Get-ApiRevisionSourceSha $Service.spec.template) -cne $SourceSha -or
        $Service.spec.template.spec.containers[0].image -cne $Image) { throw 'Cloud Run response differs from the reviewed candidate identity.' }
    Assert-ApiServiceFieldSubset -Expected $Expected.spec.template.spec -Actual $Service.spec.template.spec
    $actualTraffic = @($Service.spec.traffic)
    if ($actualTraffic.Count -ne @($Expected.spec.traffic).Count) { throw 'Cloud Run changed the reviewed public traffic or tag configuration.' }
    foreach ($entry in @($Expected.spec.traffic)) {
        $matches = @($actualTraffic | Where-Object { [string]$_.tag -ceq [string]$entry.tag -and $_.revisionName -ceq $entry.revisionName })
        # REST may omit protobuf defaults (false and zero). Both forms still
        # bind a named revision and preserve the reviewed zero-percent tags.
        if ($matches.Count -ne 1 -or [int]$matches[0].percent -ne [int]$entry.percent -or $matches[0].latestRevision -eq $true) {
            throw 'Cloud Run changed the reviewed named revision traffic allocation.'
        }
    }
    Assert-ApiSharedBackendConfiguration -Revision $Service.spec.template -Packages $Packages -Service $Service -TemplateOnly
    $serving = @($Service.spec.traffic | Where-Object { $_.percent -gt 0 })
    if ($serving.Count -ne 1 -or $serving[0].revisionName -cne $ServingRevision -or $serving[0].percent -ne 100 -or $serving[0].latestRevision -eq $true) {
        throw 'Candidate preparation must keep the original named revision serving all public traffic.'
    }
}

function Invoke-ApiServiceReplaceRequest {
    param($Context, [string]$Endpoint, [string]$Account, [switch]$DryRun)
    if ($Endpoint -cnotmatch '\Ahttps://[a-z0-9-]+-run\.googleapis\.com/apis/serving\.knative\.dev/v1/namespaces/[a-z0-9-]+/services/[a-z0-9-]+\z') { throw 'Unexpected regional Cloud Run replacement endpoint.' }
    Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
    Assert-HostRuntimeConfigurationUnchanged -Configuration $Context -LockedStream $Context.Stream
    $token = $null
    $headers = $null
    $bytes = $null
    try {
        $token = ([string](& gcloud.cmd auth print-access-token "--account=$Account" --quiet 2>$null)).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'Could not obtain the configured release principal token.' }
        $bytes = [byte[]]::new([int]$Context.Stream.Length)
        $Context.Stream.Position = 0
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $Context.Stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) { throw 'The locked candidate service payload ended unexpectedly.' }
            $offset += $read
        }
        $Context.Stream.Position = 0
        $headers = @{ Authorization = 'Bearer ' + $token }
        $uri = $Endpoint + $(if ($DryRun) { '?dryRun=all' } else { '' })
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        Assert-HostRuntimeConfigurationUnchanged -Configuration $Context -LockedStream $Context.Stream
        try { $response = Invoke-RestMethod -Uri $uri -Method Put -ContentType 'application/json' -Headers $headers -Body $bytes -TimeoutSec 90 -ErrorAction Stop }
        catch {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            throw [InvalidOperationException]::new("Cloud Run replacement request failed (HTTP $status); the mutation will not be retried.")
        }
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        Assert-HostRuntimeConfigurationUnchanged -Configuration $Context -LockedStream $Context.Stream
        return $response
    }
    finally {
        if ($headers) { $headers.Clear() }
        $token = $null
        if ($bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    }
}

function Remove-SharedBackendCandidateTag {
    param([string]$Tag, [string]$Revision, [hashtable]$Review, [hashtable]$TaggedReview)
    $current = Get-CloudRunService
    $owned = @($current.spec.traffic | Where-Object { $_.tag -ceq $Tag })
    if ($owned.Count -eq 0) { Assert-SharedBackendCandidateService -Service $current @Review; return }
    if ($owned.Count -ne 1 -or $owned[0].revisionName -cne $Revision -or [int]$owned[0].percent -ne 0 -or $owned[0].latestRevision -eq $true) {
        throw 'Candidate tag ownership changed; refusing automatic cleanup.'
    }
    Assert-SharedBackendCandidateService -Service $current @TaggedReview
    Invoke-Checked -Executable 'gcloud.cmd' -Arguments @('run', 'services', 'update-traffic', $serviceName,
        "--remove-tags=$Tag", '--region', $region, '--project', $project, '--quiet') -WorkingDirectory $releaseCheckout
    Assert-SharedBackendCandidateService -Service (Get-CloudRunService) @Review
}

function Wait-SharedBackendCandidateReady {
    param([string]$ServingRevision, [string]$PreviousRevision, [string]$SourceSha, [string]$Image, $Packages, [string]$RecordPath, $ExpectedService)
    $deadline = [DateTime]::UtcNow.AddSeconds(360)
    $tag = 'ff-candidate-' + $SourceSha.Substring(0, 8) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $review = @{ Expected = $ExpectedService; SourceSha = $SourceSha; Image = $Image; ServingRevision = $ServingRevision; Packages = $Packages }
    $attempted = $false
    $complete = $false
    $state = [ordered]@{ tag = $tag; revision = ''; cleanupCompleted = $false; cleanupPending = $false }
    try { do {
        $service = Get-CloudRunService
        if ((Get-ServingApiRevision $service) -cne $ServingRevision) { throw 'API traffic changed while the candidate was starting.' }
        $name = [string]$service.status.latestCreatedRevisionName
        $ready = $false
        if ($name -and $name -cne $ServingRevision -and $name -cne $PreviousRevision) {
            $revision = Get-ApiRevision $name
            if ((Get-ApiRevisionSourceSha $revision) -cne $SourceSha) { throw 'Another API candidate replaced the selected source while waiting for readiness.' }
            Assert-ApiRevisionImage -Revision $revision -ExpectedImage $Image
            Assert-ApiSharedBackendConfiguration -Revision $revision -Packages $Packages -Service $service -TemplateOnly
            if (-not $attempted) {
                Assert-SharedBackendCandidateService -Service $service @review
                if (@($service.spec.traffic | Where-Object { $_.tag -ceq $tag }).Count -gt 0) { throw 'Candidate activation tag already exists.' }
                $tagged = $ExpectedService | ConvertTo-Json -Depth 60 | ConvertFrom-Json
                $tagged.spec.traffic = @($tagged.spec.traffic) + [pscustomobject]@{ tag = $tag; revisionName = $name; percent = 0; latestRevision = $false }
                $taggedReview = $review.Clone()
                $taggedReview.Expected = $tagged
                $state.revision = $name
                $attempted = $true
                # A zero-traffic, untagged min-zero revision can remain retired.
                # Add only our unique tag; never replace the service's other tags.
                Invoke-Checked -Executable 'gcloud.cmd' -Arguments @('run', 'services', 'update-traffic', $serviceName,
                    "--update-tags=$tag=$name", '--region', $region, '--project', $project, '--quiet') -WorkingDirectory $releaseCheckout
            }
            if ($state.revision -cne $name) { throw 'Another API candidate appeared during tagged startup verification.' }
            Assert-SharedBackendCandidateService -Service (Get-CloudRunService) @taggedReview
            $ready = $service.status.latestReadyRevisionName -ceq $name -and
                @($revision.status.conditions | Where-Object { $_.type -ceq 'Ready' -and $_.status -ceq 'True' }).Count -eq 1
        }
        [IO.File]::WriteAllText(($RecordPath + '.api-readiness.json'), ([ordered]@{
            checkedAtUtc = [DateTime]::UtcNow.ToString('o'); candidateRevision = $name;
            servingRevision = $ServingRevision; ready = $ready
        } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        if ($ready) { $complete = $true; return $name }
        Start-Sleep -Seconds 5
    } while ([DateTime]::UtcNow -lt $deadline)
        throw 'The exact shared backend candidate did not become ready within 360 seconds; public traffic remains on the prior revision.'
    }
    finally {
        if ($attempted) {
            try {
                Remove-SharedBackendCandidateTag -Tag $tag -Revision $state.revision -Review $review -TaggedReview $taggedReview
                $state.cleanupCompleted = $true
            }
            catch {
                $state.cleanupPending = $true
                if ($complete) { throw }
                Write-Warning 'Owned candidate tag cleanup could not be confirmed; public traffic was not promoted. Inspect the candidate-tag receipt.'
            }
            finally { [IO.File]::WriteAllText(($RecordPath + '.api-candidate-tag.json'), ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false)) }
        }
    }
}

function New-ApiRuntimeFlagsContext {
    param([string]$SourceSha, [object[]]$Bindings, $RuntimeConfiguration, [string]$RecordPath, $SharedPackages, $LegacyWorkerConfiguration)
    # A catalogue-sized update exceeds cmd.exe's command-line limit. gcloud's
    # JSON flags file carries the same validated dictionary values without
    # exposing raw signing keys or relying on shell quoting/argument length.
    $flags = [ordered]@{}
    foreach ($argument in @(Get-ApiWorkerDeployFlags -SourceSha $SourceSha -Bindings $Bindings -RuntimeConfiguration $RuntimeConfiguration -SharedPackages $SharedPackages -LegacyWorkerConfiguration $LegacyWorkerConfiguration)) {
        $name, $value = $argument -split '=', 2
        $flags[$name] = $value
    }
    $path = $RecordPath + '.api-runtime.flags.json'
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($flags | ConvertTo-Json -Depth 4))
    $writer = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $writer.Write($bytes, 0, $bytes.Length) } finally { $writer.Dispose() }
    $expected = [System.IO.MemoryStream]::new($bytes, $false)
    try { $digest = Get-DeploymentInputHash $expected } finally { $expected.Dispose() }
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $context = [pscustomobject]@{ Path = $path; Sha256 = $digest; Stream = $stream }
    try { Assert-HostRuntimeConfigurationUnchanged -Configuration $context -LockedStream $stream; return $context }
    catch { $stream.Dispose(); throw }
}

function Assert-ApiHostRuntimeConfiguration {
    param($Revision, $Configuration)
    if (-not $Configuration) { return }
    $variables = @($Revision.spec.containers[0].env)
    foreach ($name in $Configuration.Environment.Keys) {
        $found = @($variables | Where-Object { $_.name -ieq $name })
        if ($found.Count -ne 1 -or $found[0].name -cne $name -or $found[0].value -cne $Configuration.Environment[$name] -or $found[0].valueFrom) {
            throw 'Candidate API revision does not match the reviewed runtime switches. Traffic was not changed.'
        }
    }
    foreach ($name in $Configuration.Secrets.Keys) {
        $found = @($variables | Where-Object { $_.name -ieq $name })
        $secretName, $version = $Configuration.Secrets[$name] -split ':', 2
        if ($found.Count -ne 1 -or $found[0].name -cne $name -or $found[0].value -or
            $found[0].valueFrom.secretKeyRef.name -cne $secretName -or [string]$found[0].valueFrom.secretKeyRef.key -cne $version) {
            throw 'Candidate API revision does not match the reviewed signing secret references. Traffic was not changed.'
        }
    }
}

function Assert-ApiSharedBackendConfiguration {
    param($Revision, $Packages, $Service, [switch]$TemplateOnly)
    if (-not $Packages) { return }
    $legacy = Get-ApiLegacyWorkerConfiguration $Revision
    if ($legacy.Environment.Count -gt 0 -or $legacy.Secrets.Count -gt 0) { throw 'Candidate shared backend retains external worker URL or signing-key bindings. Traffic was not changed.' }
    $expected = [ordered]@{ GamePackages__Required = 'true'; GamePackages__Directory = '/app/game-packages'; GamePackages__ManifestSha256 = $Packages.CompositionSha256 }
    Assert-ApiHostRuntimeConfiguration -Revision $Revision -Configuration ([pscustomobject]@{ Environment = $expected; Secrets = [ordered]@{} })
    $annotations = $Revision.metadata.annotations
    if ($Service.metadata.annotations.'run.googleapis.com/minScale' -and $Service.metadata.annotations.'run.googleapis.com/minScale' -cne '0') {
        throw 'Shared backend service-level minimum scaling must be zero. Traffic was not changed.'
    }
    if ($annotations.'autoscaling.knative.dev/maxScale' -cne '1' -or
        ($annotations.'autoscaling.knative.dev/minScale' -and $annotations.'autoscaling.knative.dev/minScale' -cne '0') -or
        ($annotations.'run.googleapis.com/cpu-throttling' -and $annotations.'run.googleapis.com/cpu-throttling' -cne 'true')) {
        throw 'Candidate shared backend must use request-based CPU with zero minimum and one maximum instance. Traffic was not changed.'
    }
    $probe = $Revision.spec.containers[0].startupProbe
    if ($probe.httpGet.path -cne '/healthz' -or $probe.httpGet.port -ne 8080 -or $probe.timeoutSeconds -ne 5 -or
        $probe.periodSeconds -ne 5 -or $probe.failureThreshold -ne 48 -or
        (-not $TemplateOnly -and @($Revision.status.conditions | Where-Object { $_.type -ceq 'Ready' -and $_.status -ceq 'True' }).Count -ne 1)) {
        throw 'Candidate shared backend has not passed its package-readiness HTTP startup probe. Traffic was not changed.'
    }
}

function Enter-SharedBackendUploadLocks {
    param([string]$Checkout, $Composition, [string]$CompositionSha256, [string]$ManifestSha256)
    if ($CompositionSha256 -cnotmatch '\A[a-f0-9]{64}\z' -or $ManifestSha256 -cnotmatch '\A[a-f0-9]{64}\z') {
        throw 'Shared backend composition requires verified SHA256 identities.'
    }
    $prefix = 'artifacts/shared-backend-upload/game-packages'
    $directory = Join-Path $Checkout $prefix
    $expected = [System.Collections.Generic.List[object]]::new()
    $compositionPath = Join-Path $directory 'composition.json'
    $expected.Add([pscustomobject]@{ Path = "$prefix/composition.json"; Size = (Get-Item -LiteralPath $compositionPath).Length; Sha256 = $CompositionSha256 })
    foreach ($package in $Composition.packages) {
        $release = [guid]::Empty
        if ($package.runtimeGameId -cnotmatch '\A[a-z0-9]+(?:-[a-z0-9]+)*\z' -or -not [guid]::TryParseExact([string]$package.releaseId, 'D', [ref]$release)) {
            throw 'Unsafe selected backend package identity.'
        }
        foreach ($file in $package.files) {
            if ($file.path -cnotmatch '\A[A-Za-z0-9_-][A-Za-z0-9_.-]*(?:/[A-Za-z0-9_-][A-Za-z0-9_.-]*)*\z' -or
                $file.sha256 -cnotmatch '\A[a-f0-9]{64}\z' -or $file.size -lt 0) { throw 'Unsafe selected backend artifact.' }
            $expected.Add([pscustomobject]@{ Path = "$prefix/$($package.runtimeGameId)/$($package.releaseId)/$($file.path)"; Size = $file.size; Sha256 = $file.sha256 })
        }
    }
    $streams = [System.Collections.Generic.List[System.IO.FileStream]]::new()
    $entries = [System.Collections.Generic.List[object]]::new()
    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($file in $expected) {
            if (-not $paths.Add($file.Path)) { throw 'Duplicate shared backend upload path.' }
            $absolute = Join-Path $Checkout $file.Path
            $item = Get-Item -LiteralPath $absolute -ErrorAction Stop
            if ($item -isnot [System.IO.FileInfo]) { throw 'Expected a regular shared backend artifact.' }
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Shared backend upload paths cannot contain reparse points.' }
            $parent = $item.Directory
            while ($parent -and $parent.FullName.TrimEnd('\') -ine $Checkout.TrimEnd('\')) {
                if ($parent.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Shared backend upload paths cannot contain redirected ancestors.' }
                $parent = $parent.Parent
            }
            $stream = [System.IO.File]::Open($absolute, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $streams.Add($stream)
            if ($stream.Length -ne $file.Size -or (Get-DeploymentInputHash $stream) -cne $file.Sha256) { throw 'Shared backend artifact differs from the committed package selection.' }
            $entries.Add([pscustomobject]@{ Path = $file.Path; AbsolutePath = $absolute; Size = $file.Size; Sha256 = $file.Sha256; Stream = $stream })
        }
        $actual = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Force)
        if ($actual.Count -ne $expected.Count -or @($actual | Where-Object { -not $paths.Contains($_.FullName.Substring($Checkout.TrimEnd('\').Length + 1).Replace('\', '/')) }).Count -gt 0) {
            throw 'Shared backend materialization contains unselected files.'
        }
        $manifestLines = [string[]]@($entries | ForEach-Object { "$($_.Path)`t$($_.Size)`t$($_.Sha256)" })
        [Array]::Sort($manifestLines, [StringComparer]::Ordinal)
        return [pscustomobject]@{ Paths = [string[]]@($entries | ForEach-Object { $_.Path }); Entries = $entries;
            Lock = [pscustomobject]@{ Streams = $streams }; FileCount = $entries.Count;
            ManifestSha256 = $ManifestSha256; CompositionSha256 = $CompositionSha256;
            FileManifestSha256 = Get-ReleaseTextHash ($manifestLines -join "`n"); Directory = $directory }
    }
    catch { foreach ($stream in $streams) { $stream.Dispose() }; throw }
}

function Assert-SharedBackendUploadUnchanged {
    param($Packages)
    if (-not $Packages) { return }
    foreach ($file in $Packages.Entries) {
        if ($file.Stream.Length -ne $file.Size -or (Get-DeploymentInputHash $file.Stream) -cne $file.Sha256 -or
            (Get-FileHash -LiteralPath $file.AbsolutePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.Sha256) {
            throw 'Selected shared backend artifact changed during the guarded release.'
        }
    }
}

function New-SharedBackendUploadContext {
    param([string]$Checkout)
    $manifest = Join-Path $Checkout 'config/backend-packages.lock.json'
    $output = Join-Path $Checkout 'artifacts/shared-backend-upload'
    [System.IO.Directory]::CreateDirectory($output) | Out-Null
    $script = Join-Path $Checkout 'scripts/shared-backend-packages.mjs'
    if (-not (Test-Path -LiteralPath (Join-Path $output 'game-packages'))) {
        $null = Invoke-JsonCommand 'node.exe' @($script, 'materialize', '--manifest', $manifest, '--output', $output)
    }
    # Reuse is allowed only for the exact immutable selection. Stale output is
    # preserved and refused; no release silently replaces a prior artifact tree.
    $verified = Invoke-JsonCommand 'node.exe' @($script, 'verify-materialized', '--manifest', $manifest, '--output', $output)
    if ($verified.manifestSha256 -cne (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Backend package lock changed during materialization.'
    }
    $composition = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json -ErrorAction Stop
    Enter-SharedBackendUploadLocks -Checkout $Checkout -Composition $composition -CompositionSha256 $verified.compositionSha256 -ManifestSha256 $verified.manifestSha256
}

function Assert-ApiWorkerBindings {
    param($Revision, [object[]]$Bindings)

    foreach ($binding in $Bindings) {
        $prefix = "GameWorkers__$($binding.gameId)__"
        $variables = @($Revision.spec.containers[0].env)
        $url = @($variables | Where-Object { $_.name -ceq "${prefix}Url" })
        $key = @($variables | Where-Object { $_.name -ceq "${prefix}SigningKey" })
        $secretName, $secretVersion = $binding.signingSecret -split ':', 2
        if ($url.Count -ne 1 -or $url[0].value -cne $binding.url -or $key.Count -ne 1 -or
            $key[0].valueFrom.secretKeyRef.name -cne $secretName -or
            [string]$key[0].valueFrom.secretKeyRef.key -cne $secretVersion) {
            throw "The candidate API revision lacks the expected binding for $($binding.gameId). Traffic was not changed."
        }
    }
}

$gameWorkerBindings = @()
if ($PSBoundParameters.ContainsKey('GameWorkerBindingsFile')) {
    $gameWorkerBindings = @(Read-GameWorkerBindings -Path $GameWorkerBindingsFile)
}
$hostRuntimeConfiguration = $null
if ($PSBoundParameters.ContainsKey('HostRuntimeConfigurationFile')) {
    $hostRuntimeConfiguration = Read-HostRuntimeConfiguration -Path $HostRuntimeConfigurationFile -Bindings $gameWorkerBindings -Repository $repoRoot
}

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try {
        if ($hostRuntimeConfigurationLock) { Assert-HostRuntimeConfigurationUnchanged -Configuration $hostRuntimeConfiguration -LockedStream $hostRuntimeConfigurationLock }
        if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
        Write-Host "`n> $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE." }
        if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
        if ($hostRuntimeConfigurationLock) { Assert-HostRuntimeConfigurationUnchanged -Configuration $hostRuntimeConfiguration -LockedStream $hostRuntimeConfigurationLock }
    }
    finally { Pop-Location }
}

function Invoke-JsonCommand {
    param([string]$Executable, [string[]]$Arguments)
    if ($hostRuntimeConfigurationLock) { Assert-HostRuntimeConfigurationUnchanged -Configuration $hostRuntimeConfiguration -LockedStream $hostRuntimeConfigurationLock }
    if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable $($Arguments -join ' ') failed with code $LASTEXITCODE." }
    try { $result = (($output -join "`n") | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw "$Executable returned invalid JSON for $($Arguments -join ' ')." }
    if ($sourceLock) { Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha }
    if ($hostRuntimeConfigurationLock) { Assert-HostRuntimeConfigurationUnchanged -Configuration $hostRuntimeConfiguration -LockedStream $hostRuntimeConfigurationLock }
    return $result
}

function Get-ApiBaseUploadInventory {
    param([string]$Checkout, [string[]]$TrackedPaths)
    # The SDK selector preserves the committed .gcloudignore exclusions. Its
    # local-only metadata command does not contact a service or create files.
    $inventory = @(Invoke-JsonCommand 'gcloud.cmd' @('meta', 'list-files-for-upload', $Checkout, '--format=json'))
    Assert-ReleaseUploadPaths -TrackedPaths $TrackedPaths -UploadPaths $inventory
}

function New-ApiUploadContext {
    param([string]$Checkout, $LockedSource, [string]$RecordPath, $Packages)
    $trackedUpload = Get-ApiBaseUploadInventory -Checkout $Checkout -TrackedPaths $LockedSource.TrackedPaths
    $upload = $trackedUpload
    if ($Packages) {
        Assert-SharedBackendUploadUnchanged $Packages
        $upload = Assert-ReleaseUploadPaths -TrackedPaths @($LockedSource.TrackedPaths + $Packages.Paths) `
            -UploadPaths @($trackedUpload.Paths + $Packages.Paths)
    }
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
        Copy-LockedReleaseArtifact -ArtifactLock $original -Directory $directory
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

function New-SiteReleaseContext {
    param([string]$Checkout, [string]$CandidateSha, [string]$RecordPath, $BaselineHosting)
    Assert-ReleaseCheckout -Repository $Checkout -Sha $CandidateSha
    $contract = Get-ReleaseSiteContract -Repository $Checkout
    $original = Enter-ReleaseHostingLock -Directory (Join-Path $Checkout $contract.outputDirectory) -CandidateSha $CandidateSha -Scope site -SiteContract $contract
    $staged = $null
    try {
        $directory = $RecordPath + '.site'
        Copy-LockedReleaseArtifact -ArtifactLock $original -Directory $directory
        $staged = Enter-ReleaseHostingLock -Directory $directory -CandidateSha $CandidateSha -Scope site -SiteContract $contract
        if ($staged.FileCount -ne $original.FileCount -or $staged.ManifestSha256 -cne $original.ManifestSha256) { throw 'Staged site artifact bytes differ from the verified build manifest.' }

        function Add-SiteContextFile([string]$Path, [string]$Text) {
            $writer = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Text); $writer.Write($bytes, 0, $bytes.Length) }
            finally { $writer.Dispose() }
            $reader = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $staged.Streams.Add($reader)
            $hash = [System.Security.Cryptography.SHA256]::Create()
            try { $actual = ([System.BitConverter]::ToString($hash.ComputeHash($reader))).Replace('-', '').ToLowerInvariant() }
            finally { $hash.Dispose() }
            $reader.Position = 0
            if ($actual -cne (Get-ReleaseTextHash $Text)) { throw 'Generated site publication input changed before its immutable handle was acquired.' }
            return $actual
        }

        # The publisher selects exactly these locked files. Newly injected files
        # cannot silently join a release even though directory entries are mutable.
        $manifestPath = $RecordPath + '.site-manifest.json'
        $manifest = [ordered]@{ schemaVersion = 1; sourceSha = $CandidateSha; files = @($staged.Files | ForEach-Object {
            [ordered]@{ path = $_.Path; bytes = $_.Bytes; sha256 = $_.Sha256 }
        }) }
        $manifestHash = Add-SiteContextFile -Path $manifestPath -Text ($manifest | ConvertTo-Json -Depth 8)
        $markerPath = ''
        $markerHash = ''
        if (-not [string]::IsNullOrEmpty($BaselineHosting.MarkerText)) {
            $markerPath = $RecordPath + '.site-baseline-marker.json'
            $markerHash = Add-SiteContextFile -Path $markerPath -Text $BaselineHosting.MarkerText
        }
        return [pscustomobject]@{ Lock = $staged; Directory = $directory; ConfigSha256 = '';
            ManifestSha256 = $staged.ManifestSha256; FileCount = $staged.FileCount;
            ArtifactManifestPath = $manifestPath; ArtifactManifestSha256 = $manifestHash;
            BaselineMarkerPath = $markerPath; BaselineMarkerSha256 = $markerHash;
            PublisherRecordPath = $RecordPath + '.site-result.json' }
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
        MarkerText = $(if ($marker) { [string]$marker.Content } else { '' })
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
    if ($Target -in @('all', 'hosting', 'site')) {
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
    if ($Target -in @('all', 'hosting', 'site')) {
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
    if ($PreflightOnly -and $Target -in @('all', 'api')) {
        $preflightTrackedPaths = @(& git -C $repoRoot -c core.quotePath=false ls-files)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked paths for local source upload preflight.' }
        $baseUpload = Get-ApiBaseUploadInventory -Checkout $repoRoot -TrackedPaths $preflightTrackedPaths
        Assert-ReleaseCheckout -Repository $repoRoot -Sha $localPreflight.CandidateSha
        $localPreflight | Add-Member -NotePropertyName BaseUploadFileCount -NotePropertyValue $baseUpload.FileCount
        $localPreflight | Add-Member -NotePropertyName BaseUploadManifestSha256 -NotePropertyValue $baseUpload.ManifestSha256
    }
    if ($sharedBackend -and $Target -in @('all', 'api')) {
        $selected = Invoke-JsonCommand 'node.exe' @((Join-Path $repoRoot 'scripts/shared-backend-packages.mjs'), 'verify', '--manifest', $sharedBackendManifestPath)
        $localPreflight | Add-Member -NotePropertyName SharedBackendPackages -NotePropertyValue $selected
    }
    if ($PreflightOnly) {
        if ($gameWorkerBindings.Count -gt 0) {
            $localPreflight | Add-Member -NotePropertyName GameWorkerBindings -NotePropertyValue $gameWorkerBindings
        }
        if ($hostRuntimeConfiguration) {
            $localPreflight | Add-Member -NotePropertyName HostRuntimeConfiguration -NotePropertyValue $hostRuntimeConfiguration
        }
        $localPreflight
        return
    }
}

$hostingBefore = $null
$apiBefore = $null
$rulesBefore = $null
if ($Target -in @('all', 'hosting', 'site')) { $hostingBefore = Get-LiveHosting }
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
if ($Target -cin @('hosting', 'site')) {
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
$hostRuntimeConfigurationLock = $null
$apiRuntimeFlagsContext = $null
$apiServiceReplaceContext = $null
$sharedBackendPackages = $null
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
    apiBaseUploadFileCount = 0
    apiBaseUploadManifestSha256 = ''
    apiBuildId = ''
    apiBuildTag = ''
    apiImageDigest = ''
    apiImageReference = ''
    gameWorkerBindings = $gameWorkerBindings
    hostRuntimeConfiguration = $hostRuntimeConfiguration
    apiRuntimeFlagsSha256 = ''
    apiRuntimeFlagsPath = ''
    apiServiceReplaceSha256 = ''
    apiServiceReplacePath = ''
    apiServiceExpectedResourceVersion = ''
    sharedBackendManifestSha256 = ''
    sharedBackendCompositionSha256 = ''
    sharedBackendFileManifestSha256 = ''
    sharedBackendFileCount = 0
    sharedBackendRemovedWorkerEnvironment = @()
    sharedBackendRemovedWorkerSecrets = @()
    firestoreIndexPublicationSha256 = ''
    firestoreIndexConfigSha256 = ''
    firestorePublishedIndexCount = 0
    firestorePublishedFieldOverrideCount = 0
    hostingArtifactManifestSha256 = ''
    hostingArtifactFileCount = 0
    hostingConfigSha256 = ''
    siteArtifactManifestSha256 = ''
    siteBaselineMarkerSha256 = ''
    sitePublisherRecord = ''
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
    newHostingVersion = ''
    newApiRevision = ''
    newFirestoreRulesRelease = ''
    status = 'preflight'
}

try {
    $workspaceLock = Enter-ReleaseWorkspaceLock -Repository $releaseCheckout
    $sourceLock = Enter-ReleaseSourceLock -Repository $releaseCheckout -Sha $candidateSha
    if ($Target -in @('all', 'api')) {
        $baseUpload = Get-ApiBaseUploadInventory -Checkout $releaseCheckout -TrackedPaths $sourceLock.TrackedPaths
        $record.apiBaseUploadFileCount = $baseUpload.FileCount
        $record.apiBaseUploadManifestSha256 = $baseUpload.ManifestSha256
    }
    if ($hostRuntimeConfiguration) { $hostRuntimeConfigurationLock = Enter-HostRuntimeConfigurationLock -Configuration $hostRuntimeConfiguration }
    Assert-ReleaseToolingMatchesCommit -Repository $repoRoot -Sha $candidateSha -Paths $releaseToolPaths
    $record.sourceManifestSha256 = $sourceLock.ManifestSha256
    $record.sourceFileCount = $sourceLock.FileCount

    $recordDirectory = Join-Path $repoRoot 'artifacts\releases'
    [System.IO.Directory]::CreateDirectory($recordDirectory) | Out-Null
    $recordPath = Join-Path $recordDirectory (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')) + '-' + $candidateSha.Substring(0, 12) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    Write-ReleaseRecord -Record $record -Path $recordPath

    if ($sharedBackend -and $Target -in @('all', 'api')) {
        $sharedBackendPackages = New-SharedBackendUploadContext -Checkout $releaseCheckout
        $record.sharedBackendManifestSha256 = $sharedBackendPackages.ManifestSha256
        $record.sharedBackendCompositionSha256 = $sharedBackendPackages.CompositionSha256
        $record.sharedBackendFileManifestSha256 = $sharedBackendPackages.FileManifestSha256
        $record.sharedBackendFileCount = $sharedBackendPackages.FileCount
        Write-ReleaseRecord -Record $record -Path $recordPath
    }

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
    elseif ($Target -cne 'site') {
        # Guard tests use synthetic command responses, never disposable repositories.
        Invoke-Checked -Executable 'powershell.exe' -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $repoRoot 'scripts/tests/deploy-guardrails.tests.ps1')
        ) -WorkingDirectory $repoRoot
        Invoke-Checked -Executable 'powershell.exe' -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $repoRoot 'scripts/tests/shared-backend-candidate-activation.tests.ps1')
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

    if ($Target -in @('all', 'hosting', 'site')) {
        if ($Target -ceq 'site') {
            $hostingContext = New-SiteReleaseContext -Checkout $releaseCheckout -CandidateSha $candidateSha -RecordPath $recordPath -BaselineHosting $hostingBefore
            $record.siteArtifactManifestSha256 = $hostingContext.ArtifactManifestSha256
            $record.siteBaselineMarkerSha256 = $hostingContext.BaselineMarkerSha256
            $record.sitePublisherRecord = $hostingContext.PublisherRecordPath
        }
        else { $hostingContext = New-HostingReleaseContext -Checkout $releaseCheckout -CandidateSha $candidateSha -RecordPath $recordPath }
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
        $uploadContext = New-ApiUploadContext -Checkout $releaseCheckout -LockedSource $sourceLock -RecordPath $recordPath -Packages $sharedBackendPackages
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
        Assert-SharedBackendUploadUnchanged $sharedBackendPackages
        $builtImage = Get-ReleaseBuiltImage -Build $build -ExpectedTag $imageTag
        $record.apiBuildId = $builtImage.BuildId
        $record.apiImageDigest = $builtImage.Digest
        $record.apiImageReference = $builtImage.Image
        Write-ReleaseRecord -Record $record -Path $recordPath
        Assert-ReleaseCheckout -Repository $releaseCheckout -Sha $candidateSha
        Assert-ServingApiUnchanged -Before $apiBefore -Now (Get-LiveApi)
        Write-Host "`nCreating a non-serving Cloud Run revision from the verified image digest..." -ForegroundColor Yellow
        if ($sharedBackendPackages) {
            $freshService = Get-CloudRunService
            $freshServingRevision = Get-ServingApiRevision $freshService
            Assert-ServingApiUnchanged -Before $apiBefore -Now ([pscustomobject]@{
                RevisionName = $freshServingRevision; SourceSha = Get-ApiRevisionSourceSha (Get-ApiRevision $freshServingRevision)
            })
            $legacyWorkerConfiguration = Get-ApiLegacyWorkerConfiguration $freshService.spec.template
            $record.sharedBackendRemovedWorkerEnvironment = $legacyWorkerConfiguration.Environment
            $record.sharedBackendRemovedWorkerSecrets = $legacyWorkerConfiguration.Secrets
            $apiServiceReplaceContext = New-ApiServiceReplaceContext -Service $freshService -ServiceName $serviceName -ServingRevision $apiBefore.RevisionName `
                -SourceSha $candidateSha -Image $builtImage.Image -Packages $sharedBackendPackages -RecordPath $recordPath
            $record.apiServiceReplaceSha256 = $apiServiceReplaceContext.Sha256
            $record.apiServiceReplacePath = $apiServiceReplaceContext.Path
            $record.apiServiceExpectedResourceVersion = $apiServiceReplaceContext.ResourceVersion
            Write-ReleaseRecord -Record $record -Path $recordPath
            $expectedService = Get-Content -LiteralPath $apiServiceReplaceContext.Path -Raw | ConvertFrom-Json
            $replaceEndpoint = "https://$region-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$project/services/$serviceName"
            $validated = Invoke-ApiServiceReplaceRequest -Context $apiServiceReplaceContext -Endpoint $replaceEndpoint -Account $firebaseAccount -DryRun
            Assert-SharedBackendCandidateService -Service $validated -Expected $expectedService -SourceSha $candidateSha -Image $builtImage.Image -ServingRevision $apiBefore.RevisionName -Packages $sharedBackendPackages
            [IO.File]::WriteAllText(($recordPath + '.api-validation.json'), ([ordered]@{
                checkedAtUtc = [DateTime]::UtcNow.ToString('o'); dryRunValidated = $true;
                sourceSha = $candidateSha; payloadSha256 = $apiServiceReplaceContext.Sha256;
                expectedResourceVersion = $apiServiceReplaceContext.ResourceVersion; servingRevision = $apiBefore.RevisionName
            } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
            $beforeReplace = Get-CloudRunService
            if ([string]$beforeReplace.metadata.resourceVersion -cne $apiServiceReplaceContext.ResourceVersion) { throw 'Cloud Run service configuration changed after candidate validation; replacement was not attempted.' }
            Assert-ServingApiUnchanged -Before $apiBefore -Now (Get-LiveApi)
            $created = Invoke-ApiServiceReplaceRequest -Context $apiServiceReplaceContext -Endpoint $replaceEndpoint -Account $firebaseAccount
            Assert-SharedBackendCandidateService -Service $created -Expected $expectedService -SourceSha $candidateSha -Image $builtImage.Image -ServingRevision $apiBefore.RevisionName -Packages $sharedBackendPackages
            $sharedCandidateRevision = Wait-SharedBackendCandidateReady -ServingRevision $apiBefore.RevisionName -PreviousRevision $freshService.status.latestCreatedRevisionName `
                -SourceSha $candidateSha -Image $builtImage.Image -Packages $sharedBackendPackages -RecordPath $recordPath -ExpectedService $expectedService
        }
        else {
            $apiDeployArguments = @('run', 'deploy', $serviceName, '--image', $builtImage.Image,
                '--region', $region, '--project', $project, '--max-instances', '1', '--no-traffic',
                "--update-labels=ff-source-sha=$candidateSha", '--quiet')
            $apiRuntimeFlagsContext = New-ApiRuntimeFlagsContext -SourceSha $candidateSha -Bindings $gameWorkerBindings -RuntimeConfiguration $hostRuntimeConfiguration -RecordPath $recordPath
            $record.apiRuntimeFlagsSha256 = $apiRuntimeFlagsContext.Sha256
            $record.apiRuntimeFlagsPath = $apiRuntimeFlagsContext.Path
            Write-ReleaseRecord -Record $record -Path $recordPath
            Assert-HostRuntimeConfigurationUnchanged -Configuration $apiRuntimeFlagsContext -LockedStream $apiRuntimeFlagsContext.Stream
            $apiDeployArguments += "--flags-file=$($apiRuntimeFlagsContext.Path)"
            if ($ContinuousAudit) { $apiDeployArguments += @('--min-instances', '1', '--no-cpu-throttling') }
            Invoke-Checked -Executable 'gcloud.cmd' -Arguments $apiDeployArguments -WorkingDirectory $releaseCheckout
        }

        $service = Get-CloudRunService
        $newApiRevision = [string]$service.status.latestCreatedRevisionName
        if ($sharedBackendPackages -and $newApiRevision -cne $sharedCandidateRevision) { throw 'Another API candidate was created after the verified shared revision became ready.' }
        if ([string]::IsNullOrWhiteSpace($newApiRevision) -or $newApiRevision -ceq $apiBefore.RevisionName) {
            throw 'Cloud Run did not expose a distinct new revision; traffic was not changed.'
        }
        $newRevision = Get-ApiRevision $newApiRevision
        if ((Get-ApiRevisionSourceSha $newRevision) -cne $candidateSha) {
            throw 'New Cloud Run revision lacks the expected source stamp; traffic was not changed.'
        }
        Assert-ApiRevisionImage -Revision $newRevision -ExpectedImage $builtImage.Image
        Assert-ApiWorkerBindings -Revision $newRevision -Bindings $gameWorkerBindings
        Assert-ApiHostRuntimeConfiguration -Revision $newRevision -Configuration $hostRuntimeConfiguration
        Assert-ApiSharedBackendConfiguration -Revision $newRevision -Packages $sharedBackendPackages -Service $service
        if ($sharedBackendPackages) {
            Assert-SharedBackendCandidateService -Service $service -Expected $expectedService -SourceSha $candidateSha -Image $builtImage.Image -ServingRevision $apiBefore.RevisionName -Packages $sharedBackendPackages
        }
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
        $candidateAtTrafficSwitch = Get-ApiRevision $newApiRevision
        Assert-ApiRevisionImage -Revision $candidateAtTrafficSwitch -ExpectedImage $builtImage.Image
        if ((Get-ApiRevisionSourceSha $candidateAtTrafficSwitch) -cne $candidateSha) { throw 'Candidate API source stamp changed before traffic switch.' }
        Assert-ApiWorkerBindings -Revision $candidateAtTrafficSwitch -Bindings $gameWorkerBindings
        Assert-ApiHostRuntimeConfiguration -Revision $candidateAtTrafficSwitch -Configuration $hostRuntimeConfiguration
        Assert-ApiSharedBackendConfiguration -Revision $candidateAtTrafficSwitch -Packages $sharedBackendPackages -Service (Get-CloudRunService)
        if ($sharedBackendPackages) {
            Assert-SharedBackendCandidateService -Service (Get-CloudRunService) -Expected $expectedService -SourceSha $candidateSha -Image $builtImage.Image -ServingRevision $apiBefore.RevisionName -Packages $sharedBackendPackages
        }
        Assert-SharedBackendUploadUnchanged $sharedBackendPackages

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

    if ($Target -in @('all', 'hosting', 'site')) {
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
        $siteResult = $null
        if ($Target -ceq 'site') {
            Write-Host "`nPublishing site while preserving live game artifacts..." -ForegroundColor Yellow
            $siteArguments = @((Join-Path $repoRoot 'scripts/publish-site-hosting.mjs'),
                '--project', $project, '--site', $project,
                '--baseline-version', $hostingBefore.VersionName, '--baseline-release', $hostingBefore.ReleaseName,
                '--directory', $hostingContext.Directory, '--source-sha', $candidateSha,
                '--artifact-manifest', $hostingContext.ArtifactManifestPath,
                '--record-path', $hostingContext.PublisherRecordPath)
            if ($hostingContext.BaselineMarkerPath) { $siteArguments += @('--baseline-marker', $hostingContext.BaselineMarkerPath) }
            Invoke-Checked -Executable 'node.exe' -Arguments $siteArguments -WorkingDirectory $releaseCheckout
            $siteResult = Get-Content -LiteralPath $hostingContext.PublisherRecordPath -Raw | ConvertFrom-Json -ErrorAction Stop
            if ($siteResult.sourceSha -cne $candidateSha -or
                $siteResult.versionName -cnotmatch "^sites/$project/versions/[^/]+$" -or
                $siteResult.releaseName -cnotmatch "^sites/$project/releases/[^/]+$" -or
                $siteResult.versionName -ceq $hostingBefore.VersionName -or
                $siteResult.releaseName -ceq $hostingBefore.ReleaseName) {
                throw 'Site publisher did not record a new release/version for the verified candidate.'
            }
        }
        else {
            Write-Host "`nPublishing stamped Firebase Hosting build..." -ForegroundColor Yellow
            Invoke-Checked -Executable 'npx.cmd' -Arguments @(
                '--yes', 'firebase-tools@14.4.0', 'deploy', '--only', 'hosting',
                '--config', $hostingContext.ConfigPath,
                '--project', $project, '--account', $firebaseAccount, '--non-interactive'
            ) -WorkingDirectory $releaseCheckout
        }

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
        if ($Target -ceq 'site' -and ($hostingAfter.VersionName -cne $siteResult.versionName -or
            $hostingAfter.ReleaseName -cne $siteResult.releaseName)) { throw 'Live Hosting differs from the recorded site release/version.' }
        $record.newHostingRelease = $hostingAfter.ReleaseName
        $record.newHostingVersion = $hostingAfter.VersionName
        Write-ReleaseRecord -Record $record -Path $recordPath
    }

    Write-Host "`nRunning production smoke..." -ForegroundColor Yellow
    $smokeScript = if ($Target -ceq 'site') { 'smoke:site' } else { 'smoke:production' }
    Invoke-Checked -Executable 'npm.cmd' -Arguments @('run', $smokeScript) -WorkingDirectory $releaseCheckout
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
    if ($apiServiceReplaceContext) { $apiServiceReplaceContext.Stream.Dispose() }
    if ($sharedBackendPackages) { Exit-ReleaseSourceLock -Lock $sharedBackendPackages.Lock }
    if ($apiRuntimeFlagsContext) { $apiRuntimeFlagsContext.Stream.Dispose() }
    if ($hostRuntimeConfigurationLock) { $hostRuntimeConfigurationLock.Dispose() }
    if ($hostingContext) { Exit-ReleaseSourceLock -Lock $hostingContext.Lock }
    Exit-ReleaseSourceLock -Lock $sourceLock
    if ($workspaceLock) { $workspaceLock.Dispose() }
}
