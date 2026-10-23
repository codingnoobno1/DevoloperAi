<#
.SYNOPSIS
    Inspect and resolve merge/rebase conflicts - the structured flow GitManager
    lacks (today it just dumps the error string into a snackbar).
.DESCRIPTION
    Lists conflicted files and applies a chosen strategy. 'manual' just lists them
    and opens nothing (you edit, then re-run with -Continue). 'ours'/'theirs' apply
    per-file checkout. 'abort' aborts the in-progress operation.
.PARAMETER Strategy  ours | theirs | manual | abort. Default: manual (list only).
.PARAMETER Continue  After manually resolving, stage all and continue the merge/rebase.
.PARAMETER Yes       Auto-approve destructive strategies.
.EXAMPLE
    ./git-resolve-conflicts.ps1                 # list conflicts
    ./git-resolve-conflicts.ps1 -Strategy theirs
    ./git-resolve-conflicts.ps1 -Continue
#>
param(
    [ValidateSet('ours','theirs','manual','abort')][string]$Strategy = 'manual',
    [switch]$Continue,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo
$p = Test-InProgress

$conflicts = (git diff --name-only --diff-filter=U)
if ($Strategy -eq 'abort') {
    if (-not (Confirm-Destructive 'abort the in-progress merge/rebase' $Yes)) { Write-Warn 'Aborted.'; exit 1 }
    if ($p.Rebase) { (Invoke-Git rebase --abort) | Out-Null } else { (Invoke-Git merge --abort) | Out-Null }
    Write-Ok 'Operation aborted; tree restored.'; return
}

if ($Continue) {
    if ($conflicts) { Write-Err "Still $(@($conflicts).Count) unresolved file(s). Resolve them first:"; $conflicts | ForEach-Object { Write-Info $_ }; exit 1 }
    git add -A
    if ($p.Rebase) { git rebase --continue } else { git commit --no-edit }
    Write-Ok 'Continued.'; return
}

if (-not $conflicts) { Write-Ok 'No conflicts. Nothing to resolve.'; return }
Write-Step "$(@($conflicts).Count) conflicted file(s):"
$conflicts | ForEach-Object { Write-Host "   ! $_" -ForegroundColor Yellow }

switch ($Strategy) {
    'manual' {
        Write-Info "`nEdit each file to remove <<<<<<< ======= >>>>>>> markers, then run:"
        Write-Info '   ./git-resolve-conflicts.ps1 -Continue'
    }
    'ours'   {
        if (-not (Confirm-Destructive "keep OUR version for all $(@($conflicts).Count) file(s)" $Yes)) { exit 1 }
        $conflicts | ForEach-Object { git checkout --ours -- $_; git add $_ }
        Write-Ok "Took 'ours' for all conflicts. Run -Continue to finish."
    }
    'theirs' {
        if (-not (Confirm-Destructive "keep THEIR version for all $(@($conflicts).Count) file(s)" $Yes)) { exit 1 }
        $conflicts | ForEach-Object { git checkout --theirs -- $_; git add $_ }
        Write-Ok "Took 'theirs' for all conflicts. Run -Continue to finish."
    }
}
