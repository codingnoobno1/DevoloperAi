<#
.SYNOPSIS
    Enable Groq for Syncro: set GROQ_API_KEY, validate it against the live API,
    and persist it at User scope so the app picks it up on next launch.
.DESCRIPTION
    GroqProvider reads the key ONLY from the GROQ_API_KEY environment variable at
    construction (Initialize/ValidateConnectionAsync are otherwise never called).
    This script fills that gap: it validates with the same lightweight ping the
    provider's ValidateConnectionAsync uses, then persists the variable.
    The key is never written to any file in the repo.
.PARAMETER Key       The Groq API key (gsk_...). If omitted, read from $env:GROQ_API_KEY.
.PARAMETER Scope     Where to persist: User (default) or Process (session only).
.PARAMETER NoPersist Validate only; don't store the variable.
.EXAMPLE
    ./enable-groq.ps1 -Key gsk_xxx
    $env:GROQ_API_KEY='gsk_xxx'; ./enable-groq.ps1
#>
param(
    [string]$Key,
    [ValidateSet('User','Process')][string]$Scope = 'User',
    [switch]$NoPersist
)
. "$PSScriptRoot\_common.ps1"

$key = Resolve-Secret $Key 'GROQ_API_KEY'
if (-not $key) {
    Write-Err 'No key. Pass -Key gsk_... or set $env:GROQ_API_KEY. Get one at https://console.groq.com/keys'
    exit 1
}
if ($key -notmatch '^gsk_') { Write-Warn "Key does not start with 'gsk_' - double-check it's a Groq key." }

Write-Step 'Validating key against Groq API (llama-3.1-8b-instant ping)'
$body = @{ model = $script:GroqModel; messages = @(@{ role='user'; content='ping' }); max_tokens = 5 } | ConvertTo-Json -Depth 4
try {
    $resp = Invoke-WebRequest -Uri $script:GroqUrl -Method Post -Headers @{ Authorization = "Bearer $key" } `
                              -ContentType 'application/json' -Body $body -UseBasicParsing
    if ($resp.StatusCode -eq 200) { Write-Ok 'Key is valid - Groq responded 200.' }
    else { Write-Warn "Unexpected status $($resp.StatusCode)." }
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -eq 401) { Write-Err 'Invalid key (401 Unauthorized).'; exit 1 }
    Write-Err "Validation failed: $($_.Exception.Message)"; exit 1
}

if ($NoPersist) { Write-Info 'Validated only (-NoPersist). Not stored.'; return }

# Persist
[Environment]::SetEnvironmentVariable('GROQ_API_KEY', $key, $Scope)
$env:GROQ_API_KEY = $key   # also make it live in this session
Write-Ok "GROQ_API_KEY persisted at $Scope scope."
if ($Scope -eq 'User') { Write-Info 'Restart the Syncro app (and any open terminals) so it inherits the new variable.' }
Write-Info 'Test drafting with:  ./groq-test.ps1'
