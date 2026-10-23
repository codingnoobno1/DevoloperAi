<#
.SYNOPSIS
    Diagnose the health of a git repository and (optionally) apply safe repairs.
.DESCRIPTION
    Read-only by default. Checks for the failure modes Syncro's GitEngine can't
    recover from on its own: detached HEAD, stale index.lock, in-progress
    merge/rebase, missing upstream, ahead/behind drift, unreachable remote and
    oversized tracked files. With -Fix it applies only the *safe* repairs and
    delegates the rest to git-repair.ps1.
.PARAMETER Path
    Repo working directory. Defaults to the current directory.
.PARAMETER Fix
    Apply safe automatic repairs (remove stale lock, set upstream, prune).
.EXAMPLE
    ./git-doctor.ps1 -Path D:\myrepo
    ./git-doctor.ps1 -Fix
#>
param(
    [string]$Path = (Get-Location).Path,
    [switch]$Fix
)
. "$PSScriptRoot\_common.ps1"
Set-Location $Path
Assert-Repo

$issues = New-Object System.Collections.Generic.List[string]

Write-Step "Repository: $(Get-RepoRoot)"

# 1. Detached HEAD
if (Test-Detached) { $issues.Add('Detached HEAD (not on a branch)'); Write-Warn 'HEAD is detached' }
else { Write-Ok "On branch '$(Get-Branch)'" }

# 2. In-progress operations / stale lock
$p = Test-InProgress
if ($p.IndexLock)  { $issues.Add('Stale .git/index.lock'); Write-Warn 'index.lock present' }
if ($p.Merge)      { $issues.Add('Merge in progress');      Write-Warn 'unfinished merge' }
if ($p.Rebase)     { $issues.Add('Rebase in progress');     Write-Warn 'unfinished rebase' }
if ($p.CherryPick) { $issues.Add('Cherry-pick in progress');Write-Warn 'unfinished cherry-pick' }
if ($p.Revert)     { $issues.Add('Revert in progress');     Write-Warn 'unfinished revert' }

# 3. Working tree cleanliness
if (Test-Dirty) { Write-Warn 'Working tree has uncommitted changes'; Write-Info ((Invoke-Git status --short).Out) }
else { Write-Ok 'Working tree clean' }

# 4. Upstream + drift
$up = Get-Upstream
if (-not $up) {
    $issues.Add("No upstream set for '$(Get-Branch)'")
    Write-Warn "Branch '$(Get-Branch)' has no upstream tracking branch"
} else {
    Write-Ok "Upstream: $up"
    $ab = Get-AheadBehind
    if ($ab) {
        if ($ab.Ahead -gt 0)  { Write-Info "Ahead of upstream by $($ab.Ahead) commit(s)" }
        if ($ab.Behind -gt 0) { Write-Info "Behind upstream by $($ab.Behind) commit(s)" }
        if ($ab.Ahead -gt 0 -and $ab.Behind -gt 0) { $issues.Add('Diverged from upstream (ahead & behind)') }
    }
}

# 5. Remote reachability (fast, no fetch)
$origin = (Invoke-Git remote get-url origin)
if ($origin.Code -ne 0) { $issues.Add('No "origin" remote configured'); Write-Warn 'origin remote missing' }
else {
    Write-Info "origin -> $($origin.Out)"
    $ls = Invoke-Git ls-remote --heads origin
    if ($ls.Code -ne 0) { $issues.Add('Remote unreachable / auth failing'); Write-Warn "Cannot reach origin: $($ls.Out)" }
    else { Write-Ok 'Remote reachable (auth OK)' }
}

# 6. Oversized tracked files (>25 MB) - common push-rejection cause
$big = git ls-files | ForEach-Object {
    $f = Get-Item -LiteralPath $_ -ErrorAction SilentlyContinue
    if ($f -and -not $f.PSIsContainer -and $f.Length -gt 25MB) { "{0} ({1:N1} MB)" -f $_, ($f.Length/1MB) }
}
if ($big) { $issues.Add('Large tracked files present'); Write-Warn 'Files >25MB tracked (may block push / bloat repo):'; $big | ForEach-Object { Write-Info $_ } }

# ---- verdict ---------------------------------------------------------------
Write-Step 'Diagnosis'
if ($issues.Count -eq 0) { Write-Ok 'No problems detected. Repo is healthy.'; return }
Write-Warn "$($issues.Count) issue(s) found:"
$issues | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }

if (-not $Fix) {
    Write-Info "`nRe-run with -Fix to apply safe repairs, or run ./git-repair.ps1 for deeper recovery."
    exit 1
}

# ---- safe auto-repairs -----------------------------------------------------
Write-Step 'Applying safe repairs (-Fix)'
if ($p.IndexLock -and -not ($p.Merge -or $p.Rebase)) {
    Remove-Item "$(Get-RepoRoot)/.git/index.lock" -Force; Write-Ok 'Removed stale index.lock'
}
if (-not $up -and -not (Test-Detached)) {
    $r = Invoke-Git push --set-upstream origin HEAD
    if ($r.Code -eq 0) { Write-Ok "Set upstream to origin/$(Get-Branch)" } else { Write-Warn "Could not set upstream: $($r.Out)" }
}
Invoke-Git remote prune origin | Out-Null; Write-Ok 'Pruned stale remote-tracking branches'
Write-Info 'For merge/rebase conflicts run ./git-resolve-conflicts.ps1; for corruption run ./git-repair.ps1.'
