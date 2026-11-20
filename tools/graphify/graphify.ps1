[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('setup', 'refresh', 'query', 'explain', 'path', 'status')]
    [string]$Action = 'status',

    [Parameter(Position = 1)]
    [string]$Query,

    [Parameter(Position = 2)]
    [string]$Other,

    [string]$PythonExe
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$toolEnvironment = Join-Path $env:LOCALAPPDATA 'FortuneForgeTools\graphify'
$toolPython = Join-Path $toolEnvironment 'Scripts\python.exe'
$requirements = Join-Path $PSScriptRoot 'requirements.txt'
$runtimeInspector = Join-Path $PSScriptRoot 'inspect_runtime.py'
$graphDirectory = Join-Path $repositoryRoot 'artifacts\graphify'
$graphFile = Join-Path $graphDirectory 'graph.json'

function Invoke-ToolPython {
    param([string[]]$ToolArguments)
    $previousErrorAction = $ErrorActionPreference
    try {
        # Python/native warnings on stderr are not process failures in Windows PowerShell.
        $ErrorActionPreference = 'Continue'
        & $toolPython @ToolArguments
        $toolExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($toolExitCode -ne 0) {
        throw "Graphify command failed with exit code $toolExitCode."
    }
}

if ($Action -eq 'setup') {
    if (-not (Test-Path -LiteralPath $toolPython)) {
        if (-not $PythonExe) {
            $PythonExe = if (Test-Path -LiteralPath 'C:\Python313\python.exe') {
                'C:\Python313\python.exe'
            } else {
                (Get-Command python -ErrorAction Stop).Source
            }
        }
        & $PythonExe $runtimeInspector check-python
        if ($LASTEXITCODE -ne 0) { throw 'A supported Python interpreter is required.' }
        & $PythonExe -m venv $toolEnvironment
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the Graphify environment.' }
    }
    Invoke-ToolPython -ToolArguments @('-m', 'pip', 'install', '--disable-pip-version-check', '-r', $requirements)
    return
}

if (-not (Test-Path -LiteralPath $toolPython)) {
    throw 'Graphify is not installed. Run tools\graphify\graphify.ps1 setup first.'
}

if ($Action -eq 'status') {
    Invoke-ToolPython -ToolArguments @($runtimeInspector, 'status', $graphFile)
    Write-Output "Environment: $toolEnvironment"
    Write-Output "Graph: $graphFile"
    if (Test-Path -LiteralPath $graphFile) {
        $graphInfo = Get-Item -LiteralPath $graphFile
        Write-Output "Updated: $($graphInfo.LastWriteTime.ToString('o'))"
    } else {
        Write-Output 'No graph yet. Run tools\graphify\graphify.ps1 refresh.'
    }
    return
}

if ($Action -ne 'refresh' -and -not (Test-Path -LiteralPath $graphFile)) {
    throw 'No graph exists. Run tools\graphify\graphify.ps1 refresh first.'
}
if ($Action -in @('query', 'explain', 'path') -and [string]::IsNullOrWhiteSpace($Query)) {
    throw 'Supply a question or symbol as the second argument.'
}
if ($Action -eq 'path' -and [string]::IsNullOrWhiteSpace($Other)) {
    throw 'Supply the destination symbol as the third argument.'
}

$controlledEnvironment = @{
    GRAPHIFY_BACKEND = 'none'
    GRAPHIFY_OUT = $graphDirectory
    GRAPHIFY_QUERY_LOG = (Join-Path $graphDirectory 'queries.jsonl')
    PYTHONUTF8 = '1'
    PYTHONUNBUFFERED = '1'
}
$previousEnvironment = @{}
foreach ($name in $controlledEnvironment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $controlledEnvironment[$name], 'Process')
}

Push-Location $repositoryRoot
try {
    if ($Action -eq 'refresh') {
        $toolArguments = @('-m', 'graphify', 'extract', '.', '--code-only', '--no-dedup', '--max-workers', '2')
        foreach ($pattern in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'excludes.txt')) {
            $pattern = $pattern.Trim()
            if ($pattern -and -not $pattern.StartsWith('#')) {
                $toolArguments += @('--exclude', $pattern)
            }
        }
        Invoke-ToolPython -ToolArguments $toolArguments
    } elseif ($Action -eq 'path') {
        Invoke-ToolPython -ToolArguments @('-m', 'graphify', 'path', $Query, $Other, '--graph', $graphFile)
    } else {
        Invoke-ToolPython -ToolArguments @('-m', 'graphify', $Action, $Query, '--graph', $graphFile)
    }
} finally {
    Pop-Location
    foreach ($name in $controlledEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
