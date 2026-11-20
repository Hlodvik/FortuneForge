Set-StrictMode -Version Latest

function Assert-RuntimeScannerUrl {
    param([Parameter(Mandatory)][uri]$Url)

    if (-not $Url.IsAbsoluteUri -or $Url.Scheme -notin @('http', 'https') -or
        $Url.UserInfo -or $Url.Fragment) {
        throw 'Runtime scanner URLs must be absolute HTTP(S) URLs without credentials or fragments.'
    }
}

function Invoke-RuntimeScannerProcess {
    param([string]$Executable, [string[]]$Arguments)

    # Keep native console output out of the result object returned to the caller.
    # Windows PowerShell promotes redirected stderr to NativeCommandError under
    # Stop, even when the executable succeeds. Its exit code is authoritative.
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    return $exitCode
}

function New-ZapScanPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][uri]$TargetUrl,
        [Parameter(Mandatory)][string]$ReportDirectory,
        [switch]$Active
    )

    Assert-RuntimeScannerUrl $TargetUrl
    $origin = $TargetUrl.GetLeftPart([System.UriPartial]::Authority)
    $basePath = $TargetUrl.GetLeftPart([System.UriPartial]::Path).TrimEnd('/')
    $scope = '^' + [regex]::Escape($basePath) + '(?:[/?].*)?$'
    $jobs = [System.Collections.Generic.List[object]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:FF_SECURITY_TOKEN)) {
        $jobs.Add([ordered]@{
            type = 'replacer'
            parameters = @{ deleteAllRules = $true }
            rules = @([ordered]@{
                description = 'Fortune Forge test identity'
                url = $scope
                matchType = 'req_header'
                matchString = 'Authorization'
                matchRegex = $false
                replacementString = 'Bearer ${FF_SECURITY_TOKEN}'
            })
        })
    }
    $jobs.Add(@{ type = 'passiveScan-config'; parameters = @{ scanOnlyInScope = $true } })
    $jobs.Add([ordered]@{
        type = 'spider'
        parameters = [ordered]@{
            context = 'fortune-forge'
            url = $TargetUrl.AbsoluteUri
            maxDuration = 1
            maxDepth = 2
            maxChildren = 20
            threadCount = 1
            postForm = $false
            processForm = $false
            logoutAvoidance = $true
            parseRobotsTxt = $false
            parseSitemapXml = $false
        }
    })
    if ($Active) {
        $jobs.Add([ordered]@{
            type = 'activeScan'
            parameters = [ordered]@{
                context = 'fortune-forge'
                url = $TargetUrl.AbsoluteUri
                maxRuleDurationInMins = 1
                maxScanDurationInMins = 3
                threadPerHost = 1
                delayInMs = 200
                maxAlertsPerRule = 10
            }
        })
    }
    $jobs.Add(@{ type = 'passiveScan-wait'; parameters = @{ maxDuration = 1 } })
    foreach ($format in @('html', 'json')) {
        $jobs.Add([ordered]@{
            type = 'report'
            parameters = [ordered]@{
                template = "traditional-$format"
                reportDir = [System.IO.Path]::GetFullPath($ReportDirectory)
                reportFile = "zap-report.$format"
                reportTitle = 'Fortune Forge security scan'
                displayReport = $false
            }
            sites = @($origin)
        })
    }
    $jobs.Add(@{ type = 'exitStatus'; parameters = @{ errorLevel = 'High'; warnLevel = 'Medium' } })
    $plan = [ordered]@{
        env = [ordered]@{
            contexts = @([ordered]@{ name = 'fortune-forge'; urls = @($TargetUrl.AbsoluteUri); includePaths = @($scope) })
            parameters = [ordered]@{ failOnError = $true; failOnWarning = $false; progressToStdout = $true }
        }
        jobs = $jobs.ToArray()
    }
    # JSON is valid YAML and avoids a second serializer for URLs, regexes and Windows paths.
    return ($plan | ConvertTo-Json -Depth 20)
}

function Invoke-ZapSecurityScan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][uri]$TargetUrl,
        [Parameter(Mandatory)][string]$ZapDirectory,
        [Parameter(Mandatory)][string]$ReportDirectory,
        [switch]$Active
    )

    Assert-RuntimeScannerUrl $TargetUrl
    $launcher = Join-Path $ZapDirectory 'zap.bat'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw "ZAP launcher is missing: $launcher" }
    $reportPath = [System.IO.Path]::GetFullPath($ReportDirectory)
    [void][System.IO.Directory]::CreateDirectory($reportPath)
    $sessionHome = Join-Path $reportPath ('zap-home-' + [guid]::NewGuid().ToString('N'))
    $planPath = Join-Path $reportPath 'zap-plan.yaml'
    $plan = New-ZapScanPlan -TargetUrl $TargetUrl -ReportDirectory $reportPath -Active:$Active
    [System.IO.File]::WriteAllText($planPath, $plan, [System.Text.UTF8Encoding]::new($false))
    # The official Windows launcher resolves its JAR against the current directory.
    Push-Location -LiteralPath $ZapDirectory
    try {
        $exitCode = Invoke-RuntimeScannerProcess -Executable $launcher -Arguments @(
            '-cmd', '-silent', '-dir', $sessionHome, '-autorun', $planPath
        )
    }
    finally { Pop-Location }
    return [pscustomobject]@{
        Tool = 'ZAP'; ExitCode = $exitCode; ReportDirectory = $reportPath
        Mode = $(if ($Active) { 'Active' } else { 'Passive' })
    }
}

function Assert-BundledOpenApiReferences {
    param($Value)

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ($key -ceq '$ref' -and ($Value[$key] -isnot [string] -or -not $Value[$key].StartsWith('#'))) {
                throw 'The OpenAPI schema contains an external reference. Export a bundled JSON schema with internal references only.'
            }
            Assert-BundledOpenApiReferences $Value[$key]
        }
    }
    elseif ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) { Assert-BundledOpenApiReferences $item }
    }
    else {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -ceq '$ref' -and ($property.Value -isnot [string] -or -not $property.Value.StartsWith('#'))) {
                throw 'The OpenAPI schema contains an external reference. Export a bundled JSON schema with internal references only.'
            }
            Assert-BundledOpenApiReferences $property.Value
        }
    }
}

function Get-RuntimeOpenApiSchema {
    param([uri]$TargetUrl, [string]$Schema)

    if (Test-Path -LiteralPath $Schema -PathType Leaf) {
        $content = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $Schema).Path)
    }
    else {
        $schemaUrl = [uri]::new($TargetUrl, $Schema)
        Assert-RuntimeScannerUrl $schemaUrl
        if ($schemaUrl.Scheme -cne $TargetUrl.Scheme -or $schemaUrl.DnsSafeHost -ine $TargetUrl.DnsSafeHost -or
            $schemaUrl.Port -ne $TargetUrl.Port) {
            throw 'The OpenAPI schema URL must have the same scheme, host and port as the scan target, or be a local bundled JSON file.'
        }
        $headers = @{}
        if (-not [string]::IsNullOrWhiteSpace($env:FF_SECURITY_TOKEN)) { $headers.Authorization = 'Bearer ' + $env:FF_SECURITY_TOKEN }
        $response = Invoke-WebRequest -Uri $schemaUrl -Headers $headers -MaximumRedirection 0 -TimeoutSec 15 -UseBasicParsing
        if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 300) {
            throw 'The OpenAPI schema URL did not return a successful response. Redirects are disabled.'
        }
        $content = [string]$response.Content
    }
    try { $document = ConvertFrom-Json -InputObject $content -ErrorAction Stop }
    catch { throw 'Runtime API scanning requires a valid bundled JSON OpenAPI schema.' }
    if (-not $document -or (-not $document.PSObject.Properties['openapi'] -and -not $document.PSObject.Properties['swagger'])) {
        throw 'The JSON document does not declare an OpenAPI or Swagger version.'
    }
    Assert-BundledOpenApiReferences $document
    return $content
}

function Invoke-SchemathesisSecurityScan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][uri]$TargetUrl,
        [Parameter(Mandatory)][string]$SchemathesisExecutable,
        [Parameter(Mandatory)][string]$ReportDirectory,
        [string]$Schema = '/openapi/v1.json',
        [switch]$Active
    )

    # Fuzzing sends real requests even when its operation filter contains only GET.
    if (-not $Active) { throw 'Schemathesis sends generated API requests. Specify -Active for a local or staging environment with disposable test data.' }
    Assert-RuntimeScannerUrl $TargetUrl
    if (-not (Test-Path -LiteralPath $SchemathesisExecutable -PathType Leaf)) {
        throw "Schemathesis executable is missing: $SchemathesisExecutable"
    }
    $executablePath = (Resolve-Path -LiteralPath $SchemathesisExecutable).Path
    $schemaContent = Get-RuntimeOpenApiSchema -TargetUrl $TargetUrl -Schema $Schema
    $reportPath = [System.IO.Path]::GetFullPath($ReportDirectory)
    [void][System.IO.Directory]::CreateDirectory($reportPath)
    $schemaPath = Join-Path $reportPath 'openapi.json'
    $configPath = Join-Path $reportPath 'schemathesis.toml'
    $config = 'max-redirects = 0' + "`n"
    if (-not [string]::IsNullOrWhiteSpace($env:FF_SECURITY_TOKEN)) {
        $config += 'headers = { Authorization = "Bearer ${FF_SECURITY_TOKEN}" }' + "`n"
    }
    $config += "[auth]`nauto-signup = false`n"
    [System.IO.File]::WriteAllText($schemaPath, $schemaContent, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($configPath, $config, [System.Text.UTF8Encoding]::new($false))
    $arguments = @('--config-file', $configPath, '--no-color', 'run', $schemaPath,
        '--url', $TargetUrl.AbsoluteUri, '--workers', '1', '--phases', 'examples,coverage,fuzzing',
        '--max-examples', '20', '--max-time', '120', '--rate-limit', '5/s', '--request-timeout', '10',
        '--max-redirects', '0', '--max-failures', '10', '--report', 'junit,html', '--report-dir', $reportPath)
    # Schemathesis keeps relative cache/state under its working directory.
    # Confine those files to this scan's reports instead of the caller's checkout.
    Push-Location -LiteralPath $reportPath
    try {
        $exitCode = Invoke-RuntimeScannerProcess -Executable $executablePath -Arguments $arguments
    }
    finally { Pop-Location }
    return [pscustomobject]@{ Tool = 'Schemathesis'; ExitCode = $exitCode; ReportDirectory = $reportPath; Mode = 'Active' }
}

Export-ModuleMember -Function New-ZapScanPlan, Invoke-ZapSecurityScan, Invoke-SchemathesisSecurityScan, Invoke-RuntimeScannerProcess, Get-RuntimeOpenApiSchema
