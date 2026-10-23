<#
.SYNOPSIS
    Push the current branch safely: set upstream if missing, integrate remote
    first, and refuse force-push unless explicitly requested.
.DESCRIPTION
    Mirrors GitEngine.PushAsync but adds the guard-rails the desktop UI's
    two-click push lacks. By default it will NOT force-push. If the branch has
    no upstream it uses --set-upstream origin HEAD (same as the engine).
.PARAMETER Path        Repo dir (default: current).
.PARAMETER Rebase      Integrate remote via 'pull --rebase' instead of --ff-only before pushing.
.PARAMETER Force       Allow a force push - uses the safer --force-with-lease.
.PARAMETER Yes         Auto-approve the force confirmation (non-interactive).
.EXAMPLE
    ./git-safe-push.ps1
    ./git-safe-push.ps1 -Rebase
    ./git-safe-push.ps1 -Force        # force-with-lease, still asks to confirm
#>
param(
    [string]$Path = (Get-Location).Path,
    [switch]$Rebase,
    [switch]$Force,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Set-Location $Path
Assert-Repo

if (Test-Detached) { Write-Err 'Detached HEAD - checkout a branch before pushing.'; exit 1 }
$branch = Get-Branch
Write-Step "Preparing to push '$branch'"

# Block pushing to protected branches unless forced-in explicitly
if ($branch -in @('main','master','release') -and -not $Force) {
    Write-Warn "'$branch' is a protected branch. Use a feature branch (./git-new-branch.ps1) or pass -Force."
}

$up = Get-Upstream
if (-not $up) {
    Write-Info "No upstream - will publish with --set-upstream origin HEAD"
    $r = Invoke-Git push --set-upstream origin HEAD
    if ($r.Code -eq 0) { Write-Ok "Published '$branch' to origin"; return } else { Write-Err $r.Out; exit 1 }
}

# Integrate remote changes first so we never clobber teammates
Write-Step 'Fetching origin'
(Invoke-Git fetch origin) | Out-Null
$ab = Get-AheadBehind
if ($ab -and $ab.Behind -gt 0) {
    if ($Rebase) {
        Write-Step "Behind by $($ab.Behind) - rebasing onto upstream"
        $r = Invoke-Git pull --rebase
    } else {
        Write-Step "Behind by $($ab.Behind) - fast-forward pull"
        $r = Invoke-Git pull --ff-only
    }
    if ($r.Code -ne 0) {
        Write-Err "Could not integrate remote changes:`n$($r.Out)"
        Write-Info 'Resolve with ./git-resolve-conflicts.ps1, then re-run this script.'
        exit 1
    }
    Write-Ok 'Remote changes integrated'
}

# Push
if ($Force) {
    if (-not (Confirm-Destructive "force-push '$branch' (--force-with-lease)" $Yes)) { Write-Warn 'Aborted.'; exit 1 }
    $r = Invoke-Git push --force-with-lease
} else {
    $r = Invoke-Git push
}
if ($r.Code -eq 0) { Write-Ok "Pushed '$branch' to $up" } else { Write-Err $r.Out; exit 1 }
