$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repository 'scripts/ReleaseGuardrails.psm1') -Force
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repository 'scripts/deploy.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Release script failed parsing.' }
foreach ($name in @('Get-DeploymentInputHash', 'New-ApiUploadContext', 'New-ApiUploadArchive')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Invoke-JsonCommand {
    param($Executable, $Arguments)
    if ($Executable -cne 'python.exe') { throw 'Archive tests cannot call a cloud command.' }
    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return ($output -join "`n") | ConvertFrom-Json
}
function Get-ApiBaseUploadInventory {
    param($Checkout, $TrackedPaths)
    Assert-ReleaseUploadPaths -TrackedPaths $TrackedPaths -UploadPaths $TrackedPaths
}

$fixture = Join-Path $repository ('artifacts/tests/release-upload-archive-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $fixture 'src')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture '.hypothesis')) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'Dockerfile'), 'FROM scratch')
[IO.File]::WriteAllText((Join-Path $fixture 'src/approved.txt'), 'exact approved bytes')
[IO.File]::WriteAllText((Join-Path $fixture '.hypothesis/unapproved.txt'), 'must never be uploaded')
Copy-Item -LiteralPath (Join-Path $repository 'scripts/create-release-upload-archive.py') -Destination (Join-Path $fixture 'scripts/create-release-upload-archive.py')
$paths = @('Dockerfile', 'src/approved.txt', 'scripts/create-release-upload-archive.py')
$locked = [pscustomobject]@{ Streams = [Collections.Generic.List[IO.FileStream]]::new(); TrackedPaths = $paths }
try {
    foreach ($path in $paths) {
        $locked.Streams.Add([IO.File]::Open((Join-Path $fixture $path), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    }
    $selection = New-ApiUploadContext -Checkout $fixture -LockedSource $locked -RecordPath (Join-Path $fixture 'release') -Packages $null
    $context = New-ApiUploadArchive -Checkout $fixture -LockedSource $locked -RecordPath (Join-Path $fixture 'release') -Packages $null -UploadContext $selection
    if ($context.FileCount -ne 3 -or $context.ArchiveSha256 -cne (Get-ReleaseFileHash -LiteralPath $context.ArchivePath)) { throw 'Archive identity differs from the approved selection.' }
    $names = (& python.exe -c 'import json,sys,tarfile; print(json.dumps(tarfile.open(sys.argv[1]).getnames()))' $context.ArchivePath) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or ($names -join '|') -cne (($paths | Sort-Object) -join '|')) { throw 'Archive members differ from the exact allowlist.' }
    Write-Host 'PASS: exact approved files and hashes; stray cache excluded'
    $changed = $false
    try { $writer = [IO.File]::Open($context.ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::Read); $writer.Dispose(); $changed = $true } catch [IO.IOException] { }
    if ($changed) { throw 'Source archive was not held immutable through submission.' }
    Write-Host 'PASS: verified source archive remains immutable'

    $document = Get-Content (Join-Path $fixture 'release.source.files.json') -Raw | ConvertFrom-Json
    $document.files[0].path = '../outside.txt'
    $badManifest = Join-Path $fixture 'unsafe.json'
    [IO.File]::WriteAllText($badManifest, ($document | ConvertTo-Json -Depth 5))
    $null = & python.exe (Join-Path $fixture 'scripts/create-release-upload-archive.py') --root $fixture --manifest $badManifest --output (Join-Path $fixture 'unsafe.tar.gz')
    if ($LASTEXITCODE -eq 0) { throw 'Traversal path was accepted.' }
    Write-Host 'PASS: traversal source rejected'

    $document = Get-Content (Join-Path $fixture 'release.source.files.json') -Raw | ConvertFrom-Json
    $document.files[0].sha256 = '0' * 64
    $badManifest = Join-Path $fixture 'wrong-hash.json'
    [IO.File]::WriteAllText($badManifest, ($document | ConvertTo-Json -Depth 5))
    $null = & python.exe (Join-Path $fixture 'scripts/create-release-upload-archive.py') --root $fixture --manifest $badManifest --output (Join-Path $fixture 'wrong-hash.tar.gz')
    if ($LASTEXITCODE -eq 0) { throw 'Source hash mismatch was accepted.' }
    Write-Host 'PASS: archive bytes must match locked source hashes'

    $document = Get-Content (Join-Path $fixture 'release.source.files.json') -Raw | ConvertFrom-Json
    $junction = Join-Path $fixture 'redirect'
    New-Item -ItemType Junction -Path $junction -Target (Join-Path $fixture 'src') | Out-Null
    ($document.files | Where-Object path -CEQ 'src/approved.txt').path = 'redirect/approved.txt'
    $badManifest = Join-Path $fixture 'redirect.json'
    [IO.File]::WriteAllText($badManifest, ($document | ConvertTo-Json -Depth 5))
    $null = & python.exe (Join-Path $fixture 'scripts/create-release-upload-archive.py') --root $fixture --manifest $badManifest --output (Join-Path $fixture 'redirect.tar.gz')
    if ($LASTEXITCODE -eq 0) { throw 'Reparse-point ancestor was accepted.' }
    Write-Host 'PASS: redirected source ancestors rejected'

    $document = Get-Content (Join-Path $fixture 'release.source.files.json') -Raw | ConvertFrom-Json
    $document.files = @($document.files) + $document.files[0]
    $badManifest = Join-Path $fixture 'duplicate.json'
    [IO.File]::WriteAllText($badManifest, ($document | ConvertTo-Json -Depth 5))
    $null = & python.exe (Join-Path $fixture 'scripts/create-release-upload-archive.py') --root $fixture --manifest $badManifest --output (Join-Path $fixture 'duplicate.tar.gz')
    if ($LASTEXITCODE -eq 0) { throw 'Duplicate archive member was accepted.' }
    Write-Host 'PASS: duplicate archive members rejected'
}
finally { Exit-ReleaseSourceLock -Lock $locked }
