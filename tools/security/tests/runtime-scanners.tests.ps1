$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'RuntimeScanners.psm1') -Force
$runtimeModule = Get-Module RuntimeScanners

function Assert-Equal {
    param($Actual, $Expected, [string]$Name)
    if ($Actual -cne $Expected) { throw "$Name expected '$Expected', got '$Actual'." }
}

function Assert-True {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw "$Name failed." }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern, [string]$Name)
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "$Name failed with unexpected error: $_" }
        return
    }
    throw "$Name did not reject the unsafe request."
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('fortune-forge-runtime-scanner-tests-' + [guid]::NewGuid().ToString('N'))
$originalToken = $env:FF_SECURITY_TOKEN
try {
    [void][System.IO.Directory]::CreateDirectory($fixtureRoot)
    $env:FF_SECURITY_TOKEN = $null
    $planText = New-ZapScanPlan -TargetUrl 'http://127.0.0.1:5099/' -ReportDirectory $fixtureRoot
    $plan = $planText | ConvertFrom-Json
    Assert-Equal @($plan.jobs | Where-Object type -eq 'activeScan').Count 0 'default ZAP plan is passive'
    Assert-Equal @($plan.jobs | Where-Object type -eq 'replacer').Count 0 'anonymous plan has no Authorization override'
    $spider = ($plan.jobs | Where-Object type -eq 'spider').parameters
    Assert-Equal $spider.postForm $false 'spider cannot submit POST forms'
    Assert-Equal $spider.processForm $false 'spider does not process forms'
    Assert-Equal $spider.threadCount 1 'bounded crawler concurrency'
    Assert-Equal $spider.maxDuration 1 'bounded crawler duration'
    Assert-Equal $spider.maxDepth 2 'bounded crawl depth'
    $scope = $plan.env.contexts[0].includePaths[0]
    Assert-True ('http://127.0.0.1:5099/api/balance' -match $scope) 'target routes are in scope'
    foreach ($outside in @('http://127.0.0.1:50990/api', 'http://127.0.0.1:5099.evil.example/api',
        'https://127.0.0.1:5099/api', 'http://localhost:5099/api')) {
        Assert-True ($outside -notmatch $scope) "origin scope excludes $outside"
    }
    $subtreePlan = New-ZapScanPlan -TargetUrl 'http://127.0.0.1:5099/preview/' -ReportDirectory $fixtureRoot | ConvertFrom-Json
    $subtreeScope = $subtreePlan.env.contexts[0].includePaths[0]
    Assert-True ('http://127.0.0.1:5099/preview/api' -match $subtreeScope) 'target subtree remains in scope'
    Assert-True ('http://127.0.0.1:5099/preview-other/api' -notmatch $subtreeScope) 'similarly named application is out of scope'
    Assert-True ('http://127.0.0.1:5099/api' -notmatch $subtreeScope) 'target subtree does not include sibling routes'
    Assert-Throws { New-ZapScanPlan -TargetUrl 'https://user:password@example.test/' -ReportDirectory $fixtureRoot } 'without credentials' 'credentials in target URL'

    $env:FF_SECURITY_TOKEN = 'test-sentinel-token-never-persist'
    $activeText = New-ZapScanPlan -TargetUrl 'http://127.0.0.1:5099/' -ReportDirectory $fixtureRoot -Active
    $active = $activeText | ConvertFrom-Json
    $activeScan = ($active.jobs | Where-Object type -eq 'activeScan').parameters
    Assert-Equal $activeScan.maxScanDurationInMins 3 'bounded active scan duration'
    Assert-Equal $activeScan.threadPerHost 1 'bounded active scan concurrency'
    Assert-True (-not $activeText.Contains($env:FF_SECURITY_TOKEN)) 'ZAP plan never embeds token value'
    $authRule = ($active.jobs | Where-Object type -eq 'replacer').rules[0]
    Assert-Equal $authRule.replacementString 'Bearer ${FF_SECURITY_TOKEN}' 'ZAP token resolves from environment'
    Assert-Equal $authRule.url $scope 'Authorization injection is scoped to target origin'

    # Native stderr is diagnostic output, not a failed scan when exit code is zero.
    $nativeCheck = & $runtimeModule {
        $ErrorActionPreference = 'Stop'
        $nativeCode = Invoke-RuntimeScannerProcess -Executable (Get-Command powershell.exe).Source -Arguments @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
            "[Console]::Out.WriteLine('runtime-native-stdout'); [Console]::Error.WriteLine('runtime-native-stderr'); exit 0"
        )
        [pscustomobject]@{ ExitCode = $nativeCode; ErrorAction = $ErrorActionPreference }
    } 6>$null
    Assert-Equal $nativeCheck.ExitCode 0 'successful native stderr preserves exit code'
    Assert-Equal $nativeCheck.ErrorAction 'Stop' 'native boundary restores error preference'

    # Replace the module's sole native execution boundary; these tests never start a scanner.
    & $runtimeModule {
        $script:ScannerCalls = [System.Collections.Generic.List[object]]::new()
        $script:FailScanner = $false
        function script:Invoke-RuntimeScannerProcess {
            param([string]$Executable, [string[]]$Arguments)
            $script:ScannerCalls.Add([pscustomobject]@{ Executable = $Executable; Arguments = $Arguments; WorkingDirectory = (Get-Location).Path })
            if ([System.IO.Path]::GetFileName($Executable) -ieq 'schemathesis.exe') {
                $stateDirectory = Join-Path (Get-Location).Path '.schemathesis'
                [void][System.IO.Directory]::CreateDirectory($stateDirectory)
                [System.IO.File]::WriteAllText((Join-Path $stateDirectory 'runner-state.marker'), 'relative scanner state')
            }
            if ($script:FailScanner) { throw 'simulated native launch failure' }
            return 7
        }
    }
    $fakeExecutable = Join-Path $fixtureRoot 'schemathesis.exe'
    [System.IO.File]::WriteAllText($fakeExecutable, '')
    $zapDirectory = Join-Path $fixtureRoot 'zap'
    [void][System.IO.Directory]::CreateDirectory($zapDirectory)
    [System.IO.File]::WriteAllText((Join-Path $zapDirectory 'zap.bat'), '')
    $schemaFile = Join-Path $fixtureRoot 'bundled.json'
    [System.IO.File]::WriteAllText($schemaFile, '{"openapi":"3.0.0","info":{"title":"test","version":"1"},"paths":{}}')
    $reports = Join-Path $fixtureRoot 'reports'
    Assert-Throws {
        Invoke-SchemathesisSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -SchemathesisExecutable $fakeExecutable -ReportDirectory $reports -Schema $schemaFile
    } 'Specify -Active' 'fuzzing requires explicit opt-in'
    Assert-True (-not (Test-Path -LiteralPath $reports)) 'rejected fuzzing writes no reports'
    foreach ($schemaUrl in @('https://127.0.0.1:5099/openapi.json', 'http://127.0.0.1:5100/openapi.json', '//other.example/openapi.json')) {
        Assert-Throws {
            Invoke-SchemathesisSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -SchemathesisExecutable $fakeExecutable -ReportDirectory $reports -Schema $schemaUrl -Active
        } 'same scheme, host and port' 'schema URL cannot redirect scan scope'
    }
    $externalSchema = Join-Path $fixtureRoot 'external.json'
    [System.IO.File]::WriteAllText($externalSchema, '{"openapi":"3.0.0","paths":{},"components":{"schemas":{"Other":{"$ref":"https://outside.example/schema.json"}}}}')
    Assert-Throws {
        Invoke-SchemathesisSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -SchemathesisExecutable $fakeExecutable -ReportDirectory $reports -Schema $externalSchema -Active
    } 'external reference' 'external schema references cannot fetch outside target'
    Assert-Equal (& $runtimeModule { $script:ScannerCalls.Count }) 0 'rejected requests never start native scanners'

    $fuzzCaller = Join-Path $fixtureRoot 'fuzz-caller'
    [void][System.IO.Directory]::CreateDirectory($fuzzCaller)
    Push-Location -LiteralPath $fuzzCaller
    try {
        $result = Invoke-SchemathesisSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -SchemathesisExecutable $fakeExecutable -ReportDirectory $reports -Schema $schemaFile -Active
        Assert-Equal (Get-Location).Path $fuzzCaller 'Schemathesis restores the caller working directory'
    }
    finally { Pop-Location }
    Assert-Equal $result.ExitCode 7 'fuzzing exit code is propagated'
    Assert-Equal (& $runtimeModule { $script:ScannerCalls[0].WorkingDirectory }) $reports 'Schemathesis runs inside its report directory'
    Assert-True (Test-Path -LiteralPath (Join-Path $reports '.schemathesis/runner-state.marker')) 'relative scanner state stays with scan reports'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fuzzCaller '.schemathesis'))) 'relative scanner state does not spill into caller directory'
    $fuzzArguments = & $runtimeModule { $script:ScannerCalls[0].Arguments }
    Assert-True ([array]::IndexOf($fuzzArguments, '--config-file') -lt [array]::IndexOf($fuzzArguments, 'run')) 'global config flag precedes run'
    foreach ($pair in @(@('--url', 'http://127.0.0.1:5099/'), @('--workers', '1'), @('--max-examples', '20'),
        @('--max-time', '120'), @('--rate-limit', '5/s'), @('--max-redirects', '0'), @('--request-timeout', '10'),
        @('--max-failures', '10'), @('--report', 'junit,html'))) {
        Assert-Equal $fuzzArguments[[array]::IndexOf($fuzzArguments, $pair[0]) + 1] $pair[1] "fuzzing option $($pair[0])"
    }
    $fuzzConfig = [System.IO.File]::ReadAllText((Join-Path $reports 'schemathesis.toml'))
    Assert-True ($fuzzConfig.Contains('auto-signup = false')) 'fuzzing cannot automatically register accounts'
    Assert-True ($fuzzConfig.Contains('${FF_SECURITY_TOKEN}')) 'fuzzing token uses environment interpolation'
    Assert-True (-not $fuzzConfig.Contains($env:FF_SECURITY_TOKEN)) 'fuzzing config never embeds token value'
    Assert-True (-not ($fuzzArguments -join ' ').Contains($env:FF_SECURITY_TOKEN)) 'native command arguments never contain token value'

    $callerDirectory = (Get-Location).Path
    $zapResult = Invoke-ZapSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -ZapDirectory $zapDirectory -ReportDirectory (Join-Path $fixtureRoot 'zap-reports')
    Assert-Equal $zapResult.ExitCode 7 'ZAP exit code is propagated'
    Assert-Equal $zapResult.Mode 'Passive' 'ZAP mode is reported'
    Assert-Equal (Get-Location).Path $callerDirectory 'ZAP restores the caller working directory'
    Assert-Equal (& $runtimeModule { $script:ScannerCalls[1].WorkingDirectory }) $zapDirectory 'ZAP resolves launcher JAR from installation directory'
    $zapArguments = & $runtimeModule { $script:ScannerCalls[1].Arguments }
    Assert-True ($zapArguments -contains '-silent') 'ZAP disables unsolicited update requests'
    Assert-True ($zapArguments -contains '-cmd') 'ZAP runs without opening desktop UI'
    $firstSessionHome = $zapArguments[[array]::IndexOf($zapArguments, '-dir') + 1]
    Assert-True ($firstSessionHome -ne $zapDirectory) 'ZAP home is separate from installation'
    Invoke-ZapSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -ZapDirectory $zapDirectory -ReportDirectory (Join-Path $fixtureRoot 'zap-reports') | Out-Null
    $nextArguments = & $runtimeModule { $script:ScannerCalls[2].Arguments }
    Assert-True ($firstSessionHome -ne $nextArguments[[array]::IndexOf($nextArguments, '-dir') + 1]) 'each ZAP invocation has an isolated home'
    & $runtimeModule { $script:FailScanner = $true }
    Assert-Throws {
        Invoke-ZapSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -ZapDirectory $zapDirectory -ReportDirectory (Join-Path $fixtureRoot 'zap-reports')
    } 'simulated native launch failure' 'native launch errors surface'
    Assert-Equal (Get-Location).Path $callerDirectory 'failed ZAP launch also restores caller working directory'
    $failedFuzzReports = Join-Path $fixtureRoot 'failed-fuzz-reports'
    Push-Location -LiteralPath $fuzzCaller
    try {
        Assert-Throws {
            Invoke-SchemathesisSecurityScan -TargetUrl 'http://127.0.0.1:5099/' -SchemathesisExecutable $fakeExecutable -ReportDirectory $failedFuzzReports -Schema $schemaFile -Active
        } 'simulated native launch failure' 'Schemathesis native launch errors surface'
        Assert-Equal (Get-Location).Path $fuzzCaller 'failed Schemathesis launch restores caller working directory'
    }
    finally { Pop-Location }
    Assert-True (Test-Path -LiteralPath (Join-Path $failedFuzzReports '.schemathesis/runner-state.marker')) 'failed scanner state stays with failed run reports'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fuzzCaller '.schemathesis'))) 'failed scanner does not spill state into caller directory'
    Write-Host 'Runtime scanner safety and command-plan tests passed.'
}
finally {
    $env:FF_SECURITY_TOKEN = $originalToken
    Remove-Module RuntimeScanners -Force
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    if ($resolvedFixture.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedFixture).StartsWith('fortune-forge-runtime-scanner-tests-')) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
