<#
.SYNOPSIS
    Add or update a remote, converting between HTTPS and SSH forms, then verify.
.DESCRIPTION
    Fills the "no remote management" gap in GitManager. Accepts a full URL or a
    'owner/repo' shorthand and can flip an existing origin between HTTPS and SSH.
.PARAMETER Repo    Full URL (https/ssh) OR 'owner/repo' shorthand.
.PARAMETER Name    Remote name. Default: origin.
.PARAMETER As      Force URL scheme: 'https' or 'ssh'. Default: keep as given / https for shorthand.
.PARAMETER Host    Host for shorthand. Default: github.com.
.EXAMPLE
    ./git-set-remote.ps1 -Repo codingnoobno1/Syncro.Desktop
    ./git-set-remote.ps1 -Repo codingnoobno1/Syncro.Desktop -As ssh
#>
param(
    [Parameter(Mandatory)][string]$Repo,
    [string]$Name = 'origin',
    [ValidateSet('https','ssh','keep')][string]$As = 'keep',
    [string]$GitHost = 'github.com'
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo

# Normalise to owner/repo when possible
$ownerRepo = $null
if ($Repo -match 'github\.com[:/](.+?)(\.git)?$') { $ownerRepo = $Matches[1] }
elseif ($Repo -match '^[\w.-]+/[\w.-]+$')          { $ownerRepo = $Repo }

$url = switch ($As) {
    'https' { if ($ownerRepo) { "https://$GitHost/$ownerRepo.git" } else { $Repo } }
    'ssh'   { if ($ownerRepo) { "git@$GitHost`:$ownerRepo.git" }   else { $Repo } }
    default { if ($ownerRepo -and $Repo -notmatch '://|@') { "https://$GitHost/$ownerRepo.git" } else { $Repo } }
}

$exists = (Invoke-Git remote get-url $Name).Code -eq 0
if ($exists) { git remote set-url $Name $url; Write-Ok "Updated remote '$Name' -> $url" }
else         { git remote add     $Name $url; Write-Ok "Added remote '$Name' -> $url" }

Write-Step "Verifying '$Name'"
$r = Invoke-Git ls-remote --heads $Name
if ($r.Code -eq 0) { Write-Ok 'Remote reachable and authenticated.' }
else { Write-Warn "Remote set but not reachable yet: $($r.Out)"; Write-Info 'Connect an account first: ./git-connect-github.ps1 (HTTPS) or ./git-connect-ssh.ps1 (SSH).' }
