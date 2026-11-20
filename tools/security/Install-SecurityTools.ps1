[CmdletBinding()]
param(
    [ValidateSet('all', 'trivy', 'zap', 'schemathesis', 'sslyze', 'nuclei', 'wapiti')][string]$Tool = 'all',
    [string]$Python = 'C:/Python313/python.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SecurityTools.psm1') -Force
if ($env:OS -ne 'Windows_NT') { throw 'This installer installs the Windows runtimes.' }
$manifest = Get-SecurityToolsManifest
$installRoot = Get-SecurityToolsHome
[IO.Directory]::CreateDirectory($installRoot) | Out-Null
$tools = if ($Tool -eq 'all') { @($manifest.PSObject.Properties.Name) } else { @($Tool) }

foreach ($name in $tools) {
    $definition = $manifest.$name
    $executable = Get-SecurityToolPath $name
    if (Test-Path -LiteralPath $executable) {
        Write-Host "$name $($definition.version) is already installed: $executable"
        continue
    }
    $destination = Join-Path $installRoot "$name-$($definition.version)"
    if ($definition.PSObject.Properties['pythonPackage']) {
        if (-not (Test-Path -LiteralPath $Python)) { throw 'Supply -Python with a Python 3.11+ executable.' }
        & $Python -m venv $destination
        if ($LASTEXITCODE -ne 0) { throw "Could not create the $name virtual environment." }
        & (Join-Path $destination 'Scripts/python.exe') -m pip install --disable-pip-version-check "$($definition.pythonPackage)==$($definition.version)"
        if ($LASTEXITCODE -ne 0) { throw "Could not install $name." }
    }
    else {
        $archive = Join-Path $installRoot "$name-$($definition.version).zip"
        if (-not (Test-Path -LiteralPath $archive)) {
            Write-Host "Downloading $name $($definition.version) from its official GitHub release."
            Invoke-WebRequest -UseBasicParsing -Uri $definition.archiveUrl -OutFile $archive
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $stream = [IO.File]::OpenRead($archive)
            try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose() }
        }
        finally { $sha.Dispose() }
        if ($actualHash -ne $definition.sha256) { throw "SHA-256 mismatch for $archive. Remove that download before retrying." }
        Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
    }
    if (-not (Test-Path -LiteralPath $executable)) { throw "Installation did not produce $executable." }
    Write-Host "Installed $name $($definition.version): $executable"
}
