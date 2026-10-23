<#
.SYNOPSIS
    One-shot sync: fetch, fast-forward pull, then push if ahead. Ahead/behind
    aware and non-destructive - never merges or rebases silently.
.DESCRIPTION
    The scripted equivalent of GitSyncService's guarded auto-sync, but on-demand.
    If the branch has diverged (ahead AND behind) it stops and tells you to use
    git-safe-push.ps1 -Rebase, rather than guessing.
.PARAMETER Path   Repo dir (default: current).
.PARAMETER Push   Also push local commits that are ahead of upstream.
.EXAMPLE
    ./git-sync.ps1              # fetch + ff pull only
    ./git-sync.ps1 -Push       # fetch + ff pull + push
#>
param(
    [string]$Path = (Get-Location).Path,
    [switch]$Push
)
. "$PSScriptRoot\_common.ps1"
Set-Location $Path
Assert-Repo

$branch = Get-Branch
Write-Step "Syncing '$branch'"

if (Test-Dirty) { Write-Warn 'Working tree is dirty - commit or stash before syncing to avoid surprises.' }

Write-Step 'Fetching origin'
$f = Invoke-Git fetch --prune origin
if ($f.Code -ne 0) { Write-Err "Fetch failed: $($f.Out)"; exit 1 }
Write-Ok 'Fetched'

$up = Get-Upstream
if (-not $up) { Write-Warn "No upstream for '$branch' - nothing to sync. Run ./git-safe-push.ps1 to publish."; return }

$ab = Get-AheadBehind
if (-not $ab) { Write-Warn 'Could not compute ahead/behind.'; return }
Write-Info "Ahead $($ab.Ahead) / Behind $($ab.Behind) vs $up"

if ($ab.Ahead -gt 0 -and $ab.Behind -gt 0) {
    Write-Err 'Branch has DIVERGED (ahead & behind). Sync stopped to avoid a surprise merge.'
    Write-Info 'Use ./git-safe-push.ps1 -Rebase to replay your commits on top of upstream.'
    exit 1
}

if ($ab.Behind -gt 0) {
    $r = Invoke-Git pull --ff-only
    if ($r.Code -ne 0) { Write-Err "FF pull failed: $($r.Out)"; exit 1 }
    Write-Ok "Fast-forwarded $($ab.Behind) commit(s)"
}

if ($Push -and $ab.Ahead -gt 0) {
    $r = Invoke-Git push
    if ($r.Code -ne 0) { Write-Err "Push failed: $($r.Out)"; exit 1 }
    Write-Ok "Pushed $($ab.Ahead) commit(s)"
} elseif ($ab.Ahead -gt 0) {
    Write-Info "$($ab.Ahead) local commit(s) not pushed. Re-run with -Push."
}
Write-Ok 'In sync.'
