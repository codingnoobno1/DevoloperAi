<#
.SYNOPSIS
    Test the current Groq configuration: confirm the key works, list available
    models, and run a tiny live completion.
.DESCRIPTION
    A quick health check for the AI features (commit drafting, PR text). Uses the
    GROQ_API_KEY already in the environment (set it with ./enable-groq.ps1).
.PARAMETER ListModels  Also fetch and print available model ids.
.EXAMPLE
    ./groq-test.ps1
    ./groq-test.ps1 -ListModels
#>
param([switch]$ListModels)
. "$PSScriptRoot\_common.ps1"

$key = Resolve-Secret $null 'GROQ_API_KEY'
if (-not $key) { Write-Err 'GROQ_API_KEY not set. Run ./enable-groq.ps1 first.'; exit 1 }
Write-Ok "Key present ($($key.Substring([Math]::Max(0,$key.Length-4))))"

if ($ListModels) {
    Write-Step 'Fetching available models'
    try {
        $m = Invoke-RestMethod -Uri 'https://api.groq.com/openai/v1/models' -Headers @{ Authorization = "Bearer $key" }
        $m.data | Sort-Object id | ForEach-Object { Write-Info $_.id }
    } catch { Write-Warn "Could not list models: $($_.Exception.Message)" }
}

Write-Step "Running a live completion on $script:GroqModel"
try {
    $reply = Invoke-Groq -System 'You are a terse assistant. Reply with exactly: SYNCRO GROQ OK' -User 'Say the phrase.' -Temperature 0
    Write-Ok "Model replied: $($reply.Trim())"
    Write-Ok 'Groq is working - AI commit/PR drafting is ready.'
} catch { Write-Err "Completion failed: $($_.Exception.Message)"; exit 1 }
