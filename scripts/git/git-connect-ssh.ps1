<#
.SYNOPSIS
    Set up SSH-key auth for GitHub: generate an ed25519 key if absent, load the
    agent, wire git to use it, and print the public key to register.
.DESCRIPTION
    Complements GitCredentialResolver (which only *detects* keys). Idempotent "
    re-running with an existing key just re-verifies. Never prints the private key.
.PARAMETER Email    Comment/email baked into the key. Defaults to git user.email.
.PARAMETER KeyName  Key filename under ~/.ssh. Default: id_ed25519.
.PARAMETER Host     Host to verify against. Default: github.com.
.EXAMPLE
    ./git-connect-ssh.ps1 -Email you@example.com
#>
param(
    [string]$Email,
    [string]$KeyName = 'id_ed25519',
    [string]$SshHost = 'github.com'
)
. "$PSScriptRoot\_common.ps1"

$sshDir = Join-Path $HOME '.ssh'
if (-not (Test-Path $sshDir)) { New-Item -ItemType Directory -Path $sshDir | Out-Null }
$keyPath = Join-Path $sshDir $KeyName
if (-not $Email) { $Email = (git config --global user.email) }
if (-not $Email) { $Email = "$env:USERNAME@$env:COMPUTERNAME" }

# 1. Generate key if missing (no passphrase so it works head-less; use agent for security)
if (-not (Test-Path $keyPath)) {
    Write-Step "Generating ed25519 key ($KeyName)"
    ssh-keygen -t ed25519 -C $Email -f $keyPath -N '""' | Out-Null
    Write-Ok 'Key generated'
} else { Write-Info "Key already exists: $keyPath" }

# 2. Ensure ssh-agent is running and key is loaded
Write-Step 'Loading key into ssh-agent'
try {
    Get-Service ssh-agent -ErrorAction Stop | Where-Object Status -ne 'Running' | Start-Service -ErrorAction SilentlyContinue
} catch { Write-Info 'ssh-agent service not manageable here; continuing.' }
ssh-add $keyPath 2>$null | Out-Null
Write-Ok 'Key added to agent'

# 3. Tell git to use this key (matches GitCredentialResolver.UseSshKeyAsync)
git config --global core.sshCommand "ssh -i `"$keyPath`" -o IdentitiesOnly=yes"
Write-Ok "core.sshCommand -> $KeyName"

# 4. Show public key to register
Write-Step "Add this PUBLIC key to $SshHost -> Settings > SSH keys:"
Write-Host (Get-Content "$keyPath.pub") -ForegroundColor White

# 5. Verify
Write-Step "Verifying SSH to $SshHost (after you've added the key)"
$out = ssh -o StrictHostKeyChecking=accept-new -T "git@$SshHost" 2>&1 | Out-String
if ($out -match 'successfully authenticated') { Write-Ok 'SSH authentication succeeded - account connected.' }
else { Write-Warn "Not yet authenticated. Register the key above, then re-run. Server said:"; Write-Info $out.Trim() }
