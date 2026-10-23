<#
.SYNOPSIS
    Tidy a repository: prune dead remote branches, delete merged local branches,
    and compact the object store.
.DESCRIPTION
    Non-destructive by default (deletes only branches already merged into the
    default branch, never the current or protected branches). -Aggressive adds
    'git gc --prune=now'.
.PARAMETER Protected  Branch names never deleted. Default: main, master, develop, release.
.PARAMETER Aggressive Run 'git gc --prune=now' after pruning.
.PARAMETER Yes        Approve branch deletions without prompting.
.EXAMPLE
    ./git-cleanup.ps1
    ./git-cleanup.ps1 -Aggressive -Yes
#>
param(
    [string[]]$Protected = @('main','master','develop','release'),
    [switch]$Aggressive,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo
$current = Get-Branch

Write-Step 'Pruning stale remote-tracking branches'
(Invoke-Git remote prune origin) | Out-Null
Write-Ok 'Pruned origin'

# Determine default branch for "merged into" check
$h = Invoke-Git symbolic-ref refs/remotes/origin/HEAD
$def = if ($h.Code -eq 0) { ($h.Out -replace '.*origin/','') } else { 'main' }

Write-Step "Finding local branches already merged into '$def'"
$merged = (git branch --merged $def) | ForEach-Object { $_.TrimStart('* ').Trim() } |
          Where-Object { $_ -and $_ -ne $current -and $_ -notin $Protected -and $_ -ne $def }

if (-not $merged) { Write-Info 'No merged branches to delete.' }
else {
    Write-Warn "Merged branches eligible for deletion:"; $merged | ForEach-Object { Write-Info $_ }
    if (Confirm-Destructive "delete $(@($merged).Count) merged branch(es)" $Yes) {
        $merged | ForEach-Object { (Invoke-Git branch -d $_) | Out-Null; Write-Ok "deleted $_" }
    }
}

if ($Aggressive) {
    if (Confirm-Destructive 'run git gc --prune=now (compacts object store)' $Yes) {
        Write-Step 'Compacting object store'
        git gc --prune=now --quiet
        Write-Ok 'gc complete'
    }
}
Write-Ok 'Cleanup done.'
