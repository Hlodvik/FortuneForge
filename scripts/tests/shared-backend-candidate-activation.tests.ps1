param(
    [ValidateSet('success', 'failure', 'conflict', 'stdout')]
    [string[]]$Scenarios = @('success', 'failure', 'conflict', 'stdout')
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repository 'scripts/deploy.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($name in @('Remove-SharedBackendCandidateTag', 'Wait-SharedBackendCandidateReady')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true)
    if (-not $definition) { throw "Missing function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

# These tests execute only the activation functions with in-memory cloud mocks.
function Get-CloudRunService { return $script:service | ConvertTo-Json -Depth 30 | ConvertFrom-Json }
function Get-ServingApiRevision { param($Service) return 'api-old' }
function Get-ApiRevision {
    param($Name)
    if ($script:mode -eq 'failure' -and $script:added) { throw 'Synthetic startup failure.' }
    return [pscustomobject]@{ status = @{ conditions = @(@{ type = 'Ready'; status = $(if ($script:added) { 'True' } else { 'False' }) }) } }
}
function Get-ApiRevisionSourceSha { param($Revision) return ('a' * 40) }
function Assert-ApiRevisionImage { param($Revision, $ExpectedImage) }
function Assert-ApiSharedBackendConfiguration { param($Revision, $Packages, $Service, [switch]$TemplateOnly) }
function Start-Sleep { param($Seconds) }
function Assert-SharedBackendCandidateService {
    param($Service, $Expected, $SourceSha, $Image, $ServingRevision, $Packages)
    $actual = @($Service.spec.traffic | ForEach-Object { "$($_.revisionName)|$($_.tag)|$([int]$_.percent)|$([bool]$_.latestRevision)" } | Sort-Object)
    $wanted = @($Expected.spec.traffic | ForEach-Object { "$($_.revisionName)|$($_.tag)|$([int]$_.percent)|$([bool]$_.latestRevision)" } | Sort-Object)
    if (($actual -join ';') -cne ($wanted -join ';')) { throw 'Reviewed traffic configuration changed.' }
    $public = @($Service.spec.traffic | Where-Object { $_.percent -gt 0 })
    if ($public.Count -ne 1 -or $public[0].revisionName -cne 'api-old' -or $public[0].percent -ne 100) { throw 'Public traffic changed.' }
}
function Invoke-Checked {
    param($Executable, $Arguments, $WorkingDirectory)
    $script:commands.Add(($Arguments -join ' '))
    if ($script:mode -eq 'stdout') {
        Write-Output 'URL: https://candidate.example.test'
        Write-Output 'Traffic: 100% api-old'
    }
    $add = @($Arguments | Where-Object { $_ -like '--update-tags=*' })
    if ($add.Count) {
        $mapping = $add[0].Substring('--update-tags='.Length).Split('=')
        if ($mapping[1] -cne 'api-new') { throw 'Activation did not pin a concrete candidate revision.' }
        $script:service.spec.traffic += [pscustomobject]@{ tag = $mapping[0]; revisionName = $(if ($script:mode -eq 'conflict') { 'api-foreign' } else { $mapping[1] }); percent = 0; latestRevision = $false }
        $script:added = $true
        $script:service.status.latestReadyRevisionName = 'api-new'
        return
    }
    $remove = @($Arguments | Where-Object { $_ -like '--remove-tags=*' })
    if ($remove.Count -ne 1) { throw 'Unexpected cloud operation.' }
    $tag = $remove[0].Substring('--remove-tags='.Length)
    $script:service.spec.traffic = @($script:service.spec.traffic | Where-Object { $_.tag -cne $tag })
}

$serviceName = 'api'
$region = 'local-only'
$project = 'local-only'
$releaseCheckout = $repository
$receiptBase = Join-Path ([IO.Path]::GetTempPath()) ('ff-candidate-activation-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($case in $Scenarios) {
        $script:mode = $case
        $script:added = $false
        $script:commands = [Collections.Generic.List[string]]::new()
        $script:service = [pscustomobject]@{
            spec = [pscustomobject]@{ traffic = @(
                [pscustomobject]@{ revisionName = 'api-old'; percent = 100; latestRevision = $false },
                [pscustomobject]@{ tag = 'existing-preview'; revisionName = 'api-preview'; percent = 0; latestRevision = $false }
            ) }
            status = [pscustomobject]@{ latestCreatedRevisionName = 'api-new'; latestReadyRevisionName = 'api-old' }
        }
        $expected = Get-CloudRunService
        $caught = $null
        try {
            $result = Wait-SharedBackendCandidateReady -ServingRevision 'api-old' -PreviousRevision 'api-old' -SourceSha ('a' * 40) -Image 'image@sha256:abc' -Packages @{} -RecordPath $receiptBase -ExpectedService $expected
        }
        catch { $caught = $_ }
        $succeeds = $case -in @('success', 'stdout')
        if ($succeeds -and ($caught -or $result -isnot [string] -or $result -cne 'api-new')) { throw "Tagged activation did not return exactly one candidate revision string: $caught" }
        if (-not $succeeds -and -not $caught) { throw "Expected $case to stop before promotion." }
        $receipt = Get-Content ($receiptBase + '.api-candidate-tag.json') -Raw | ConvertFrom-Json
        $removals = @($script:commands | Where-Object { $_ -like '*--remove-tags=*' })
        if ($case -eq 'conflict') {
            if ($removals.Count -ne 0 -or -not $receipt.cleanupPending) { throw 'A changed tag was mutated or cleanup conflict was lost.' }
        }
        elseif ($removals.Count -ne 1 -or -not $receipt.cleanupCompleted -or $script:service.spec.traffic.Count -ne 2) { throw 'Owned tag was not cleaned up.' }
        if (@($script:commands | Where-Object { $_ -like '*--update-tags=*' }).Count -ne 1) { throw 'Candidate activation must add exactly one owned tag.' }
        if (@($script:service.spec.traffic | Where-Object { $_.tag -ceq 'existing-preview' -and $_.revisionName -ceq 'api-preview' }).Count -ne 1) { throw 'Existing preview tag was changed.' }
        if (@($script:service.spec.traffic | Where-Object { $_.revisionName -ceq 'api-old' -and $_.percent -eq 100 }).Count -ne 1) { throw 'Original public traffic was changed.' }
        Write-Host "PASS: $case"
    }
}
finally {
    foreach ($suffix in @('.api-readiness.json', '.api-candidate-tag.json')) {
        $path = $receiptBase + $suffix
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
