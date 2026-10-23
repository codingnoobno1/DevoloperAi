<#
.SYNOPSIS
    Connect a GitHub account via HTTPS + Personal Access Token, without ever
    hardcoding the token. Fills the "no account-connection" gap in GitManager.
.DESCRIPTION
    1. Sets git identity (user.name / user.email) if provided or missing.
    2. Ensures the Git Credential Manager is the credential.helper.
    3. Stores the PAT in the OS credential store via 'git credential approve'
       (the token is read from -Token or the GH_TOKEN env var - never written
       to disk or echoed).
    4. Verifies with 'git ls-remote' against the repo's origin (or -TestRepo).
.PARAMETER UserName   git user.name to set (optional).
.PARAMETER Email      git user.email to set (optional).
.PARAMETER GitHubUser Your GitHub username (defaults to user.name).
.PARAMETER Token      PAT. If omitted, read from $env:GH_TOKEN. Create one at
                      https://github.com/settings/tokens (scope: repo).
.PARAMETER TestRepo   HTTPS URL to verify against. Defaults to origin.
.EXAMPLE
    $env:GH_TOKEN='ghp_xxx'; ./git-connect-github.ps1 -GitHubUser codingnoobno1
#>
param(
    [string]$UserName,
    [string]$Email,
    [string]$GitHubUser,
    [string]$Token,
    [string]$TestRepo
)
. "$PSScriptRoot\_common.ps1"

# 1. Identity
if ($UserName) { git config --global user.name  $UserName; Write-Ok "user.name  = $UserName" }
if ($Email)    { git config --global user.email $Email;    Write-Ok "user.email = $Email" }
if (-not $GitHubUser) { $GitHubUser = (git config --global user.name) }
if (-not $GitHubUser) { Write-Err 'Provide -GitHubUser (or set git user.name first).'; exit 1 }

# 2. Credential helper
$helper = git config --global credential.helper
if (-not $helper) {
    git config --global credential.helper manager
    Write-Ok 'credential.helper set to Git Credential Manager'
} else { Write-Info "credential.helper already = $helper" }

# 3. Store PAT (secretly)
$pat = Resolve-Secret $Token 'GH_TOKEN'
if (-not $pat) {
    Write-Err 'No token. Pass -Token or set $env:GH_TOKEN (never commit it). Scope needed: repo.'
    exit 1
}
Write-Step 'Storing credential for github.com'
@"
protocol=https
host=github.com
username=$GitHubUser
password=$pat
"@ | git credential approve
Write-Ok 'Credential stored in OS credential manager'

# 4. Verify
if (-not $TestRepo) { $o = Invoke-Git remote get-url origin; if ($o.Code -eq 0) { $TestRepo = $o.Out } }
if ($TestRepo -and $TestRepo -like 'https*') {
    Write-Step "Verifying access to $TestRepo"
    $r = Invoke-Git ls-remote --heads $TestRepo
    if ($r.Code -eq 0) { Write-Ok 'Authentication succeeded - account connected.' }
    else { Write-Err "Verification failed: $($r.Out)"; Write-Info 'Check the token scope (needs "repo") and that it is not expired.'; exit 1 }
} else {
    Write-Info 'No HTTPS repo to verify against; credential stored. Test later with any https clone/push.'
}
