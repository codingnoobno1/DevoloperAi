<#
.SYNOPSIS
    Push the current branch and open a Pull Request - the feature GitManager
    has no UI for. Uses the GitHub CLI when present, else the REST API.
.DESCRIPTION
    Fills the biggest gap in the git subsystem: there is no PR creation anywhere.
    Title/body are generated from the branch's commits via Groq unless supplied.
    Auth: 'gh' if it's installed & logged in, otherwise a PAT from -Token / $GH_TOKEN.
.PARAMETER Base     Target/base branch. Default: repo default (main/master).
.PARAMETER Title    PR title. Default: AI-generated from commits.
.PARAMETER Body     PR body.  Default: AI-generated summary + commit list.
.PARAMETER Draft    Open as a draft PR.
.PARAMETER Token    PAT for REST fallback. Default: $env:GH_TOKEN.
.PARAMETER NoAI     Skip Groq; use a plain title/body from the branch name & commits.
.EXAMPLE
    ./git-open-pr.ps1
    ./git-open-pr.ps1 -Base develop -Draft
#>
param(
    [string]$Base,
    [string]$Title,
    [string]$Body,
    [switch]$Draft,
    [string]$Token,
    [switch]$NoAI
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo

$branch = Get-Branch
if ($branch -in @('main','master')) { Write-Err "You are on '$branch'. Create a feature branch first (./git-new-branch.ps1)."; exit 1 }

if (-not $Base) {
    $h = Invoke-Git symbolic-ref refs/remotes/origin/HEAD
    $Base = if ($h.Code -eq 0) { ($h.Out -replace '.*origin/','') } else { 'main' }
}

# Ensure branch is published
if (-not (Get-Upstream)) { Write-Step "Publishing '$branch'"; $p = Invoke-Git push --set-upstream origin HEAD; if ($p.Code -ne 0) { Write-Err $p.Out; exit 1 } }
else { (Invoke-Git push) | Out-Null }
Write-Ok "Pushed '$branch'"

# Gather commit log for context
$commits = (git log "origin/$Base..HEAD" --pretty=format:'- %s') -join "`n"
if (-not $commits) { Write-Warn "No commits ahead of origin/$Base - nothing to PR."; exit 1 }

# Title / body
if ((-not $Title -or -not $Body) -and -not $NoAI) {
    try {
        Write-Step 'Drafting PR title & body with Groq'
        $sys = 'Given a list of git commit subjects, output a JSON object {"title":"...","body":"..."} for a pull request. ' +
               'Title: concise, imperative, <=72 chars. Body: a short summary paragraph then a "## Changes" bullet list. No code fences.'
        $json = Invoke-Groq -System $sys -User $commits -Temperature 0.3
        $obj  = $json -replace '```json','' -replace '```','' | ConvertFrom-Json
        if (-not $Title) { $Title = $obj.title }
        if (-not $Body)  { $Body  = $obj.body }
    } catch { Write-Warn "AI draft failed ($($_.Exception.Message)); falling back to plain text." }
}
if (-not $Title) { $Title = "$branch -> $Base" }
if (-not $Body)  { $Body  = "## Changes`n$commits" }

# 1) Prefer gh CLI
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    Write-Step 'Opening PR via GitHub CLI'
    $args = @('pr','create','--base',$Base,'--head',$branch,'--title',$Title,'--body',$Body)
    if ($Draft) { $args += '--draft' }
    & gh @args
    if ($LASTEXITCODE -eq 0) { Write-Ok 'PR opened.'; return } else { Write-Warn 'gh failed; trying REST API...' }
}

# 2) REST fallback
$pat = Resolve-Secret $Token 'GH_TOKEN'
if (-not $pat) { Write-Err 'No gh CLI and no token. Install gh, or set $env:GH_TOKEN (scope: repo).'; exit 1 }
$originUrl = (Invoke-Git remote get-url origin).Out
if ($originUrl -notmatch 'github\.com[:/](.+?)/(.+?)(\.git)?$') { Write-Err "origin is not a GitHub URL: $originUrl"; exit 1 }
$owner = $Matches[1]; $repo = $Matches[2]
Write-Step "Opening PR via REST: $owner/$repo ($branch -> $Base)"
$payload = @{ title=$Title; head=$branch; base=$Base; body=$Body; draft=[bool]$Draft } | ConvertTo-Json
try {
    $r = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$owner/$repo/pulls" `
            -Headers @{ Authorization="Bearer $pat"; 'User-Agent'='Syncro-Desktop'; Accept='application/vnd.github+json' } -Body $payload
    Write-Ok "PR #$($r.number) opened: $($r.html_url)"
} catch { Write-Err "REST create failed: $($_.Exception.Message)"; exit 1 }
