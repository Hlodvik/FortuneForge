[CmdletBinding()]
param(
    [string]$BaseRef = 'origin/main'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw 'Run this script from a FortuneForge Git worktree.'
}

$worktrees = [System.Collections.Generic.List[object]]::new()
$current = @{}
foreach ($line in (git -C $repositoryRoot worktree list --porcelain)) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        if ($current.ContainsKey('Path')) {
            $worktrees.Add([pscustomobject]$current)
        }
        $current = @{}
        continue
    }

    $key, $value = $line -split ' ', 2
    switch ($key) {
        'worktree' { $current.Path = $value }
        'HEAD' { $current.Commit = $value.Substring(0, [Math]::Min(8, $value.Length)) }
        'branch' { $current.Branch = $value -replace '^refs/heads/', '' }
        'detached' { $current.Branch = '(detached)' }
    }
}
if ($current.ContainsKey('Path')) {
    $worktrees.Add([pscustomobject]$current)
}

$worktreeStatus = foreach ($worktree in $worktrees) {
    $changes = @(git -C $worktree.Path status --short --untracked-files=normal)
    [pscustomobject]@{
        Branch = $worktree.Branch
        Commit = $worktree.Commit
        Changes = $changes.Count
        Path = $worktree.Path
    }
}

Write-Output 'Worktrees'
$worktreeStatus | Sort-Object -Property @{ Expression = 'Changes'; Descending = $true }, Branch | Format-Table -AutoSize

git -C $repositoryRoot rev-parse --verify --quiet $BaseRef *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Base reference '$BaseRef' was not found. Fetch the remote or pass -BaseRef."
    return
}

$unintegratedBranches = foreach ($branch in (git -C $repositoryRoot for-each-ref --format='%(refname:short)' refs/heads)) {
    git -C $repositoryRoot merge-base --is-ancestor $branch $BaseRef 2>$null
    if ($LASTEXITCODE -ne 0) {
        $commit = (git -C $repositoryRoot rev-parse --short=8 $branch).Trim()
        $subject = (git -C $repositoryRoot log -1 --format='%s' $branch).Trim()
        [pscustomobject]@{ Branch = $branch; Commit = $commit; Subject = $subject }
    }
}

Write-Output "Local branches not contained in $BaseRef"
$unintegratedBranches | Sort-Object Branch | Format-Table -AutoSize
