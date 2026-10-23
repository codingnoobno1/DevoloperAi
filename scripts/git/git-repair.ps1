<#
.SYNOPSIS
    Recover a broken repository: stale locks, aborted operations, corruption.
.DESCRIPTION
    Deeper recovery than git-doctor. Aborts any in-progress merge/rebase/
    cherry-pick/revert, clears a stale index.lock, refreshes the index, runs
    fsck, and re-establishes the upstream if it went 'gone'. Destructive steps
    require -Yes (so it can run head-less through ShellMcp / GitWorkflowRunner).
.PARAMETER Path
    Repo working directory. Defaults to the current directory.
.PARAMETER AbortOps
    Abort in-progress merge/rebase/cherry-pick/revert (safe: restores pre-op state).
.PARAMETER Fsck
    Run 'git fsck --full' and report dangling/corrupt objects.
.PARAMETER Yes
    Auto-approve prompts (non-interactive).
.EXAMPLE
    ./git-repair.ps1 -AbortOps -Fsck
#>
param(
    [string]$Path = (Get-Location).Path,
    [switch]$AbortOps,
    [switch]$Fsck,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Set-Location $Path
Assert-Repo
$root = Get-RepoRoot
$p = Test-InProgress

# 1. Stale index.lock (only when no live operation owns it)
if ($p.IndexLock) {
    if (-not ($p.Merge -or $p.Rebase -or $p.CherryPick)) {
        Remove-Item "$root/.git/index.lock" -Force
        Write-Ok 'Removed stale .git/index.lock'
    } else {
        Write-Warn 'index.lock belongs to a live merge/rebase - resolve or abort it first (see -AbortOps)'
    }
}

# 2. Abort in-progress operations
if ($AbortOps) {
    if ($p.Merge)      { (Invoke-Git merge --abort)      | Out-Null; Write-Ok 'Merge aborted' }
    if ($p.Rebase)     { (Invoke-Git rebase --abort)     | Out-Null; Write-Ok 'Rebase aborted' }
    if ($p.CherryPick) { (Invoke-Git cherry-pick --abort)| Out-Null; Write-Ok 'Cherry-pick aborted' }
    if ($p.Revert)     { (Invoke-Git revert --abort)     | Out-Null; Write-Ok 'Revert aborted' }
    if (-not ($p.Merge -or $p.Rebase -or $p.CherryPick -or $p.Revert)) { Write-Info 'No operation in progress to abort' }
}

# 3. Refresh the index (fixes 'unmerged'/stat cache weirdness)
Write-Step 'Refreshing index'
(Invoke-Git update-index --refresh) | Out-Null
(Invoke-Git status --short) | Out-Null
Write-Ok 'Index refreshed'

# 4. Re-set a "gone" upstream
$up = Get-Upstream
if (-not $up -and -not (Test-Detached)) {
    if (Confirm-Destructive "set upstream to origin/$(Get-Branch)" $Yes) {
        $r = Invoke-Git push --set-upstream origin HEAD
        if ($r.Code -eq 0) { Write-Ok "Upstream re-set to origin/$(Get-Branch)" } else { Write-Warn "Failed: $($r.Out)" }
    }
}

# 5. Optional integrity check
if ($Fsck) {
    Write-Step 'Running git fsck --full (read-only integrity check)'
    $r = Invoke-Git fsck --full --strict
    Write-Info $r.Out
    if ($r.Out -match 'missing|corrupt|bad ') { Write-Err 'Corruption detected - consider re-cloning; do NOT gc --prune until backed up.' }
    else { Write-Ok 'No object corruption found' }
}

Write-Step 'Repair complete'
Write-Info "Run ./git-doctor.ps1 to re-verify."
