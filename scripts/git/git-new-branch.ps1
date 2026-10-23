<#
.SYNOPSIS
    Create a feature branch from an up-to-date base, optionally publishing it.
.DESCRIPTION
    Fetches, fast-forwards the base branch, then branches off it - so new work
    never starts from a stale base. Enforces a naming convention by default.
.PARAMETER Name       New branch name (a 'feat/'/'fix/' prefix is added if missing).
.PARAMETER Base       Base branch to branch from. Default: repo default (main/master).
.PARAMETER Type       Prefix when Name has none: feat|fix|chore|docs. Default: feat.
.PARAMETER Publish     Push the new branch with -u after creating it.
.EXAMPLE
    ./git-new-branch.ps1 -Name login-page
    ./git-new-branch.ps1 -Name hotfix-crash -Type fix -Publish
#>
param(
    [Parameter(Mandatory)][string]$Name,
    [string]$Base,
    [ValidateSet('feat','fix','chore','docs','refactor')][string]$Type = 'feat',
    [switch]$Publish
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo

if (Test-Dirty) { Write-Warn 'Working tree is dirty - commit or stash first so it moves with you cleanly.' }

# Resolve default base if not given
if (-not $Base) {
    $h = Invoke-Git symbolic-ref refs/remotes/origin/HEAD
    if ($h.Code -eq 0) { $Base = ($h.Out -replace '.*origin/','') } else { $Base = 'main' }
}
# Normalise name to convention
if ($Name -notmatch '^(feat|fix|chore|docs|refactor)/') { $Name = "$Type/$Name" }
$Name = $Name.ToLower() -replace '[^a-z0-9/_-]','-'

Write-Step "Updating base '$Base'"
(Invoke-Git fetch origin) | Out-Null
$co = Invoke-Git checkout $Base
if ($co.Code -ne 0) { Write-Err "Cannot checkout base '$Base': $($co.Out)"; exit 1 }
(Invoke-Git pull --ff-only) | Out-Null

Write-Step "Creating '$Name' from '$Base'"
$r = Invoke-Git checkout -b $Name
if ($r.Code -ne 0) { Write-Err $r.Out; exit 1 }
Write-Ok "On new branch '$Name'"

if ($Publish) {
    $p = Invoke-Git push --set-upstream origin HEAD
    if ($p.Code -eq 0) { Write-Ok "Published '$Name' to origin" } else { Write-Warn "Publish failed: $($p.Out)" }
}
