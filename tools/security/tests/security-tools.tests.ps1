$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../SecurityTools.psm1') -Force

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Description)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
}

foreach ($url in @('http://127.0.0.1:5150', 'http://localhost:5150', 'http://[::1]:5150')) {
    $target = Assert-SecurityTarget $url
    if (-not $target.IsLoopback) { throw "Loopback target rejected: $url" }
}
$staging = Assert-SecurityTarget 'https://ff-test.example/api/' -AllowedHost @('FF-TEST.EXAMPLE')
if ($staging.AbsolutePath -ne '/api/') { throw 'Target path was not preserved.' }

Assert-Rejected { Assert-SecurityTarget 'https://fortuneforge.example' } 'remote by default'
Assert-Rejected { Assert-SecurityTarget 'https://ff-test.example.evil.test' -AllowedHost @('ff-test.example') } 'hostname suffix bypass'
Assert-Rejected { Assert-SecurityTarget 'http://ff-test.example' -AllowedHost @('ff-test.example') } 'unencrypted remote target'
Assert-Rejected { Assert-SecurityTarget 'https://user:password@ff-test.example' -AllowedHost @('ff-test.example') } 'credentials in target'
Assert-Rejected { Assert-SecurityTarget 'http://localhost:5150?token=secret' } 'query credentials'
Assert-Rejected { Assert-SecurityTarget 'http://localhost:5150/#fragment' } 'fragment'
Assert-Rejected { Assert-SecurityTarget 'file:///C:/Windows' } 'non-HTTP scheme'
Assert-Rejected { Assert-SecurityTarget 'not a URL' } 'relative or malformed URL'

Write-Host 'Security target boundary checks passed.'
